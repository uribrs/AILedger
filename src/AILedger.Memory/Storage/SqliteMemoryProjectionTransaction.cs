using System.Buffers.Binary;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using AILedger.Memory.Contracts;
using Microsoft.Data.Sqlite;

namespace AILedger.Memory.Storage;

internal sealed class SqliteMemoryProjectionTransaction : IMemoryProjectionTransaction
{
    private readonly SqliteConnection _connection;
    private readonly SqliteTransaction _transaction;
    private bool _completed;

    public SqliteMemoryProjectionTransaction(SqliteConnection connection, SqliteTransaction transaction)
    {
        _connection = connection;
        _transaction = transaction;
    }

    public async Task ApplyAsync(SourceDelta delta, CancellationToken cancellationToken = default)
    {
        ThrowIfCompleted();
        ArgumentNullException.ThrowIfNull(delta);
        ValidateDelta(delta);

        await EnsureCheckpointMatchesAsync(delta.PreviousCheckpoint, delta.Source.Id, cancellationToken)
            .ConfigureAwait(false);

        await ExecuteAsync(
            """
            INSERT INTO sources(id, kind, canonical_path, repository, task_id)
            VALUES($id, $kind, $path, $repository, $taskId)
            ON CONFLICT(id) DO UPDATE SET
                kind = excluded.kind,
                canonical_path = excluded.canonical_path,
                repository = excluded.repository,
                task_id = excluded.task_id
            """,
            command =>
            {
                command.Parameters.AddWithValue("$id", delta.Source.Id);
                command.Parameters.AddWithValue("$kind", delta.Source.Kind.ToString());
                command.Parameters.AddWithValue("$path", delta.Source.CanonicalPath);
                command.Parameters.AddWithValue("$repository", DbValue(delta.Source.Repository));
                command.Parameters.AddWithValue("$taskId", DbValue(delta.Source.TaskId));
            },
            cancellationToken).ConfigureAwait(false);

        foreach (var documentId in delta.Deletes.Distinct(StringComparer.Ordinal))
        {
            await ExecuteAsync(
                "DELETE FROM documents WHERE id = $id AND source_id = $sourceId",
                command =>
                {
                    command.Parameters.AddWithValue("$id", documentId);
                    command.Parameters.AddWithValue("$sourceId", delta.Source.Id);
                },
                cancellationToken).ConfigureAwait(false);
        }

        foreach (var document in delta.Upserts)
        {
            await UpsertDocumentAsync(delta.Source.Id, document, cancellationToken).ConfigureAwait(false);
        }

        // K15: keep the statically declared SourceCheckpoint type for both serialization directions.
        SourceCheckpoint checkpoint = delta.NextCheckpoint;
        var checkpointJson = JsonSerializer.Serialize(
            checkpoint,
            typeof(SourceCheckpoint),
            MemoryStorageJson.Options);
        await ExecuteAsync(
            """
            UPDATE sources
            SET checkpoint_json = $checkpoint, checkpointed_at = $checkpointedAt
            WHERE id = $id
            """,
            command =>
            {
                command.Parameters.AddWithValue("$checkpoint", checkpointJson);
                command.Parameters.AddWithValue("$checkpointedAt", checkpoint.ObservedAt.ToString("O", CultureInfo.InvariantCulture));
                command.Parameters.AddWithValue("$id", delta.Source.Id);
            },
            cancellationToken).ConfigureAwait(false);
    }

