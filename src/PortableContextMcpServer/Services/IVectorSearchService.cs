namespace PortableContextMcpServer.Services;

public interface IVectorSearchService
{
    float ComputeSimilarity(float[] query, float[] candidate);
    Task<int[]> SearchSimilarVectorsAsync(float[] queryVector, IReadOnlyList<float[]> allVectors, int limit);
}
