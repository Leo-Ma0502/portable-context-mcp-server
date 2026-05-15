namespace PortableContextMcpServer.Services;

public interface IEmbeddingService
{
    Task<float[]> CreateEmbeddingAsync(string text);
}
