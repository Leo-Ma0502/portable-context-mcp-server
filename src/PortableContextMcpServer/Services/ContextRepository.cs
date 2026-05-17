using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PortableContextMcpServer.Configuration;
using PortableContextMcpServer.Models;

namespace PortableContextMcpServer.Services;

public sealed class ContextRepository : IContextRepository
{
    private const string EmbeddingDimensionMetadataKey = "embedding_dimension";

    private readonly string _databasePath;
    private readonly int _embeddingDimension;
    private readonly SqliteVecSettings _sqliteVecSettings;
    private readonly ILogger<ContextRepository> _logger;
    private readonly JsonSerializerOptions _serializerOptions = new(JsonSerializerDefaults.Web);
    private readonly SemaphoreSlim _initializeLock = new(1, 1);
    private readonly object _writeLock = new();

    private bool _initialized;
    private bool _sqliteVecAvailable;

    public ContextRepository(
        IConfiguration configuration,
        IOptions<EmbeddingSettings> embeddingSettings,
        IOptions<SqliteVecSettings> sqliteVecSettings,
        ILogger<ContextRepository> logger)
    {
        var databaseSettings = configuration.GetSection("Database").Get<DatabaseSettings>() ?? new DatabaseSettings();
        _databasePath = databaseSettings.Path;
        _embeddingDimension = Math.Max(1, embeddingSettings.Value.Dimension);
        _sqliteVecSettings = sqliteVecSettings.Value;
        _logger = logger;
    }

    public async Task InitializeAsync()
    {
        if (_initialized)
        {
            return;
        }

        await _initializeLock.WaitAsync();
        try
        {
            if (_initialized)
            {
                return;
            }

            var folder = Path.GetDirectoryName(_databasePath);
            if (!string.IsNullOrEmpty(folder))
            {
                Directory.CreateDirectory(folder);
            }

            using var connection = OpenConnection(loadVectorExtension: false);
            _sqliteVecAvailable = TryLoadVectorExtension(connection);
            if (_sqliteVecSettings.Required && !_sqliteVecAvailable)
            {
                throw new InvalidOperationException("sqlite-vec is required but the vec0 extension could not be loaded.");
            }

            ApplyMigrations(connection);
            EnsureEmbeddingDimensionCompatibility(connection);
            EnsureVectorSchema(connection);
            _initialized = true;
        }
        finally
        {
            _initializeLock.Release();
        }
    }

