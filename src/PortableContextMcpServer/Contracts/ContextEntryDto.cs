namespace PortableContextMcpServer.Contracts;

public sealed class ContextEntryDto
{
    public string Id { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public string Text { get; init; } = string.Empty;
    public string SourceTool { get; init; } = string.Empty;
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
    public float Score { get; init; }
}
