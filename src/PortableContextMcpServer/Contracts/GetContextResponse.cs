namespace PortableContextMcpServer.Contracts;

public sealed class GetContextResponse
{
    public IList<ContextEntryDto> Entries { get; init; } = Array.Empty<ContextEntryDto>();
}
