using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PortableContextMcpServer.Configuration;

namespace PortableContextMcpServer.Services;

public sealed class EmbeddingService : IEmbeddingService
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly EmbeddingSettings _settings;
    private readonly int _dimension;
    private readonly ILogger<EmbeddingService> _logger;
    private readonly HttpClient _httpClient;

    public EmbeddingService(IOptions<EmbeddingSettings> settings)
        : this(settings, NullLogger<EmbeddingService>.Instance, new HttpClient())
    {
    }

    public EmbeddingService(IOptions<EmbeddingSettings> settings, ILogger<EmbeddingService> logger)
        : this(settings, logger, new HttpClient())
    {
    }

    public EmbeddingService(IOptions<EmbeddingSettings> settings, ILogger<EmbeddingService> logger, HttpClient httpClient)
    {
        _settings = settings.Value;
        _dimension = Math.Max(1, _settings.Dimension);
        _logger = logger;
        _httpClient = httpClient;

        var timeoutSeconds = Math.Max(1, _settings.TimeoutSeconds);
        _httpClient.Timeout = TimeSpan.FromSeconds(timeoutSeconds);
    }

    public async Task<float[]> CreateEmbeddingAsync(string text)
    {
        if (string.Equals(_settings.Provider, "Ollama", StringComparison.OrdinalIgnoreCase))
        {
            return await CreateOllamaEmbeddingOrFallbackAsync(text);
        }

        return CreateHashEmbedding(text);
    }

    private async Task<float[]> CreateOllamaEmbeddingOrFallbackAsync(string text)
    {
        try
        {
            return await CreateOllamaEmbeddingAsync(text);
        }
        catch (Exception exception) when (_settings.FallbackToHash)
        {
            _logger.LogWarning(exception, "Ollama embedding failed. Falling back to hash embedding.");
            return CreateHashEmbedding(text);
        }
    }

    private async Task<float[]> CreateOllamaEmbeddingAsync(string text)
    {
        if (string.IsNullOrWhiteSpace(_settings.Model))
        {
            throw new InvalidOperationException("Embedding:Model must be set when Embedding:Provider is Ollama.");
        }

        if (string.IsNullOrWhiteSpace(_settings.Ollama.Endpoint))
        {
            throw new InvalidOperationException("Embedding:Ollama:Endpoint must be set when Embedding:Provider is Ollama.");
        }

        var endpoint = new Uri(new Uri(_settings.Ollama.Endpoint.TrimEnd('/') + "/"), "api/embed");
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new OllamaEmbedRequest(_settings.Model, text), SerializerOptions),
                Encoding.UTF8,
                "application/json")
        };

        if (!string.IsNullOrWhiteSpace(_settings.Ollama.ApiKey))
        {
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _settings.Ollama.ApiKey);
        }

        using var response = await _httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();

        await using var responseStream = await response.Content.ReadAsStreamAsync();
        using var document = await JsonDocument.ParseAsync(responseStream);
        var embedding = ReadEmbedding(document.RootElement);

        if (embedding.Length != _dimension)
        {
            throw new InvalidOperationException($"Ollama embedding dimension {embedding.Length} does not match configured dimension {_dimension}.");
        }

        Normalize(embedding);
        return embedding;
    }

    private float[] CreateHashEmbedding(string text)
    {
        var normalized = text.Trim().ToLowerInvariant();
        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(normalized));
        var vector = new float[_dimension];

        for (var index = 0; index < _dimension; index++)
        {
            vector[index] = (hash[index % hash.Length] - 128) / 128f;
        }

        Normalize(vector);
        return vector;
    }

    private static float[] ReadEmbedding(JsonElement root)
    {
        if (root.TryGetProperty("embeddings", out var embeddings)
            && embeddings.ValueKind == JsonValueKind.Array
            && embeddings.GetArrayLength() > 0)
        {
            return ReadFloatArray(embeddings[0]);
        }

        if (root.TryGetProperty("embedding", out var embedding))
        {
            return ReadFloatArray(embedding);
        }

        throw new InvalidOperationException("Ollama embedding response did not include an embedding vector.");
    }

    private static float[] ReadFloatArray(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException("Ollama embedding vector must be a JSON array.");
        }

        var vector = new float[element.GetArrayLength()];
        var index = 0;
        foreach (var value in element.EnumerateArray())
        {
            vector[index] = value.GetSingle();
            index++;
        }

        return vector;
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

    private sealed record OllamaEmbedRequest(string Model, string Input);
}
