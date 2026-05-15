using System.Text.Json;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using PortableContextMcpServer.Configuration;
using PortableContextMcpServer.Contracts;
using PortableContextMcpServer.Handlers;
using PortableContextMcpServer.Models;
using PortableContextMcpServer.Services;

namespace PortableContextMcpServer.Tests;

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

        var embeddingSettings = Options.Create(new EmbeddingSettings());
        var sqliteVecSettings = Options.Create(new SqliteVecSettings());
        var repository = new ContextRepository(config, embeddingSettings, sqliteVecSettings, NullLogger<ContextRepository>.Instance);
        await repository.InitializeAsync();

        var embeddingService = new EmbeddingService(embeddingSettings);
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
    public async Task InitializeCreatesMigrationAndContextTables()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"context-{Guid.NewGuid()}.db");
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:Path"] = tempPath,
                ["SqliteVec:Enabled"] = "false"
            })
            .Build();

        var repository = new ContextRepository(
            config,
            Options.Create(new EmbeddingSettings()),
            Options.Create(new SqliteVecSettings { Enabled = false }),
            NullLogger<ContextRepository>.Instance);

        await repository.InitializeAsync();
        await repository.InitializeAsync();

        using var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={tempPath}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name IN ('schema_migrations', 'context_entries');";

        Assert.Equal(2L, command.ExecuteScalar());
    }

    [Fact]
    public async Task RequiredSqliteVecCreatesVectorTableAndSearches()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"context-{Guid.NewGuid()}.db");
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:Path"] = tempPath,
                ["SqliteVec:Enabled"] = "true",
                ["SqliteVec:Required"] = "true"
            })
            .Build();

        var embeddingSettings = Options.Create(new EmbeddingSettings());
        var repository = new ContextRepository(
            config,
            embeddingSettings,
            Options.Create(new SqliteVecSettings { Enabled = true, Required = true }),
            NullLogger<ContextRepository>.Instance);
        var embeddingService = new EmbeddingService(embeddingSettings);

        await repository.InitializeAsync();
        var embedding = await embeddingService.CreateEmbeddingAsync("sqlite vec search");
        var entry = await repository.SaveAsync(new ContextEntry
        {
            Text = "sqlite vec search",
            Category = "memory",
            SourceTool = "unit-test",
            Embedding = embedding
        });

        var results = await repository.SearchAsync(embedding, 3);

        Assert.Contains(results, result => result.Entry.Id == entry.Id);

        using var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={tempPath}");
        connection.Open();
        connection.LoadExtension("vec0");
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM context_vectors;";

        Assert.Equal(1L, command.ExecuteScalar());
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

    [Fact]
    public async Task SdkStdioServerExposesAndRunsContextTools()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"context-{Guid.NewGuid()}.db");
        var serverDll = Path.Combine(AppContext.BaseDirectory, "PortableContextMcpServer.dll");
        var transport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Name = "portable-context-mcp-server-test",
            Command = "dotnet",
            Arguments = [serverDll, "--transport", "stdio"],
            WorkingDirectory = AppContext.BaseDirectory,
            EnvironmentVariables = new Dictionary<string, string?>
            {
                ["DATABASE__PATH"] = tempPath,
                ["SQLITEVEC__ENABLED"] = "true",
                ["SQLITEVEC__REQUIRED"] = "true"
            }
        });

        await using var client = await McpClient.CreateAsync(transport);
        var tools = await client.ListToolsAsync();

        Assert.Contains(tools, tool => tool.Name == "get_context");
        Assert.Contains(tools, tool => tool.Name == "update_context");

        var updateResult = await client.CallToolAsync(
            "update_context",
            new Dictionary<string, object?>
            {
                ["text"] = "I prefer local SQLite vector search.",
                ["category"] = "preference",
                ["sourceTool"] = "sdk-test"
            },
            cancellationToken: CancellationToken.None);

        Assert.False(updateResult.IsError is true);

        var getResult = await client.CallToolAsync(
            "get_context",
            new Dictionary<string, object?>
            {
                ["query"] = "local SQLite vector search",
                ["toolId"] = "sdk-test",
                ["maxResults"] = 5
            },
            cancellationToken: CancellationToken.None);

        Assert.False(getResult.IsError is true);
        var text = Assert.IsType<TextContentBlock>(Assert.Single(getResult.Content)).Text;
        using var json = JsonDocument.Parse(text);

        Assert.NotEmpty(json.RootElement.GetProperty("entries").EnumerateArray());
    }

    [Fact]
    public async Task HttpServerRequiresBearerTokenAndRunsContextTools()
    {
        var port = GetFreeTcpPort();
        var token = $"test-token-{Guid.NewGuid():N}";
        var tempPath = Path.Combine(Path.GetTempPath(), $"context-{Guid.NewGuid()}.db");
        var serverDll = Path.Combine(AppContext.BaseDirectory, "PortableContextMcpServer.dll");
        using var process = StartHttpServer(serverDll, port, tempPath, token);

        try
        {
            await WaitForHealthAsync(port);

            using var httpClient = new HttpClient();
            using var unauthorizedResponse = await httpClient.PostAsync(
                $"http://127.0.0.1:{port}/mcp",
                new StringContent("{}", System.Text.Encoding.UTF8, "application/json"));

            Assert.Equal(HttpStatusCode.Unauthorized, unauthorizedResponse.StatusCode);

            var transport = new HttpClientTransport(new HttpClientTransportOptions
            {
                Name = "portable-context-mcp-http-test",
                Endpoint = new Uri($"http://127.0.0.1:{port}/mcp"),
                TransportMode = HttpTransportMode.StreamableHttp,
                AdditionalHeaders = new Dictionary<string, string>
                {
                    ["Authorization"] = $"Bearer {token}"
                }
            });

            await using var client = await McpClient.CreateAsync(transport);
            var tools = await client.ListToolsAsync();

            Assert.Contains(tools, tool => tool.Name == "get_context");
            Assert.Contains(tools, tool => tool.Name == "update_context");

            var updateResult = await client.CallToolAsync(
                "update_context",
                new Dictionary<string, object?>
                {
                    ["text"] = "HTTP MCP can store local SQLite context.",
                    ["category"] = "memory",
                    ["sourceTool"] = "http-test"
                },
                cancellationToken: CancellationToken.None);

            Assert.False(updateResult.IsError is true);

            var getResult = await client.CallToolAsync(
                "get_context",
                new Dictionary<string, object?>
                {
                    ["query"] = "HTTP MCP SQLite context",
                    ["toolId"] = "http-test",
                    ["maxResults"] = 5
                },
                cancellationToken: CancellationToken.None);

            Assert.False(getResult.IsError is true);
            var text = Assert.IsType<TextContentBlock>(Assert.Single(getResult.Content)).Text;
            using var json = JsonDocument.Parse(text);

            Assert.NotEmpty(json.RootElement.GetProperty("entries").EnumerateArray());
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
    }

    private static Process StartHttpServer(string serverDll, int port, string databasePath, string token)
    {
        var processStartInfo = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = AppContext.BaseDirectory,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false
        };

        processStartInfo.ArgumentList.Add(serverDll);
        processStartInfo.ArgumentList.Add("--transport");
        processStartInfo.ArgumentList.Add("http");
        processStartInfo.EnvironmentVariables["ASPNETCORE_URLS"] = $"http://127.0.0.1:{port}";
        processStartInfo.EnvironmentVariables["DATABASE__PATH"] = databasePath;
        processStartInfo.EnvironmentVariables["SQLITEVEC__ENABLED"] = "true";
        processStartInfo.EnvironmentVariables["SQLITEVEC__REQUIRED"] = "true";
        processStartInfo.EnvironmentVariables["AUTH__MODE"] = "BearerToken";
        processStartInfo.EnvironmentVariables["AUTH__TOKENS__0"] = token;

        return Process.Start(processStartInfo) ?? throw new InvalidOperationException("Failed to start HTTP MCP server.");
    }

    private static async Task WaitForHealthAsync(int port)
    {
        using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(1) };
        var deadline = DateTimeOffset.UtcNow.AddSeconds(10);
        Exception? lastException = null;

        while (DateTimeOffset.UtcNow < deadline)
        {
            try
            {
                using var response = await httpClient.GetAsync($"http://127.0.0.1:{port}/healthz");
                if (response.IsSuccessStatusCode)
                {
                    return;
                }
            }
            catch (Exception exception)
            {
                lastException = exception;
            }

            await Task.Delay(100);
        }

        throw new TimeoutException("HTTP MCP server did not become healthy.", lastException);
    }

    private static int GetFreeTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
