using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace PortableContextMcpServer.Services;

public sealed class DatabaseInitializationHostedService : IHostedService
{
    private readonly IContextRepository _repository;
    private readonly ILogger<DatabaseInitializationHostedService> _logger;

    public DatabaseInitializationHostedService(
        IContextRepository repository,
        ILogger<DatabaseInitializationHostedService> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Initializing context database");
        await _repository.InitializeAsync();
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
