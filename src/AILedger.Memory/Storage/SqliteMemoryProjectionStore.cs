using System.Buffers.Binary;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using AILedger.Memory.Contracts;
using Microsoft.Data.Sqlite;

namespace AILedger.Memory.Storage;

public sealed class SqliteMemoryProjectionStore : IMemoryProjectionStore
{
    private const int ReusableEmbeddingHashBatchSize = 500;

    private const string DocumentColumns = """
        d.id, d.kind, d.text, d.repository, d.task_id, d.work_item_id, d.run_id, d.actor_id,
        d.source_timestamp, d.source_version, d.authority, d.lifecycle, d.tags_json,
        d.related_ids_json, d.citations_json, d.content_hash, d.normalizer_version
        """;

    private readonly string _databasePath;

    public SqliteMemoryProjectionStore(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        _databasePath = Path.GetFullPath(databasePath);
        ConnectionString = new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Private,
            Pooling = true,
            ForeignKeys = true
        }.ToString();
    }

    internal string ConnectionString { get; }

    internal void ClearPool()
    {
        using var connection = new SqliteConnection(ConnectionString);
        SqliteConnection.ClearPool(connection);
    }

    public int SupportedSchemaVersion => MemorySchemaV1.Version;

    internal async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        SqliteRuntimeProbe.RequireFts5(connection);

        await using (var pragma = connection.CreateCommand())
        {
            pragma.CommandText = "PRAGMA journal_mode=DELETE; PRAGMA synchronous=FULL;";
            await pragma.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await using var transaction = await connection.BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);
        foreach (var statement in MemorySchemaV1.CreateStatements)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = (SqliteTransaction)transaction;
            command.CommandText = statement;
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    internal async Task ValidateSchemaAsync(CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        SqliteRuntimeProbe.RequireFts5(connection);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT value FROM memory_metadata WHERE key = 'schema_version'";

        string? rawVersion;
        try
        {
            rawVersion = Convert.ToString(
                await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false),
                CultureInfo.InvariantCulture);
        }
        catch (SqliteException exception) when (exception.SqliteErrorCode == 1)
        {
            throw new InvalidDataException(
                "The memory projection has no recognized schema; rebuild it with this binary.",
                exception);
        }

        if (!int.TryParse(rawVersion, NumberStyles.None, CultureInfo.InvariantCulture, out var version))
        {
            throw new InvalidDataException(
                "The memory projection has no valid schema version; rebuild it with this binary.");
        }

        if (version > SupportedSchemaVersion)
        {
            throw new NotSupportedException(
                $"Memory schema {version} is newer than supported schema {SupportedSchemaVersion}; use a compatible binary.");
        }

        if (version < SupportedSchemaVersion)
        {
            throw new NotSupportedException(
                $"Memory schema {version} is older than supported schema {SupportedSchemaVersion}; rebuild the disposable projection.");
        }
    }

    public async Task<IMemoryProjectionTransaction> BeginUpdateAsync(
        CancellationToken cancellationToken = default)
    {
        await ValidateSchemaAsync(cancellationToken).ConfigureAwait(false);
        var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken)
                .ConfigureAwait(false);
            return new SqliteMemoryProjectionTransaction(connection, transaction);
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    public async Task<SourceCheckpoint?> GetCheckpointAsync(
        string sourceId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceId);
        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT checkpoint_json FROM sources WHERE id = $id";
        command.Parameters.AddWithValue("$id", sourceId);
        var value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        if (value is null or DBNull)
        {
            return null;
        }

        return MemoryStorageJson.DeserializeCheckpoint((string)value, sourceId);
    }

    public async Task<MemoryDocument?> InspectAsync(
        string documentId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentId);
        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {DocumentColumns} FROM documents d WHERE d.id = $id";
        command.Parameters.AddWithValue("$id", documentId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var document = ReadDocument(reader, []);
        await reader.DisposeAsync().ConfigureAwait(false);
        return document with { Relations = await ReadRelationsAsync(connection, document.Id, cancellationToken).ConfigureAwait(false) };
    }

    public async Task<MemoryDatabaseStatistics> GetStatisticsAsync(
        CancellationToken cancellationToken = default)
    {
        await ValidateSchemaAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var documentCount = await ScalarInt64Async(connection, "SELECT count(*) FROM documents", cancellationToken)
            .ConfigureAwait(false);
        var embeddingCount = await ScalarInt64Async(connection, "SELECT count(*) FROM document_embeddings", cancellationToken)
            .ConfigureAwait(false);
        var sourceCount = await ScalarInt64Async(connection, "SELECT count(*) FROM sources", cancellationToken)
            .ConfigureAwait(false);
        var identity = await ReadActiveIdentityAsync(connection, cancellationToken).ConfigureAwait(false);

        IndexRunStatistics? latestRun = null;
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT statistics_json FROM index_runs ORDER BY started_at DESC LIMIT 1";
            var json = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) as string;
            if (json is not null)
            {
                latestRun = JsonSerializer.Deserialize<IndexRunStatistics>(json, MemoryStorageJson.Options)
                    ?? throw new InvalidDataException("The latest index run statistics are empty.");
            }
        }

        return new MemoryDatabaseStatistics
        {
            SchemaVersion = SupportedSchemaVersion,
            DatabaseBytes = File.Exists(_databasePath) ? new FileInfo(_databasePath).Length : 0,
            DocumentCount = documentCount,
            EmbeddingChunkCount = embeddingCount,
            SourceCount = sourceCount,
            ActiveEmbeddingIdentity = identity,
            LatestRun = latestRun
        };
    }

    public async Task<EmbeddingIdentity?> GetActiveEmbeddingIdentityAsync(
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        return await ReadActiveIdentityAsync(connection, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<DocumentEmbedding>> FindReusableEmbeddingsAsync(
        IReadOnlySet<string> contentHashes,
        EmbeddingIdentity identity,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(contentHashes);
        ValidateIdentity(identity);
        if (contentHashes.Count == 0)
        {
            return [];
        }

        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await EnsureActiveIdentityMatchesAsync(connection, identity, cancellationToken).ConfigureAwait(false);
        var results = new List<DocumentEmbedding>();
        var hashes = contentHashes.Order(StringComparer.Ordinal).ToArray();
        for (var offset = 0; offset < hashes.Length; offset += ReusableEmbeddingHashBatchSize)
        {
            await using var command = connection.CreateCommand();
            var batch = hashes
                .Skip(offset)
                .Take(ReusableEmbeddingHashBatchSize)
                .ToHashSet(StringComparer.Ordinal);
            var hashParameters = AddStringSet(command, "$hash", batch);
            command.CommandText = $"""
                SELECT document_id, content_hash, chunk_index, chunk_count, vector, embedded_at
                FROM document_embeddings
                WHERE provider = $provider AND model = $model AND dimensions = $dimensions
                  AND embedding_version = $version AND content_hash IN ({hashParameters})
                """;
            AddIdentityParameters(command, identity);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                results.Add(ReadEmbedding(reader, identity));
            }
        }

        return results
            .OrderBy(static embedding => embedding.DocumentId, StringComparer.Ordinal)
            .ThenBy(static embedding => embedding.ChunkIndex)
            .ToArray();
    }

    public async Task<IReadOnlyList<LexicalCandidate>> FindLexicalCandidatesAsync(
        string query,
        MemoryCandidateFilter filter,
        int limit,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        ArgumentNullException.ThrowIfNull(filter);
        if (limit <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(limit), "Candidate limit must be positive.");
        }

        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        var predicates = new List<string> { "documents_fts MATCH $query", "d.lifecycle <> 'Deleted'" };
        command.Parameters.AddWithValue("$query", BuildFtsQuery(query));
        AddFilter(command, filter, predicates);
        command.Parameters.AddWithValue("$limit", limit);
        command.CommandText = $"""
            SELECT {DocumentColumns}, -bm25(documents_fts) AS lexical_score
            FROM documents_fts
            JOIN documents d ON d.rowid = documents_fts.rowid
            WHERE {string.Join(" AND ", predicates)}
            ORDER BY bm25(documents_fts), d.source_timestamp DESC, d.id
            LIMIT $limit
            """;

        var rows = new List<(MemoryDocument Document, double Score)>();
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                rows.Add((ReadDocument(reader, []), reader.GetDouble(17)));
            }
        }

        var results = new List<LexicalCandidate>(rows.Count);
        foreach (var row in rows)
        {
            var relations = await ReadRelationsAsync(connection, row.Document.Id, cancellationToken).ConfigureAwait(false);
            results.Add(new LexicalCandidate
            {
                Document = row.Document with { Relations = relations },
                Score = row.Score
            });
        }

        return results;
    }

    public async Task<IReadOnlyList<EmbeddingCandidate>> ReadEmbeddingCandidatesAsync(
        EmbeddingIdentity identity,
        MemoryCandidateFilter filter,
        CancellationToken cancellationToken = default)
    {
        ValidateIdentity(identity);
        ArgumentNullException.ThrowIfNull(filter);
        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await EnsureActiveIdentityMatchesAsync(connection, identity, cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        var predicates = new List<string>
        {
            "e.provider = $provider", "e.model = $model", "e.dimensions = $dimensions",
            "e.embedding_version = $version", "e.content_hash = d.content_hash", "d.lifecycle <> 'Deleted'"
        };
        AddIdentityParameters(command, identity);
        AddFilter(command, filter, predicates);
        command.CommandText = $"""
            SELECT {DocumentColumns}, e.document_id, e.content_hash, e.chunk_index, e.chunk_count, e.vector, e.embedded_at
            FROM document_embeddings e
            JOIN documents d ON d.id = e.document_id
            WHERE {string.Join(" AND ", predicates)}
            ORDER BY d.id, e.chunk_index
            """;

        var rows = new List<(MemoryDocument Document, DocumentEmbedding Embedding)>();
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var document = ReadDocument(reader, []);
                rows.Add((document, ReadEmbedding(reader, identity, 17)));
            }
        }

        var relationCache = new Dictionary<string, IReadOnlyList<DocumentRelation>>(StringComparer.Ordinal);
        var results = new List<EmbeddingCandidate>(rows.Count);
        foreach (var row in rows)
        {
            if (!relationCache.TryGetValue(row.Document.Id, out var relations))
            {
                relations = await ReadRelationsAsync(connection, row.Document.Id, cancellationToken).ConfigureAwait(false);
                relationCache.Add(row.Document.Id, relations);
            }

            results.Add(new EmbeddingCandidate
            {
                Document = row.Document with { Relations = relations },
                Embedding = row.Embedding
            });
        }

        return results;
    }

    public async Task VerifyIntegrityAsync(CancellationToken cancellationToken = default)
    {
        await ValidateSchemaAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "PRAGMA integrity_check";
            var result = Convert.ToString(
                await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false),
                CultureInfo.InvariantCulture);
            if (!string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"SQLite integrity check failed: {result}");
            }
        }

        await using (var command = connection.CreateCommand())
        {
            command.CommandText =
                "INSERT INTO documents_fts(documents_fts, rank) VALUES('integrity-check', 1)";
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<SqliteConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(ConnectionString);
        try
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA foreign_keys=ON; PRAGMA busy_timeout=5000;";
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private static MemoryDocument ReadDocument(SqliteDataReader reader, IReadOnlyList<DocumentRelation> relations) =>
        new()
        {
            Id = reader.GetString(0),
            Kind = ParseEnum<MemoryDocumentKind>(reader.GetString(1), "document kind"),
            Text = reader.GetString(2),
            Repository = GetNullableString(reader, 3),
            TaskId = GetNullableString(reader, 4),
            WorkItemId = GetNullableString(reader, 5),
            RunId = GetNullableString(reader, 6),
            ActorId = GetNullableString(reader, 7),
            SourceTimestamp = ParseTimestamp(reader.GetString(8), "source timestamp"),
            SourceVersion = reader.GetString(9),
            Authority = (MemoryAuthority)reader.GetInt32(10),
            Lifecycle = ParseEnum<MemoryLifecycle>(reader.GetString(11), "document lifecycle"),
            Tags = DeserializeList<string>(reader.GetString(12), "document tags"),
            RelatedIds = DeserializeList<string>(reader.GetString(13), "related identifiers"),
            Citations = DeserializeList<MemoryCitation>(reader.GetString(14), "document citations"),
            ContentHash = reader.GetString(15),
            NormalizerVersion = reader.GetString(16),
            Relations = relations
        };

    private static async Task<IReadOnlyList<DocumentRelation>> ReadRelationsAsync(
        SqliteConnection connection,
        string documentId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT kind, target_id
            FROM document_relations
            WHERE document_id = $documentId
            ORDER BY kind, target_id
            """;
        command.Parameters.AddWithValue("$documentId", documentId);
        var relations = new List<DocumentRelation>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            relations.Add(new DocumentRelation
            {
                Kind = ParseEnum<DocumentRelationKind>(reader.GetString(0), "relation kind"),
                TargetId = reader.GetString(1)
            });
        }

        return relations;
    }

    private static async Task<EmbeddingIdentity?> ReadActiveIdentityAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT provider, model, dimensions, version
            FROM embedding_identities
            WHERE id = 1
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var identity = new EmbeddingIdentity
        {
            Provider = reader.GetString(0),
            Model = reader.GetString(1),
            Dimensions = reader.GetInt32(2),
            Version = reader.GetString(3)
        };
        ValidateIdentity(identity);
        return identity;
    }

    private static async Task EnsureActiveIdentityMatchesAsync(
        SqliteConnection connection,
        EmbeddingIdentity requested,
        CancellationToken cancellationToken)
    {
        var active = await ReadActiveIdentityAsync(connection, cancellationToken).ConfigureAwait(false);
        if (active is not null && !IdentityEquals(active, requested))
        {
            throw new InvalidOperationException(
                $"Embedding identity '{requested.Provider}/{requested.Model}/{requested.Dimensions}/{requested.Version}' " +
                $"does not match the active projection identity '{active.Provider}/{active.Model}/{active.Dimensions}/{active.Version}'.");
        }
    }

    private static void AddFilter(
        SqliteCommand command,
        MemoryCandidateFilter filter,
        ICollection<string> predicates)
    {
        if (!filter.IncludeSuperseded)
        {
            predicates.Add("d.lifecycle <> 'Superseded'");
        }

        if (filter.Repository is not null)
        {
            predicates.Add("d.repository = $repository");
            command.Parameters.AddWithValue("$repository", filter.Repository);
        }

        if (filter.TaskId is not null)
        {
            predicates.Add("d.task_id = $taskId");
            command.Parameters.AddWithValue("$taskId", filter.TaskId);
        }

        if (filter.Kinds is { Count: > 0 })
        {
            var values = filter.Kinds.Select(static value => value.ToString()).ToHashSet(StringComparer.Ordinal);
            predicates.Add($"d.kind IN ({AddStringSet(command, "$kind", values)})");
        }
    }

    private static string AddStringSet(SqliteCommand command, string prefix, IReadOnlySet<string> values)
    {
        var names = new List<string>(values.Count);
        var index = 0;
        foreach (var value in values.Order(StringComparer.Ordinal))
        {
            var name = prefix + index++.ToString(CultureInfo.InvariantCulture);
            names.Add(name);
            command.Parameters.AddWithValue(name, value);
        }

        return string.Join(", ", names);
    }

    private static string BuildFtsQuery(string query)
    {
        var terms = new List<string>();
        var start = -1;
        for (var index = 0; index <= query.Length; index++)
        {
            var isTokenCharacter = index < query.Length && char.IsLetterOrDigit(query[index]);
            if (isTokenCharacter && start < 0)
            {
                start = index;
            }
            else if (!isTokenCharacter && start >= 0)
            {
                terms.Add(query[start..index]);
                start = -1;
            }
        }

        if (terms.Count == 0)
        {
            throw new ArgumentException(
                "A lexical query must contain at least one letter or digit.",
                nameof(query));
        }

        return string.Join(" OR ", terms.Select(static term => $"\"{term}\""));
    }

    private static void AddIdentityParameters(SqliteCommand command, EmbeddingIdentity identity)
    {
        command.Parameters.AddWithValue("$provider", identity.Provider);
        command.Parameters.AddWithValue("$model", identity.Model);
        command.Parameters.AddWithValue("$dimensions", identity.Dimensions);
        command.Parameters.AddWithValue("$version", identity.Version);
    }

    private static DocumentEmbedding ReadEmbedding(
        SqliteDataReader reader,
        EmbeddingIdentity identity,
        int offset = 0) =>
        new()
        {
            DocumentId = reader.GetString(offset),
            ContentHash = reader.GetString(offset + 1),
            Identity = identity,
            ChunkIndex = reader.GetInt32(offset + 2),
            ChunkCount = reader.GetInt32(offset + 3),
            Vector = DecodeVector((byte[])reader.GetValue(offset + 4), identity.Dimensions),
            EmbeddedAt = ParseTimestamp(reader.GetString(offset + 5), "embedding timestamp")
        };

    private static ReadOnlyMemory<float> DecodeVector(byte[] bytes, int dimensions)
    {
        if (bytes.Length != checked(dimensions * sizeof(float)))
        {
            throw new InvalidDataException(
                $"Stored vector has {bytes.Length} bytes but identity requires {dimensions * sizeof(float)}.");
        }

        var vector = new float[dimensions];
        for (var index = 0; index < dimensions; index++)
        {
            var bits = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(index * sizeof(float), sizeof(float)));
            vector[index] = BitConverter.Int32BitsToSingle(bits);
        }

        return vector;
    }

    private static async Task<long> ScalarInt64Async(
        SqliteConnection connection,
        string sql,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(
            await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false),
            CultureInfo.InvariantCulture);
    }

    private static string? GetNullableString(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);

    private static DateTimeOffset ParseTimestamp(string value, string description) =>
        DateTimeOffset.TryParseExact(
            value,
            "O",
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind,
            out var timestamp)
            ? timestamp
            : throw new InvalidDataException($"Invalid {description} '{value}'.");

    private static T ParseEnum<T>(string value, string description)
        where T : struct, Enum =>
        Enum.TryParse<T>(value, ignoreCase: false, out var parsed) && Enum.IsDefined(parsed)
            ? parsed
            : throw new InvalidDataException($"Invalid {description} '{value}'.");

    private static IReadOnlyList<T> DeserializeList<T>(string json, string description) =>
        JsonSerializer.Deserialize<IReadOnlyList<T>>(json, MemoryStorageJson.Options)
        ?? throw new InvalidDataException($"Stored {description} are empty.");

    internal static bool IdentityEquals(EmbeddingIdentity left, EmbeddingIdentity right) =>
        left.Dimensions == right.Dimensions &&
        string.Equals(left.Provider, right.Provider, StringComparison.Ordinal) &&
        string.Equals(left.Model, right.Model, StringComparison.Ordinal) &&
        string.Equals(left.Version, right.Version, StringComparison.Ordinal);

    internal static void ValidateIdentity(EmbeddingIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentException.ThrowIfNullOrWhiteSpace(identity.Provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(identity.Model);
        ArgumentException.ThrowIfNullOrWhiteSpace(identity.Version);
        identity.Validate();
    }
}
