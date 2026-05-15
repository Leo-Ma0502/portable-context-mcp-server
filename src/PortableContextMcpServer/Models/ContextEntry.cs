namespace PortableContextMcpServer.Models;

public sealed class ContextEntry
{
    public string Id { get; init; } = Guid.NewGuid().ToString("D");
    public string Category { get; init; } = string.Empty;
    public string Text { get; init; } = string.Empty;
    public float[] Embedding { get; init; } = Array.Empty<float>();
    public string SourceTool { get; init; } = string.Empty;
    public VisibilityPolicy VisibilityPolicy { get; init; } = VisibilityPolicy.Default;
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; init; } = DateTimeOffset.UtcNow;
}