    public async Task<ContextEntry> SaveAsync(ContextEntry entry)
    {
        await InitializeAsync();

        lock (_writeLock)
        {
            using var connection = OpenConnection(loadVectorExtension: _sqliteVecAvailable);
            using var transaction = connection.BeginTransaction();

            var vectorRowId = GetOrCreateVectorRowId(connection, transaction, entry.Id);

            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO context_entries
                    (id, vector_rowid, category, text, embedding_json, source_tool, visibility_policy_json, created_at, updated_at)
                VALUES
                    ($id, $vectorRowId, $category, $text, $embedding, $sourceTool, $policy, $createdAt, $updatedAt)
                ON CONFLICT(id) DO UPDATE SET
                    category = excluded.category,
                    text = excluded.text,
                    embedding_json = excluded.embedding_json,
                    source_tool = excluded.source_tool,
                    visibility_policy_json = excluded.visibility_policy_json,
                    updated_at = excluded.updated_at;
                """;
            AddEntryParameters(command, entry, vectorRowId);
            command.ExecuteNonQuery();

            if (_sqliteVecAvailable)
            {
                using var vectorCommand = connection.CreateCommand();
                vectorCommand.Transaction = transaction;
                vectorCommand.CommandText = """
                    INSERT OR REPLACE INTO context_vectors(rowid, embedding)
                    VALUES ($rowid, $embedding);
                    """;
                vectorCommand.Parameters.AddWithValue("$rowid", vectorRowId);
                vectorCommand.Parameters.AddWithValue("$embedding", SerializeEmbedding(entry.Embedding));
                vectorCommand.ExecuteNonQuery();
            }

            transaction.Commit();
        }

        return entry;
    }

    public async Task<IReadOnlyList<ContextSearchResult>> SearchAsync(float[] queryEmbedding, int limit)
    {
        await InitializeAsync();

        var safeLimit = Math.Clamp(limit, 1, 100);
        using var connection = OpenConnection(loadVectorExtension: _sqliteVecAvailable);

        if (_sqliteVecAvailable)
        {
            return SearchWithSqliteVec(connection, queryEmbedding, safeLimit);
        }

        _logger.LogWarning("sqlite-vec is unavailable; falling back to in-memory vector search.");
        return SearchInMemory(connection, queryEmbedding, safeLimit);
    }

    private SqliteConnection OpenConnection(bool loadVectorExtension)
    {
        var connection = new SqliteConnection($"Data Source={_databasePath}");
        connection.Open();
        if (loadVectorExtension && !TryLoadVectorExtension(connection, logSuccess: false))
        {
            throw new InvalidOperationException("sqlite-vec was available during startup but could not be loaded for a database operation.");
        }

        return connection;
    }

    private bool TryLoadVectorExtension(SqliteConnection connection, bool logSuccess = true)
    {
        if (!_sqliteVecSettings.Enabled)
        {
            _logger.LogInformation("sqlite-vec is disabled by configuration.");
            return false;
        }

        foreach (var extensionPath in GetVectorExtensionCandidates())
        {
            try
            {
                connection.LoadExtension(extensionPath);
                using var command = connection.CreateCommand();
                command.CommandText = "SELECT vec_version();";
                var version = command.ExecuteScalar()?.ToString() ?? "unknown";
                if (logSuccess)
                {
                    _logger.LogInformation("Loaded sqlite-vec extension from {ExtensionPath}; version {Version}", extensionPath, version);
                }

                return true;
            }
            catch (Exception exception)
            {
                _logger.LogDebug(exception, "Unable to load sqlite-vec extension from {ExtensionPath}", extensionPath);
            }
        }

        _logger.LogWarning("sqlite-vec extension could not be loaded.");
        return false;
    }

    private static IEnumerable<string> GetVectorExtensionCandidates()
    {
        var fileName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? "vec0.dll"
            : RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
                ? "vec0.dylib"
                : "vec0.so";

        yield return "vec0";
        yield return Path.Combine(AppContext.BaseDirectory, fileName);
        yield return Path.Combine(AppContext.BaseDirectory, "runtimes", RuntimeInformation.RuntimeIdentifier, "native", fileName);
    }

    private void ApplyMigrations(SqliteConnection connection)
    {
        ExecuteNonQuery(connection, """
            CREATE TABLE IF NOT EXISTS schema_migrations (
                version INTEGER PRIMARY KEY,
                applied_at TEXT NOT NULL
            );
            """);

        var appliedVersion = GetAppliedVersion(connection);
        if (appliedVersion < 1)
        {
            ApplySchemaVersion1(connection);
            MarkMigrationApplied(connection, 1);
        }

        if (appliedVersion < 2)
        {
            ApplySchemaVersion2(connection);
            MarkMigrationApplied(connection, 2);
        }
    }

    private void EnsureEmbeddingDimensionCompatibility(SqliteConnection connection)
    {
        var storedDimension = GetStoredEmbeddingDimension(connection);
        if (storedDimension is not null)
        {
            EnsureDimensionMatches(storedDimension.Value);
            return;
        }

        var inferredDimension = InferExistingEmbeddingDimension(connection);
        if (inferredDimension is not null)
        {
            EnsureDimensionMatches(inferredDimension.Value);
        }

        SetStoredEmbeddingDimension(connection, _embeddingDimension);
    }

    private void EnsureVectorSchema(SqliteConnection connection)
    {
        if (!_sqliteVecAvailable)
        {
            return;
        }

        ExecuteNonQuery(connection, $"""
            CREATE VIRTUAL TABLE IF NOT EXISTS context_vectors
            USING vec0(embedding float[{_embeddingDimension}]);
            """);

        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT vector_rowid, embedding_json
            FROM context_entries
            WHERE vector_rowid NOT IN (SELECT rowid FROM context_vectors);
            """;

        using var reader = command.ExecuteReader();
        var missingVectors = new List<(long RowId, string EmbeddingJson)>();
        while (reader.Read())
        {
            missingVectors.Add((reader.GetInt64(0), reader.GetString(1)));
        }

        foreach (var vector in missingVectors)
        {
            using var insert = connection.CreateCommand();
            insert.CommandText = "INSERT OR REPLACE INTO context_vectors(rowid, embedding) VALUES ($rowid, $embedding);";
            insert.Parameters.AddWithValue("$rowid", vector.RowId);
            insert.Parameters.AddWithValue("$embedding", vector.EmbeddingJson);
            insert.ExecuteNonQuery();
        }
    }

    private void ApplySchemaVersion2(SqliteConnection connection)
    {
        ExecuteNonQuery(connection, """
            CREATE TABLE IF NOT EXISTS app_metadata (
                key TEXT PRIMARY KEY,
                value TEXT NOT NULL
            );
            """);
    }

    private void ApplySchemaVersion1(SqliteConnection connection)
    {
        using var transaction = connection.BeginTransaction();

        ExecuteNonQuery(connection, """
            CREATE TABLE IF NOT EXISTS context_entries (
                id TEXT PRIMARY KEY,
                vector_rowid INTEGER NOT NULL UNIQUE,
                category TEXT NOT NULL,
                text TEXT NOT NULL,
                embedding_json TEXT NOT NULL,
                source_tool TEXT NOT NULL,
                visibility_policy_json TEXT NOT NULL,
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL
            );
            """, transaction);

        ExecuteNonQuery(connection, """
            CREATE INDEX IF NOT EXISTS idx_context_entries_category
            ON context_entries(category);
            """, transaction);

        ExecuteNonQuery(connection, """
            CREATE INDEX IF NOT EXISTS idx_context_entries_source_tool
            ON context_entries(source_tool);
            """, transaction);

        if (_sqliteVecAvailable)
        {
            ExecuteNonQuery(connection, $"""
                CREATE VIRTUAL TABLE IF NOT EXISTS context_vectors
                USING vec0(embedding float[{_embeddingDimension}]);
                """, transaction);
        }

        transaction.Commit();
    }

    private static long GetAppliedVersion(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COALESCE(MAX(version), 0) FROM schema_migrations;";
        return (long)(command.ExecuteScalar() ?? 0L);
    }

    private static void MarkMigrationApplied(SqliteConnection connection, int version)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT OR IGNORE INTO schema_migrations(version, applied_at) VALUES ($version, $appliedAt);";
        command.Parameters.AddWithValue("$version", version);
        command.Parameters.AddWithValue("$appliedAt", DateTimeOffset.UtcNow.ToString("O"));
        command.ExecuteNonQuery();
    }

