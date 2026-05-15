using PortableContextMcpServer.Models;

namespace PortableContextMcpServer.Contracts;

public sealed class UpdateContextRequest
{
    public string Text { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public string SourceTool { get; init; } = string.Empty;
    public VisibilityPolicy? VisibilityPolicy { get; init; }
}
