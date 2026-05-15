using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PortableContextMcpServer.Configuration;
using PortableContextMcpServer.Handlers;
using PortableContextMcpServer.Services;

var host = Host.CreateDefaultBuilder(args)
    .ConfigureAppConfiguration((context, config) =>
    {
        config.AddEnvironmentVariables();
    })
    .ConfigureServices((context, services) =>
    {
        services.Configure<DatabaseSettings>(context.Configuration.GetSection("Database"));
        services.AddSingleton<IEmbeddingService, EmbeddingService>();
        services.AddSingleton<IVectorSearchService, VectorSearchService>();
        services.AddSingleton<IContextRepository, ContextRepository>();
        services.AddSingleton<IPolicyService, PolicyService>();
        services.AddSingleton<McpRequestHandler>();
        services.AddSingleton<StdioMcpServer>();
        services.AddHostedService<McpServerHostedService>();
    })
    .Build();

await host.RunAsync();
