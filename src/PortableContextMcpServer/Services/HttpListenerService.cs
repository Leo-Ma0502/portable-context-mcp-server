using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PortableContextMcpServer.Configuration;
using PortableContextMcpServer.Contracts;
using PortableContextMcpServer.Handlers;

namespace PortableContextMcpServer.Services;

public sealed class HttpListenerService : BackgroundService
{
    private readonly McpRequestHandler _requestHandler;
    private readonly ILogger<HttpListenerService> _logger;
    private readonly ServerSettings _serverSettings;
    private readonly JsonSerializerOptions _serializerOptions = new(JsonSerializerDefaults.Web);

    public HttpListenerService(
        McpRequestHandler requestHandler,
        ILogger<HttpListenerService> logger,
        IConfiguration configuration)
    {
        _requestHandler = requestHandler;
        _logger = logger;
        _serverSettings = configuration.GetSection("Server").Get<ServerSettings>() ?? new ServerSettings();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var listener = new HttpListener();
        var prefix = $"http://localhost:{_serverSettings.Port}/";
        listener.Prefixes.Add(prefix);
        listener.Start();
        _logger.LogInformation("Listening on {Prefix}", prefix);

        while (!stoppingToken.IsCancellationRequested)
        {
            var context = await listener.GetContextAsync();
            _ = Task.Run(() => HandleContextAsync(context), stoppingToken);
        }

        listener.Stop();
    }

    private async Task HandleContextAsync(HttpListenerContext context)
    {
        try
        {
            var path = context.Request.Url?.AbsolutePath ?? string.Empty;
            if (path.EndsWith("/get-context", StringComparison.OrdinalIgnoreCase))
            {
                await HandleGetContextAsync(context);
                return;
            }

            if (path.EndsWith("/update-context", StringComparison.OrdinalIgnoreCase))
            {
                await HandleUpdateContextAsync(context);
                return;
            }

            context.Response.StatusCode = 404;
            await WriteResponseAsync(context, new { error = "Not found" });
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Request handling failed");
            context.Response.StatusCode = 500;
            await WriteResponseAsync(context, new { error = "Server error" });
        }
    }

    private async Task HandleGetContextAsync(HttpListenerContext context)
    {
        var request = await ReadRequestAsync<GetContextRequest>(context.Request);
        var response = await _requestHandler.HandleGetContextAsync(request);
        await WriteResponseAsync(context, response);
    }

    private async Task HandleUpdateContextAsync(HttpListenerContext context)
    {
        var request = await ReadRequestAsync<UpdateContextRequest>(context.Request);
        var response = await _requestHandler.HandleUpdateContextAsync(request);
        await WriteResponseAsync(context, response);
    }

    private async Task<T> ReadRequestAsync<T>(HttpListenerRequest request)
    {
        using var reader = new StreamReader(request.InputStream, request.ContentEncoding);
        var body = await reader.ReadToEndAsync();
        return JsonSerializer.Deserialize<T>(body, _serializerOptions) ?? throw new InvalidOperationException("Unable to deserialize request.");
    }

    private static async Task WriteResponseAsync(HttpListenerContext context, object payload)
    {
        var body = JsonSerializer.Serialize(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var bytes = Encoding.UTF8.GetBytes(body);
        context.Response.ContentType = "application/json";
        context.Response.ContentEncoding = Encoding.UTF8;
        context.Response.ContentLength64 = bytes.Length;
        await context.Response.OutputStream.WriteAsync(bytes.AsMemory(0, bytes.Length));
        context.Response.OutputStream.Close();
    }
}
