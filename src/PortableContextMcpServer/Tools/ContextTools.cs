using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using PortableContextMcpServer.Contracts;
using PortableContextMcpServer.Handlers;
using PortableContextMcpServer.Models;

namespace PortableContextMcpServer.Tools;

[McpServerToolType]
public sealed class ContextTools
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    [McpServerTool(Name = "get_context")]
    [Description("Retrieve personal context entries by semantic similarity.")]
    public static async Task<string> GetContextAsync(
        McpRequestHandler handler,
        [Description("Semantic query text used to search saved context.")] string query,
        [Description("Identifier of the calling MCP client or tool.")] string toolId,
        [Description("Maximum number of matching entries to return, from 1 to 100.")] int maxResults = 10)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            throw new ArgumentException("query is required.", nameof(query));
        }

        if (string.IsNullOrWhiteSpace(toolId))
        {
            throw new ArgumentException("toolId is required.", nameof(toolId));
        }

        var response = await handler.HandleGetContextAsync(new GetContextRequest
        {
            Query = query,
            ToolId = toolId,
            MaxResults = Math.Clamp(maxResults, 1, 100)
        });

        return JsonSerializer.Serialize(new { entries = response.Entries }, SerializerOptions);
    }

    [McpServerTool(Name = "update_context")]
    [Description("Save a personal context entry for later semantic retrieval.")]
    public static async Task<string> UpdateContextAsync(
        McpRequestHandler handler,
        [Description("Context text to save.")] string text,
        [Description("Category for the context, such as preference, memory, skill, experience, relationship, or project.")] string category,
        [Description("Identifier of the client or tool that provided this context.")] string sourceTool)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new ArgumentException("text is required.", nameof(text));
        }

        if (string.IsNullOrWhiteSpace(category))
        {
            throw new ArgumentException("category is required.", nameof(category));
        }

        if (string.IsNullOrWhiteSpace(sourceTool))
        {
            throw new ArgumentException("sourceTool is required.", nameof(sourceTool));
        }

        var response = await handler.HandleUpdateContextAsync(new UpdateContextRequest
        {
            Text = text,
            Category = category,
            SourceTool = sourceTool,
            VisibilityPolicy = VisibilityPolicy.Default
        });

        return JsonSerializer.Serialize(response, SerializerOptions);
    }
}
