using System.Security.Cryptography;
using System.Text;

namespace PortableContextMcpServer.Services;

public sealed class EmbeddingService : IEmbeddingService
{
    private const int Dimension = 128;

    public Task<float[]> CreateEmbeddingAsync(string text)
    {
        var normalized = text.Trim().ToLowerInvariant();
        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(normalized));
        var vector = new float[Dimension];

        for (var index = 0; index < Dimension; index++)
        {
            vector[index] = (hash[index % hash.Length] - 128) / 128f;
        }

        Normalize(vector);
        return Task.FromResult(vector);
    }

    private static void Normalize(float[] vector)
    {
        var length = MathF.Sqrt(vector.Sum(value => value * value));
        if (length <= 0f)
        {
            return;
        }

        for (var index = 0; index < vector.Length; index++)
        {
            vector[index] /= length;
        }
    }
}
