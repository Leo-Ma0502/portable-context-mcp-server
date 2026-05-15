using PortableContextMcpServer.Models;

namespace PortableContextMcpServer.Services;

public interface IContextRepository
{
    Task InitializeAsync();
    Task<ContextEntry> SaveAsync(ContextEntry entry);
    Task<IReadOnlyList<ContextEntry>> SearchAsync(float[] queryEmbedding, int limit);
}
