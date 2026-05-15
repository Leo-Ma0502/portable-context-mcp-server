namespace PortableContextMcpServer.Configuration;

public sealed class EmbeddingSettings
{
    public string Provider { get; init; } = "Hash";
    public string Model { get; init; } = "sha256-hash-v1";
    public int Dimension { get; init; } = 128;
}