    public async Task StoreEmbeddingsAsync(
        IReadOnlyList<DocumentEmbedding> embeddings,
        CancellationToken cancellationToken = default)
    {
        ThrowIfCompleted();
        ArgumentNullException.ThrowIfNull(embeddings);
        if (embeddings.Count == 0)
        {
            return;
        }

        var identity = embeddings[0].Identity;
        SqliteMemoryProjectionStore.ValidateIdentity(identity);
        await EnsureActiveIdentityCompatibleAsync(identity, requireActive: true, cancellationToken)
            .ConfigureAwait(false);

        foreach (var embedding in embeddings)
        {
            ValidateEmbedding(embedding, identity);
            await ExecuteAsync(
                """
                INSERT INTO document_embeddings(
                    document_id, content_hash, provider, model, dimensions, embedding_version,
                    chunk_index, chunk_count, vector, embedded_at)
                VALUES(
                    $documentId, $contentHash, $provider, $model, $dimensions, $version,
                    $chunkIndex, $chunkCount, $vector, $embeddedAt)
                ON CONFLICT(document_id, content_hash, provider, model, dimensions, embedding_version, chunk_index)
                DO UPDATE SET chunk_count = excluded.chunk_count,
                              vector = excluded.vector,
                              embedded_at = excluded.embedded_at
                """,
                command =>
                {
                    command.Parameters.AddWithValue("$documentId", embedding.DocumentId);
                    command.Parameters.AddWithValue("$contentHash", embedding.ContentHash);
                    AddIdentityParameters(command, identity);
                    command.Parameters.AddWithValue("$chunkIndex", embedding.ChunkIndex);
                    command.Parameters.AddWithValue("$chunkCount", embedding.ChunkCount);
                    command.Parameters.Add("$vector", SqliteType.Blob).Value = EncodeVector(embedding.Vector.Span);
                    command.Parameters.AddWithValue("$embeddedAt", embedding.EmbeddedAt.ToString("O", CultureInfo.InvariantCulture));
                },
                cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task SetActiveEmbeddingIdentityAsync(
        EmbeddingIdentity identity,
        CancellationToken cancellationToken = default)
    {
        ThrowIfCompleted();
        SqliteMemoryProjectionStore.ValidateIdentity(identity);
        await EnsureActiveIdentityCompatibleAsync(identity, requireActive: false, cancellationToken)
            .ConfigureAwait(false);
        await ExecuteAsync(
            """
            INSERT INTO embedding_identities(id, provider, model, dimensions, version)
            VALUES(1, $provider, $model, $dimensions, $version)
            ON CONFLICT(id) DO UPDATE SET
                provider = excluded.provider,
                model = excluded.model,
                dimensions = excluded.dimensions,
                version = excluded.version
            """,
            command => AddIdentityParameters(command, identity),
            cancellationToken).ConfigureAwait(false);
    }

    public Task RecordIndexRunAsync(
        IndexRunStatistics statistics,
        CancellationToken cancellationToken = default)
    {
        ThrowIfCompleted();
        ArgumentNullException.ThrowIfNull(statistics);
        ArgumentException.ThrowIfNullOrWhiteSpace(statistics.RunId);
        var json = JsonSerializer.Serialize(statistics, MemoryStorageJson.Options);
        return ExecuteAsync(
            """
            INSERT INTO index_runs(
                id, kind, outcome, started_at, completed_at, statistics_json, failure_code, failure_message)
            VALUES($id, $kind, $outcome, $startedAt, $completedAt, $statistics, $failureCode, $failureMessage)
            ON CONFLICT(id) DO UPDATE SET
                kind = excluded.kind,
                outcome = excluded.outcome,
                started_at = excluded.started_at,
                completed_at = excluded.completed_at,
                statistics_json = excluded.statistics_json,
                failure_code = excluded.failure_code,
                failure_message = excluded.failure_message
            """,
            command =>
            {
                command.Parameters.AddWithValue("$id", statistics.RunId);
                command.Parameters.AddWithValue("$kind", statistics.Kind.ToString());
                command.Parameters.AddWithValue("$outcome", statistics.Outcome.ToString());
                command.Parameters.AddWithValue("$startedAt", statistics.StartedAt.ToString("O", CultureInfo.InvariantCulture));
                command.Parameters.AddWithValue("$completedAt", DbValue(statistics.CompletedAt?.ToString("O", CultureInfo.InvariantCulture)));
                command.Parameters.AddWithValue("$statistics", json);
                command.Parameters.AddWithValue("$failureCode", DbValue(statistics.FailureCode));
                command.Parameters.AddWithValue("$failureMessage", DbValue(statistics.FailureMessage));
            },
            cancellationToken);
    }

    public async Task CommitAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfCompleted();
        await _transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        _completed = true;
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (!_completed)
            {
                await _transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
                _completed = true;
            }
        }
        finally
        {
            await _transaction.DisposeAsync().ConfigureAwait(false);
            await _connection.DisposeAsync().ConfigureAwait(false);
        }
    }

    private async Task UpsertDocumentAsync(
        string sourceId,
        MemoryDocument document,
        CancellationToken cancellationToken)
    {
        ValidateDocument(document);
        await ExecuteAsync(
            """
            INSERT INTO documents(
                id, kind, text, repository, task_id, work_item_id, run_id, actor_id,
                source_timestamp, source_version, authority, lifecycle, tags_json,
                related_ids_json, citations_json, content_hash, normalizer_version, source_id)
            VALUES(
                $id, $kind, $text, $repository, $taskId, $workItemId, $runId, $actorId,
                $sourceTimestamp, $sourceVersion, $authority, $lifecycle, $tags,
                $relatedIds, $citations, $contentHash, $normalizerVersion, $sourceId)
            ON CONFLICT(id) DO UPDATE SET
                kind = excluded.kind,
                text = excluded.text,
                repository = excluded.repository,
                task_id = excluded.task_id,
                work_item_id = excluded.work_item_id,
                run_id = excluded.run_id,
                actor_id = excluded.actor_id,
                source_timestamp = excluded.source_timestamp,
                source_version = excluded.source_version,
                authority = excluded.authority,
                lifecycle = excluded.lifecycle,
                tags_json = excluded.tags_json,
                related_ids_json = excluded.related_ids_json,
                citations_json = excluded.citations_json,
                content_hash = excluded.content_hash,
                normalizer_version = excluded.normalizer_version,
                source_id = excluded.source_id
            """,
            command =>
            {
                command.Parameters.AddWithValue("$id", document.Id);
                command.Parameters.AddWithValue("$kind", document.Kind.ToString());
                command.Parameters.AddWithValue("$text", document.Text);
                command.Parameters.AddWithValue("$repository", DbValue(document.Repository));
                command.Parameters.AddWithValue("$taskId", DbValue(document.TaskId));
                command.Parameters.AddWithValue("$workItemId", DbValue(document.WorkItemId));
                command.Parameters.AddWithValue("$runId", DbValue(document.RunId));
                command.Parameters.AddWithValue("$actorId", DbValue(document.ActorId));
                command.Parameters.AddWithValue("$sourceTimestamp", document.SourceTimestamp.ToString("O", CultureInfo.InvariantCulture));
                command.Parameters.AddWithValue("$sourceVersion", document.SourceVersion);
                command.Parameters.AddWithValue("$authority", (int)document.Authority);
                command.Parameters.AddWithValue("$lifecycle", document.Lifecycle.ToString());
                command.Parameters.AddWithValue("$tags", JsonSerializer.Serialize(document.Tags, MemoryStorageJson.Options));
                command.Parameters.AddWithValue("$relatedIds", JsonSerializer.Serialize(document.RelatedIds, MemoryStorageJson.Options));
                command.Parameters.AddWithValue("$citations", JsonSerializer.Serialize(document.Citations, MemoryStorageJson.Options));
                command.Parameters.AddWithValue("$contentHash", document.ContentHash);
                command.Parameters.AddWithValue("$normalizerVersion", document.NormalizerVersion);
                command.Parameters.AddWithValue("$sourceId", sourceId);
            },
            cancellationToken).ConfigureAwait(false);

        await ExecuteAsync(
            "DELETE FROM document_relations WHERE document_id = $documentId",
            command => command.Parameters.AddWithValue("$documentId", document.Id),
            cancellationToken).ConfigureAwait(false);
        await ExecuteAsync(
            "DELETE FROM document_embeddings WHERE document_id = $documentId AND content_hash <> $contentHash",
            command =>
            {
                command.Parameters.AddWithValue("$documentId", document.Id);
                command.Parameters.AddWithValue("$contentHash", document.ContentHash);
            },
            cancellationToken).ConfigureAwait(false);
        foreach (var relation in document.Relations.Distinct())
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(relation.TargetId);
            await ExecuteAsync(
                """
                INSERT INTO document_relations(document_id, kind, target_id)
                VALUES($documentId, $kind, $targetId)
                """,
                command =>
                {
                    command.Parameters.AddWithValue("$documentId", document.Id);
                    command.Parameters.AddWithValue("$kind", relation.Kind.ToString());
                    command.Parameters.AddWithValue("$targetId", relation.TargetId);
                },
                cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task EnsureActiveIdentityCompatibleAsync(
        EmbeddingIdentity identity,
        bool requireActive,
        CancellationToken cancellationToken)
    {
        await using var command = CreateCommand();
        command.CommandText = """
            SELECT provider, model, dimensions, version
            FROM embedding_identities
            WHERE id = 1
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            if (requireActive)
            {
                throw new InvalidOperationException(
                    "Set the active embedding identity before storing document embeddings.");
            }

            return;
        }

        var active = new EmbeddingIdentity
        {
            Provider = reader.GetString(0),
            Model = reader.GetString(1),
            Dimensions = reader.GetInt32(2),
            Version = reader.GetString(3)
        };
        if (!SqliteMemoryProjectionStore.IdentityEquals(active, identity))
        {
            throw new InvalidOperationException(
                "The requested embedding identity does not match the active projection identity; rebuild before changing identity.");
        }
    }

    private async Task EnsureCheckpointMatchesAsync(
        SourceCheckpoint? expected,
        string sourceId,
        CancellationToken cancellationToken)
    {
        await using var command = CreateCommand();
        command.CommandText = "SELECT checkpoint_json FROM sources WHERE id = $sourceId";
        command.Parameters.AddWithValue("$sourceId", sourceId);
        var storedJson = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) as string;
        if (expected is null)
        {
            if (storedJson is not null)
            {
                throw new InvalidOperationException(
                    $"Source '{sourceId}' already has a checkpoint; refresh before applying the delta.");
            }

            return;
        }

        var storedCheckpoint = storedJson is null
            ? null
            : MemoryStorageJson.DeserializeCheckpoint(storedJson, sourceId);
        if (storedCheckpoint is null || !storedCheckpoint.Equals(expected))
        {
            throw new InvalidOperationException(
                $"Checkpoint for source '{sourceId}' changed; refresh before applying the delta.");
        }
    }

    private Task ExecuteAsync(
        string sql,
        Action<SqliteCommand> bind,
        CancellationToken cancellationToken)
    {
        var command = CreateCommand();
        command.CommandText = sql;
        bind(command);
        return ExecuteAndDisposeAsync(command, cancellationToken);
    }

    private static async Task ExecuteAndDisposeAsync(
        SqliteCommand command,
        CancellationToken cancellationToken)
    {
        await using (command)
        {
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private SqliteCommand CreateCommand()
    {
        var command = _connection.CreateCommand();
        command.Transaction = _transaction;
        return command;
    }

    private static void ValidateDelta(SourceDelta delta)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(delta.Source.Id);
        ArgumentException.ThrowIfNullOrWhiteSpace(delta.Source.CanonicalPath);
        if (!string.Equals(delta.Source.Id, delta.NextCheckpoint.SourceId, StringComparison.Ordinal) ||
            delta.Source.Kind != delta.NextCheckpoint.Kind)
        {
            throw new ArgumentException("The source descriptor and next checkpoint identify different sources.", nameof(delta));
        }

        if (delta.PreviousCheckpoint is not null &&
            (!string.Equals(delta.Source.Id, delta.PreviousCheckpoint.SourceId, StringComparison.Ordinal) ||
             delta.Source.Kind != delta.PreviousCheckpoint.Kind))
        {
            throw new ArgumentException("The previous checkpoint does not belong to the source delta.", nameof(delta));
        }

        var duplicateIds = delta.Upserts.GroupBy(static document => document.Id, StringComparer.Ordinal)
            .FirstOrDefault(static group => group.Count() > 1);
        if (duplicateIds is not null)
        {
            throw new ArgumentException($"Source delta contains duplicate document '{duplicateIds.Key}'.", nameof(delta));
        }
    }

    private static void ValidateDocument(MemoryDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrWhiteSpace(document.Id);
        ArgumentException.ThrowIfNullOrWhiteSpace(document.Text);
        ArgumentException.ThrowIfNullOrWhiteSpace(document.SourceVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(document.ContentHash);
        ArgumentException.ThrowIfNullOrWhiteSpace(document.NormalizerVersion);
        ArgumentNullException.ThrowIfNull(document.Tags);
        ArgumentNullException.ThrowIfNull(document.RelatedIds);
        ArgumentNullException.ThrowIfNull(document.Citations);
        ArgumentNullException.ThrowIfNull(document.Relations);
    }

    private static void ValidateEmbedding(DocumentEmbedding embedding, EmbeddingIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(embedding);
        ArgumentException.ThrowIfNullOrWhiteSpace(embedding.DocumentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(embedding.ContentHash);
        if (!SqliteMemoryProjectionStore.IdentityEquals(embedding.Identity, identity))
        {
            throw new ArgumentException("An embedding batch cannot mix provider identities.", nameof(embedding));
        }

        if (embedding.Vector.Length != identity.Dimensions)
        {
            throw new ArgumentException("Embedding vector dimensions do not match its identity.", nameof(embedding));
        }

        if (embedding.ChunkCount <= 0 || embedding.ChunkIndex < 0 || embedding.ChunkIndex >= embedding.ChunkCount)
        {
            throw new ArgumentException("Embedding chunk coordinates are invalid.", nameof(embedding));
        }

        foreach (var value in embedding.Vector.Span)
        {
            if (!float.IsFinite(value))
            {
                throw new ArgumentException("Embedding vectors must contain only finite values.", nameof(embedding));
            }
        }
    }

    private static byte[] EncodeVector(ReadOnlySpan<float> vector)
    {
        var bytes = new byte[checked(vector.Length * sizeof(float))];
        for (var index = 0; index < vector.Length; index++)
        {
            BinaryPrimitives.WriteInt32LittleEndian(
                bytes.AsSpan(index * sizeof(float), sizeof(float)),
                BitConverter.SingleToInt32Bits(vector[index]));
        }

        return bytes;
    }

    private static void AddIdentityParameters(SqliteCommand command, EmbeddingIdentity identity)
    {
        command.Parameters.AddWithValue("$provider", identity.Provider);
        command.Parameters.AddWithValue("$model", identity.Model);
        command.Parameters.AddWithValue("$dimensions", identity.Dimensions);
        command.Parameters.AddWithValue("$version", identity.Version);
    }

    private static object DbValue(string? value) => value is null ? DBNull.Value : value;

    private void ThrowIfCompleted()
    {
        if (_completed)
        {
            throw new InvalidOperationException("The projection transaction has already completed.");
        }
    }
}
