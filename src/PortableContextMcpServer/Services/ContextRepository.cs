using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using PortableContextMcpServer.Configuration;
using PortableContextMcpServer.Models;

namespace PortableContextMcpServer.Services;

public sealed class ContextRepository : IContextRepository
{
    private readonly string _databasePath;
    private readonly IVectorSearchService _vectorSearchService;
    private readonly JsonSerializerOptions _serializerOptions = new(JsonSerializerDefaults.Web);
    private readonly object _lock = new();

    public ContextRepository(IConfiguration configuration, IVectorSearchService vectorSearchService)
    {
        var settings = configuration.GetSection("Database").Get<DatabaseSettings>() ?? new DatabaseSettings();
        _databasePath = settings.Path;
        _vectorSearchService = vectorSearchService;
    }

    public Task InitializeAsync()
    {
        var folder = Path.GetDirectoryName(_databasePath);
        if (!string.IsNullOrEmpty(folder))
        {
            Directory.CreateDirectory(folder);
        }

        using var connection = new SqliteConnection($"Data Source={_databasePath}");
        connection.Open();

        var vectorExtensionAvailable = TryLoadVectorExtension(connection);
        if (_vectorSearchService is VectorSearchService vectorSearch)
        {
            vectorSearch.SetVectorExtensionAvailable(vectorExtensionAvailable);
        }

        using var command = connection.CreateCommand();
        command.CommandText = "CREATE TABLE IF NOT EXISTS ContextEntries (Id TEXT PRIMARY KEY, Category TEXT, Text TEXT, EmbeddingJson TEXT, SourceTool TEXT, VisibilityPolicyJson TEXT, CreatedAt TEXT, UpdatedAt TEXT);";
        command.ExecuteNonQuery();

        return Task.CompletedTask;
    }

    private static bool TryLoadVectorExtension(SqliteConnection connection)
    {
        try
        {
            var command = connection.CreateCommand();
            command.CommandText = "SELECT load_extension('vec0')";
            command.ExecuteNonQuery();
            return true;
        }
        catch
        {
            return false;
        }
    }

    public Task<ContextEntry> SaveAsync(ContextEntry entry)
    {
        lock (_lock)
        {
            using var connection = new SqliteConnection($"Data Source={_databasePath}");
            connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText = "INSERT OR REPLACE INTO ContextEntries (Id, Category, Text, EmbeddingJson, SourceTool, VisibilityPolicyJson, CreatedAt, UpdatedAt) VALUES ($id, $category, $text, $embedding, $sourceTool, $policy, $createdAt, $updatedAt);";
            command.Parameters.AddWithValue("$id", entry.Id);
            command.Parameters.AddWithValue("$category", entry.Category);
            command.Parameters.AddWithValue("$text", entry.Text);
            command.Parameters.AddWithValue("$embedding", JsonSerializer.Serialize(entry.Embedding, _serializerOptions));
            command.Parameters.AddWithValue("$sourceTool", entry.SourceTool);
            command.Parameters.AddWithValue("$policy", JsonSerializer.Serialize(entry.VisibilityPolicy, _serializerOptions));
            command.Parameters.AddWithValue("$createdAt", entry.CreatedAt.ToString("O"));
            command.Parameters.AddWithValue("$updatedAt", entry.UpdatedAt.ToString("O"));
            command.ExecuteNonQuery();
        }

        return Task.FromResult(entry);
    }

    public Task<IReadOnlyList<ContextEntry>> SearchAsync(float[] queryEmbedding, int limit)
    {
        using var connection = new SqliteConnection($"Data Source={_databasePath}");
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, Category, Text, EmbeddingJson, SourceTool, VisibilityPolicyJson, CreatedAt, UpdatedAt FROM ContextEntries;";

        using var reader = command.ExecuteReader();
        var entries = new List<ContextEntry>();
        var embeddings = new List<float[]>();

        while (reader.Read())
        {
            var entry = new ContextEntry
            {
                Id = reader.GetString(0),
                Category = reader.GetString(1),
                Text = reader.GetString(2),
                Embedding = JsonSerializer.Deserialize<float[]>(reader.GetString(3), _serializerOptions) ?? Array.Empty<float>(),
                SourceTool = reader.GetString(4),
                VisibilityPolicy = JsonSerializer.Deserialize<VisibilityPolicy>(reader.GetString(5), _serializerOptions) ?? VisibilityPolicy.Default,
                CreatedAt = DateTimeOffset.Parse(reader.GetString(6)),
                UpdatedAt = DateTimeOffset.Parse(reader.GetString(7))
            };

            entries.Add(entry);
            embeddings.Add(entry.Embedding);
        }

        var similarIndices = _vectorSearchService.SearchSimilarVectorsAsync(queryEmbedding, embeddings, limit).Result;
        var results = similarIndices.Select(index => entries[index]).ToList();

        return Task.FromResult<IReadOnlyList<ContextEntry>>(results);
    }

    private static float ComputeSimilarity(float[] query, float[] candidate)
    {
        if (query.Length != candidate.Length || query.Length == 0)
        {
            return 0f;
        }

        var dot = 0f;
        for (var index = 0; index < query.Length; index++)
        {
            dot += query[index] * candidate[index];
        }

        return dot;
    }
}
