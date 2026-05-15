using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using PortableContextMcpServer.Contracts;
using PortableContextMcpServer.Handlers;
using PortableContextMcpServer.Models;
using PortableContextMcpServer.Services;

namespace PortableContextMcpServer.Tests;

internal sealed class TestLogger : ILogger<VectorSearchService>
{
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => false;
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) { }
}

public class RepositoryAndHandlerTests
{
    [Fact]
    public async Task SaveAndSearchContextEntryProducesRelevantResults()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"context-{Guid.NewGuid()}.db");
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:Path"] = tempPath
            })
            .Build();

        var vectorSearchService = new VectorSearchService(new TestLogger());
        var repository = new ContextRepository(config, vectorSearchService);
        await repository.InitializeAsync();

        var embeddingService = new EmbeddingService();
        var policyService = new PolicyService();
        var handler = new McpRequestHandler(repository, embeddingService, policyService);

        var updateResponse = await handler.HandleUpdateContextAsync(new UpdateContextRequest
        {
            Text = "I enjoy working with local development environments.",
            Category = "preference",
            SourceTool = "unit-test",
            VisibilityPolicy = VisibilityPolicy.Default
        });

        Assert.True(updateResponse.Success);
        Assert.False(string.IsNullOrWhiteSpace(updateResponse.EntryId));

        var getResponse = await handler.HandleGetContextAsync(new GetContextRequest
        {
            Query = "local development",
            ToolId = "unit-test",
            MaxResults = 5
        });

        Assert.NotEmpty(getResponse.Entries);
        Assert.Contains(getResponse.Entries, entry => entry.Id == updateResponse.EntryId);
    }

    [Fact]
    public void PolicyServiceDeniesEntriesWithDifferentToolId()
    {
        var policyService = new PolicyService();
        var entry = new ContextEntry
        {
            Category = "preference",
            Text = "Test record",
            SourceTool = "source-tool",
            VisibilityPolicy = new VisibilityPolicy
            {
                AllowedToolIds = new[] { "allowed-tool" },
                AllowedCategories = new[] { "preference" }
            }
        };

        Assert.False(policyService.IsVisible(entry, "other-tool"));
        Assert.True(policyService.IsVisible(entry, "allowed-tool"));
    }
}
