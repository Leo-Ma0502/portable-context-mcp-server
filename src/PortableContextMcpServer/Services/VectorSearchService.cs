using Microsoft.Extensions.Logging;

namespace PortableContextMcpServer.Services;

public sealed class VectorSearchService : IVectorSearchService
{
    private readonly ILogger<VectorSearchService> _logger;
    private bool _sqliteVecAvailable = false;

    public VectorSearchService(ILogger<VectorSearchService> logger)
    {
        _logger = logger;
    }

    public void SetVectorExtensionAvailable(bool available)
    {
        _sqliteVecAvailable = available;
        if (available)
        {
            _logger.LogInformation("sqlite-vec vector extension is available");
        }
        else
        {
            _logger.LogInformation("Using in-memory vector similarity search");
        }
    }

    public float ComputeSimilarity(float[] query, float[] candidate)
    {
        if (query.Length != candidate.Length || query.Length == 0)
        {
            return 0f;
        }

        var dot = 0f;
        for (var i = 0; i < query.Length; i++)
        {
            dot += query[i] * candidate[i];
        }

        return dot;
    }

    public Task<int[]> SearchSimilarVectorsAsync(float[] queryVector, IReadOnlyList<float[]> allVectors, int limit)
    {
        var results = new List<(int Index, float Score)>();

        for (var i = 0; i < allVectors.Count; i++)
        {
            var score = ComputeSimilarity(queryVector, allVectors[i]);
            results.Add((i, score));
        }

        var topResults = results
            .OrderByDescending(x => x.Score)
            .Take(limit)
            .Select(x => x.Index)
            .ToArray();

        return Task.FromResult(topResults);
    }
}