    private static void ExecuteNonQuery(SqliteConnection connection, string sql, SqliteTransaction? transaction = null)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private long GetOrCreateVectorRowId(SqliteConnection connection, SqliteTransaction transaction, string entryId)
    {
        using var existingCommand = connection.CreateCommand();
        existingCommand.Transaction = transaction;
        existingCommand.CommandText = "SELECT vector_rowid FROM context_entries WHERE id = $id;";
        existingCommand.Parameters.AddWithValue("$id", entryId);
        var existing = existingCommand.ExecuteScalar();
        if (existing is long rowId)
        {
            return rowId;
        }

        using var nextCommand = connection.CreateCommand();
        nextCommand.Transaction = transaction;
        nextCommand.CommandText = "SELECT COALESCE(MAX(vector_rowid), 0) + 1 FROM context_entries;";
        return (long)(nextCommand.ExecuteScalar() ?? 1L);
    }

    private void AddEntryParameters(SqliteCommand command, ContextEntry entry, long vectorRowId)
    {
        command.Parameters.AddWithValue("$id", entry.Id);
        command.Parameters.AddWithValue("$vectorRowId", vectorRowId);
        command.Parameters.AddWithValue("$category", entry.Category);
        command.Parameters.AddWithValue("$text", entry.Text);
        command.Parameters.AddWithValue("$embedding", SerializeEmbedding(entry.Embedding));
        command.Parameters.AddWithValue("$sourceTool", entry.SourceTool);
        command.Parameters.AddWithValue("$policy", JsonSerializer.Serialize(entry.VisibilityPolicy, _serializerOptions));
        command.Parameters.AddWithValue("$createdAt", entry.CreatedAt.ToString("O"));
        command.Parameters.AddWithValue("$updatedAt", entry.UpdatedAt.ToString("O"));
    }

    private int? GetStoredEmbeddingDimension(SqliteConnection connection)
    {
        if (!TableExists(connection, "app_metadata"))
        {
            return null;
        }

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT value FROM app_metadata WHERE key = $key;";
        command.Parameters.AddWithValue("$key", EmbeddingDimensionMetadataKey);
        var value = command.ExecuteScalar()?.ToString();

        return int.TryParse(value, out var dimension) ? dimension : null;
    }

