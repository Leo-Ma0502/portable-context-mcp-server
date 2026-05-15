using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace PortableContextMcpServer.Services;

public sealed class McpServerHostedService : BackgroundService
{
    private readonly StdioMcpServer _mcpServer;
    private readonly ILogger<McpServerHostedService> _logger;

    public McpServerHostedService(
        StdioMcpServer mcpServer,
        ILogger<McpServerHostedService> logger)
    {
        _mcpServer = mcpServer;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("MCP Server starting on stdio");
        await _mcpServer.RunAsync(stoppingToken);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("MCP Server stopping");
        await base.StopAsync(cancellationToken);
    }
}
