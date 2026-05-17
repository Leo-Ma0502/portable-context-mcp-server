namespace PortableContextMcpServer.Configuration;

public sealed class EmbeddingSettings
{
    public string Provider { get; init; } = "Hash";
    public string Model { get; init; } = "sha256-hash-v1";
    public int Dimension { get; init; } = 128;
    public bool FallbackToHash { get; init; } = true;
    public int TimeoutSeconds { get; init; } = 10;
    public OllamaEmbeddingSettings Ollama { get; init; } = new();
}

public sealed class OllamaEmbeddingSettings
{
    public string Endpoint { get; init; } = "http://localhost:11434";
    public string? ApiKey { get; init; }
}
