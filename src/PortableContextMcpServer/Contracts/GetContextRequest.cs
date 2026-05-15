namespace PortableContextMcpServer.Contracts;

public sealed class GetContextRequest
{
    public string Query { get; init; } = string.Empty;
    public string ToolId { get; init; } = string.Empty;
    public int MaxResults { get; init; } = 10;
}
