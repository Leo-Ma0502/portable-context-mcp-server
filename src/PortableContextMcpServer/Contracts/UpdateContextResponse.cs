namespace PortableContextMcpServer.Contracts;

public sealed class UpdateContextResponse
{
    public bool Success { get; init; }
    public string EntryId { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
}