    private void SetStoredEmbeddingDimension(SqliteConnection connection, int dimension)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO app_metadata(key, value)
            VALUES ($key, $value)
            ON CONFLICT(key) DO UPDATE SET value = excluded.value;
            """;
        command.Parameters.AddWithValue("$key", EmbeddingDimensionMetadataKey);
        command.Parameters.AddWithValue("$value", dimension.ToString());
        command.ExecuteNonQuery();
    }

    private int? InferExistingEmbeddingDimension(SqliteConnection connection)
    {
        var vectorSchemaDimension = GetVectorTableDimension(connection);
        if (vectorSchemaDimension is not null)
        {
            return vectorSchemaDimension;
        }

        if (!TableExists(connection, "context_entries"))
        {
            return null;
        }

        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT embedding_json
            FROM context_entries
            WHERE embedding_json IS NOT NULL AND embedding_json <> ''
            LIMIT 1;
            """;
        var embeddingJson = command.ExecuteScalar()?.ToString();
        if (string.IsNullOrWhiteSpace(embeddingJson))
        {
            return null;
        }

        var embedding = JsonSerializer.Deserialize<float[]>(embeddingJson, _serializerOptions);
        return embedding?.Length;
    }

    private static int? GetVectorTableDimension(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT sql FROM sqlite_master WHERE name = 'context_vectors';";
        var createSql = command.ExecuteScalar()?.ToString();
        if (string.IsNullOrWhiteSpace(createSql))
        {
            return null;
        }

        var match = Regex.Match(createSql, @"float\[(?<dimension>\d+)\]", RegexOptions.IgnoreCase);
        return match.Success && int.TryParse(match.Groups["dimension"].Value, out var dimension)
            ? dimension
            : null;
    }

    private static bool TableExists(SqliteConnection connection, string tableName)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE name = $name;";
        command.Parameters.AddWithValue("$name", tableName);
        return (long)(command.ExecuteScalar() ?? 0L) > 0;
    }

    private void EnsureDimensionMatches(int databaseDimension)
    {
        if (databaseDimension == _embeddingDimension)
        {
            return;
        }

        throw new InvalidOperationException(
            $"Embedding dimension mismatch. Database was initialized with dimension {databaseDimension}, but EMBEDDING__DIMENSION is {_embeddingDimension}. " +
            "Use the original dimension, or rebuild/re-embed the database after changing embedding providers or models. " +
            "For a disposable Docker development database, stop the server and delete docker/data/context.db, then start it again with the new embedding configuration.");
    }

    private IReadOnlyList<ContextSearchResult> SearchWithSqliteVec(SqliteConnection connection, float[] queryEmbedding, int limit)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                e.id,
                e.category,
                e.text,
                e.embedding_json,
                e.source_tool,
                e.visibility_policy_json,
                e.created_at,
                e.updated_at,
                v.distance
            FROM context_vectors v
            JOIN context_entries e ON e.vector_rowid = v.rowid
            WHERE v.embedding MATCH $embedding AND k = $limit
            ORDER BY v.distance;
            """;
        command.Parameters.AddWithValue("$embedding", SerializeEmbedding(queryEmbedding));
        command.Parameters.AddWithValue("$limit", limit);

        using var reader = command.ExecuteReader();
        var results = new List<ContextSearchResult>();
        while (reader.Read())
        {
            var entry = ReadEntry(reader);
            var distance = Convert.ToSingle(reader.GetDouble(8));
            var score = 1f / (1f + Math.Max(0f, distance));
            results.Add(new ContextSearchResult(entry, score));
        }

        return results;
    }

    private IReadOnlyList<ContextSearchResult> SearchInMemory(SqliteConnection connection, float[] queryEmbedding, int limit)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, category, text, embedding_json, source_tool, visibility_policy_json, created_at, updated_at
            FROM context_entries;
            """;

        using var reader = command.ExecuteReader();
        var results = new List<ContextSearchResult>();
        while (reader.Read())
        {
            var entry = ReadEntry(reader);
            results.Add(new ContextSearchResult(entry, ComputeSimilarity(queryEmbedding, entry.Embedding)));
        }

        return results
            .OrderByDescending(result => result.Score)
            .Take(limit)
            .ToList();
    }

    private ContextEntry ReadEntry(SqliteDataReader reader)
    {
        return new ContextEntry
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
    }

    private string SerializeEmbedding(float[] embedding)
    {
        if (embedding.Length != _embeddingDimension)
        {
            throw new InvalidOperationException($"Embedding dimension {embedding.Length} does not match configured dimension {_embeddingDimension}.");
        }

        return JsonSerializer.Serialize(embedding, _serializerOptions);
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
