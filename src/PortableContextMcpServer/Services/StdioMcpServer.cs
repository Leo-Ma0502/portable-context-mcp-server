using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using PortableContextMcpServer.Contracts;
using PortableContextMcpServer.Handlers;
using PortableContextMcpServer.Models;

namespace PortableContextMcpServer.Services;

public sealed class StdioMcpServer
{
    private readonly McpRequestHandler _requestHandler;
    private readonly ILogger<StdioMcpServer> _logger;

    public StdioMcpServer(
        McpRequestHandler requestHandler,
        ILogger<StdioMcpServer> logger)
    {
        _requestHandler = requestHandler;
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken stoppingToken)
    {
        try
        {
            await ProcessStdioAsync(stoppingToken);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "MCP server error");
            throw;
        }
    }

    private async Task ProcessStdioAsync(CancellationToken stoppingToken)
    {
        using var streamReader = new StreamReader(Console.OpenStandardInput(), System.Text.Encoding.UTF8, leaveOpen: true);
        var serializerOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);

        while (!stoppingToken.IsCancellationRequested)
        {
            var line = await streamReader.ReadLineAsync(stoppingToken);
            if (string.IsNullOrEmpty(line))
            {
                continue;
            }

            try
            {
                var request = JsonSerializer.Deserialize<JsonElement>(line, serializerOptions);
                var response = await HandleMcpRequestAsync(request, serializerOptions);
                var responseJson = JsonSerializer.Serialize(response, serializerOptions);
                Console.WriteLine(responseJson);
                Console.Out.Flush();
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Failed to process MCP request");
                var errorResponse = new
                {
                    jsonrpc = "2.0",
                    id = (int?)null,
                    error = new { code = -32603, message = "Internal server error" }
                };
                Console.WriteLine(JsonSerializer.Serialize(errorResponse, serializerOptions));
                Console.Out.Flush();
            }
        }
    }

    private async Task<object> HandleMcpRequestAsync(JsonElement request, JsonSerializerOptions options)
    {
        var jsonRpc = request.TryGetProperty("jsonrpc", out var jsonRpcEl) ? jsonRpcEl.GetString() : "2.0";
        var method = request.TryGetProperty("method", out var methodEl) ? methodEl.GetString() : string.Empty;
        var id = request.TryGetProperty("id", out var idEl) ? idEl.GetInt32() : 0;
        var paramsEl = request.TryGetProperty("params", out var p) ? p : default;

        return method switch
        {
            "initialize" => await HandleInitializeAsync(id),
            "tools/list" => HandleToolsList(id),
            "tools/call" => await HandleToolCallAsync(paramsEl, id, options),
            _ => new { jsonrpc = "2.0", id, error = new { code = -32601, message = "Method not found" } }
        };
    }

    private Task<object> HandleInitializeAsync(int id)
    {
        var response = new
        {
            jsonrpc = "2.0",
            id,
            result = new
            {
                protocolVersion = "2024-11-05",
                capabilities = new { },
                serverInfo = new
                {
                    name = "PortableContextMcpServer",
                    version = "0.1.0"
                }
            }
        };
        return Task.FromResult<object>(response);
    }

    private object HandleToolsList(int id)
    {
        var response = new
        {
            jsonrpc = "2.0",
            id,
            result = new
            {
                tools = (object[])new[]
                {
                    (object)new
                    {
                        name = "get_context",
                        description = "Retrieve personal context by semantic query",
                        inputSchema = new
                        {
                            type = "object",
                            properties = new
                            {
                                query = new { type = "string", description = "Semantic query text" },
                                toolId = new { type = "string", description = "Identifier of the calling tool" },
                                maxResults = new { type = "integer", description = "Maximum results to return", minimum = 1, maximum = 100 }
                            },
                            required = new[] { "query", "toolId" }
                        }
                    },
                    (object)new
                    {
                        name = "update_context",
                        description = "Save or update personal context",
                        inputSchema = new
                        {
                            type = "object",
                            properties = new
                            {
                                text = new { type = "string", description = "Context text to save" },
                                category = new { type = "string", description = "Category of the context" },
                                sourceTool = new { type = "string", description = "Identifier of the source tool" }
                            },
                            required = new[] { "text", "category", "sourceTool" }
                        }
                    }
                }
            }
        };
        return response;
    }

    private async Task<object> HandleToolCallAsync(JsonElement paramsEl, int id, JsonSerializerOptions options)
    {
        var toolName = paramsEl.TryGetProperty("name", out var nameEl) ? nameEl.GetString() : string.Empty;
        var arguments = paramsEl.TryGetProperty("arguments", out var argsEl) ? argsEl : default;

        try
        {
            return toolName switch
            {
                "get_context" => await HandleGetContextToolAsync(arguments, id, options),
                "update_context" => await HandleUpdateContextToolAsync(arguments, id, options),
                _ => new { jsonrpc = "2.0", id, error = new { code = -32601, message = "Tool not found" } }
            };
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Tool call failed for {ToolName}", toolName);
            return new
            {
                jsonrpc = "2.0",
                id,
                error = new { code = -32603, message = exception.Message }
            };
        }
    }

    private async Task<object> HandleGetContextToolAsync(JsonElement arguments, int id, JsonSerializerOptions options)
    {
        var query = arguments.TryGetProperty("query", out var q) ? q.GetString() ?? string.Empty : string.Empty;
        var toolId = arguments.TryGetProperty("toolId", out var t) ? t.GetString() ?? string.Empty : string.Empty;
        var maxResults = arguments.TryGetProperty("maxResults", out var m) && m.TryGetInt32(out var mr) ? mr : 10;

        var request = new GetContextRequest
        {
            Query = query,
            ToolId = toolId,
            MaxResults = maxResults
        };

        var response = await _requestHandler.HandleGetContextAsync(request);
        var result = JsonSerializer.Serialize(new
        {
            entries = response.Entries.Select(e => new
            {
                e.Id,
                e.Category,
                e.Text,
                e.SourceTool,
                e.CreatedAt,
                e.UpdatedAt,
                e.Score
            }).ToList()
        }, options);

        return new
        {
            jsonrpc = "2.0",
            id,
            result = new
            {
                type = "text",
                text = result
            }
        };
    }

    private async Task<object> HandleUpdateContextToolAsync(JsonElement arguments, int id, JsonSerializerOptions options)
    {
        var text = arguments.TryGetProperty("text", out var t) ? t.GetString() ?? string.Empty : string.Empty;
        var category = arguments.TryGetProperty("category", out var c) ? c.GetString() ?? string.Empty : string.Empty;
        var sourceTool = arguments.TryGetProperty("sourceTool", out var s) ? s.GetString() ?? string.Empty : string.Empty;

        var request = new UpdateContextRequest
        {
            Text = text,
            Category = category,
            SourceTool = sourceTool,
            VisibilityPolicy = VisibilityPolicy.Default
        };

        var response = await _requestHandler.HandleUpdateContextAsync(request);
        var result = JsonSerializer.Serialize(new
        {
            response.Success,
            response.EntryId,
            response.Message
        }, options);

        return new
        {
            jsonrpc = "2.0",
            id,
            result = new
            {
                type = "text",
                text = result
            }
        };
    }
}
