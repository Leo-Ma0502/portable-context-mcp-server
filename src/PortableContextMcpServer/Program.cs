using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using PortableContextMcpServer.Configuration;
using PortableContextMcpServer.Handlers;
using PortableContextMcpServer.Services;

var transport = GetTransport(args);

if (transport == "stdio")
{
    var builder = Host.CreateApplicationBuilder(args);
    ConfigureCommonServices(builder.Services, builder.Configuration);
    builder.Logging.AddConsole(options =>
    {
        options.LogToStandardErrorThreshold = LogLevel.Trace;
    });

    builder.Services
        .AddMcpServer()
        .WithStdioServerTransport()
        .WithToolsFromAssembly();

    await builder.Build().RunAsync();
    return;
}

var webBuilder = WebApplication.CreateBuilder(args);
ConfigureCommonServices(webBuilder.Services, webBuilder.Configuration);
webBuilder.Services.AddSingleton<BearerTokenValidator>();
webBuilder.Services.AddCors(options =>
{
    options.AddPolicy("McpCors", policy =>
    {
        policy
            .AllowAnyOrigin()
            .AllowAnyMethod()
            .AllowAnyHeader()
            .WithExposedHeaders("Mcp-Session-Id");
    });
});

webBuilder.Services
    .AddMcpServer()
    .WithHttpTransport(options =>
    {
        options.Stateless = true;
    })
    .WithToolsFromAssembly();

var app = webBuilder.Build();
EnsureHttpAuthConfigured(app.Services);

app.MapGet("/healthz", () => Results.Ok(new { status = "ok" }));
app.UseCors("McpCors");

app.UseWhen(
    context => context.Request.Path.StartsWithSegments("/mcp"),
    branch =>
    {
        branch.Use(async (context, next) =>
        {
            if (HttpMethods.IsOptions(context.Request.Method))
            {
                context.Response.StatusCode = StatusCodes.Status204NoContent;
                return;
            }

            var validator = context.RequestServices.GetRequiredService<BearerTokenValidator>();
            if (!validator.IsValid(context.Request.Headers.Authorization))
            {
                context.Response.Headers.WWWAuthenticate = "Bearer";
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsync("Unauthorized");
                return;
            }

            await next();
        });
    });

app.MapMcp("/mcp");

await app.RunAsync();

static void ConfigureCommonServices(IServiceCollection services, IConfiguration configuration)
{
    services.Configure<DatabaseSettings>(configuration.GetSection("Database"));
    services.Configure<EmbeddingSettings>(configuration.GetSection("Embedding"));
    services.Configure<SqliteVecSettings>(configuration.GetSection("SqliteVec"));
    services.Configure<SecuritySettings>(configuration.GetSection("Security"));
    services.Configure<AuthSettings>(configuration.GetSection("Auth"));

    services.AddSingleton<IEmbeddingService, EmbeddingService>();
    services.AddSingleton<IContextRepository, ContextRepository>();
    services.AddSingleton<IPolicyService, PolicyService>();
    services.AddSingleton<McpRequestHandler>();
    services.AddHostedService<DatabaseInitializationHostedService>();
}

static void EnsureHttpAuthConfigured(IServiceProvider services)
{
    var validator = services.GetRequiredService<BearerTokenValidator>();
    if (validator.IsEnabled && !validator.HasTokens)
    {
        throw new InvalidOperationException("HTTP MCP transport requires at least one bearer token. Set AUTH__TOKENS__0 or use AUTH__MODE=Disabled for local-only development.");
    }
}

static string GetTransport(string[] args)
{
    for (var index = 0; index < args.Length; index++)
    {
        if (string.Equals(args[index], "--transport", StringComparison.OrdinalIgnoreCase) && index + 1 < args.Length)
        {
            return args[index + 1].Trim().ToLowerInvariant();
        }
    }

    return (Environment.GetEnvironmentVariable("TRANSPORT") ?? "http").Trim().ToLowerInvariant();
}
