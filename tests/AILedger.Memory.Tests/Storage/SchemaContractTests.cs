using System.Text.Json;
using AILedger.Memory.Contracts;
using AILedger.Memory.Storage;
using AILedger.Memory.Tests.Support;
using Microsoft.Data.Sqlite;

namespace AILedger.Memory.Tests.Storage;

public sealed class SchemaContractTests
{
    [Fact]
    public void SchemaV1RunsWithFts5AndPassesBothIntegrityChecks()
    {
        using var fixture = new SchemaFixture();
        fixture.Execute("""
            INSERT INTO sources(id, kind, canonical_path) VALUES ('source', 'EventHistory', 'events.jsonl');
            INSERT INTO documents(
                id, kind, text, source_timestamp, source_version, authority, lifecycle,
                tags_json, related_ids_json, citations_json, content_hash, normalizer_version, source_id)
            VALUES (
                'document', 'Decision', 'alpha searchable text', '2026-09-08T00:00:00Z', '1', 60, 'Active',
                '[""memory""]', '[]', '[]', 'hash', '1', 'source');
            """);

        Assert.Equal(1L, fixture.Scalar<long>("SELECT count(*) FROM documents_fts WHERE documents_fts MATCH 'alpha'"));

        fixture.Execute("UPDATE documents SET text = 'beta replacement text' WHERE id = 'document'");
        Assert.Equal(0L, fixture.Scalar<long>("SELECT count(*) FROM documents_fts WHERE documents_fts MATCH 'alpha'"));
        Assert.Equal(1L, fixture.Scalar<long>("SELECT count(*) FROM documents_fts WHERE documents_fts MATCH 'beta'"));

        Assert.Equal("ok", fixture.Scalar<string>("PRAGMA integrity_check"));
        fixture.Execute("INSERT INTO documents_fts(documents_fts, rank) VALUES ('integrity-check', 1)");

        fixture.Execute("DELETE FROM documents WHERE id = 'document'");
        Assert.Equal(0L, fixture.Scalar<long>("SELECT count(*) FROM documents_fts WHERE documents_fts MATCH 'beta'"));
    }

    [Fact]
    public void Q15_RefusalCheckpointSurvivesTheSchemaStorageColumn()
    {
        using var fixture = new SchemaFixture();
        SourceCheckpoint checkpoint = new RefusalJournalCheckpoint
        {
            SourceId = "refusals", Kind = CanonicalSourceKind.RefusalJournal,
            SourceFingerprint = "fingerprint", ObservedAt = DateTimeOffset.UnixEpoch,
            CommittedByteOffset = 123, CommittedLineCount = 7, PrefixHash = "prefix-hash"
        };
        var json = JsonSerializer.Serialize(checkpoint);

        using (var command = fixture.Connection.CreateCommand())
        {
            command.CommandText = "INSERT INTO sources(id, kind, canonical_path, checkpoint_json) VALUES ($id, $kind, $path, $checkpoint)";
            command.Parameters.AddWithValue("$id", "refusals");
            command.Parameters.AddWithValue("$kind", "RefusalJournal");
            command.Parameters.AddWithValue("$path", "refusals.jsonl");
            command.Parameters.AddWithValue("$checkpoint", json);
            command.ExecuteNonQuery();
        }

        var stored = fixture.Scalar<string>("SELECT checkpoint_json FROM sources WHERE id = 'refusals'");
        var restored = Assert.IsType<RefusalJournalCheckpoint>(JsonSerializer.Deserialize<SourceCheckpoint>(stored));
        Assert.Equal(123, restored.CommittedByteOffset);
        Assert.Equal(7, restored.CommittedLineCount);
        Assert.Equal("prefix-hash", restored.PrefixHash);
    }

    [Fact]
    public void SchemaEnforcesSingleActiveIdentityAndChunkBounds()
    {
        using var fixture = new SchemaFixture();
        fixture.Execute("INSERT INTO embedding_identities(id, provider, model, dimensions, version) VALUES (1, 'p', 'm', 3, 'v1')");

        Assert.Throws<SqliteException>(() =>
            fixture.Execute("INSERT INTO embedding_identities(id, provider, model, dimensions, version) VALUES (2, 'p', 'm2', 3, 'v1')"));
        Assert.Throws<SqliteException>(() =>
            fixture.Execute("UPDATE embedding_identities SET dimensions = 0 WHERE id = 1"));
    }

    private sealed class SchemaFixture : IDisposable
    {
        private readonly TemporaryDirectory _directory = new();

        public SchemaFixture()
        {
            Connection = new SqliteConnection($"Data Source={Path.Combine(_directory.Path, "memory.sqlite")}");
            Connection.Open();
            Execute("PRAGMA foreign_keys = ON");
            SqliteRuntimeProbe.RequireFts5(Connection);
            foreach (var statement in MemorySchemaV1.CreateStatements)
            {
                Execute(statement);
            }
        }

        public SqliteConnection Connection { get; }

        public void Execute(string sql)
        {
            using var command = Connection.CreateCommand();
            command.CommandText = sql;
            command.ExecuteNonQuery();
        }

        public T Scalar<T>(string sql)
        {
            using var command = Connection.CreateCommand();
            command.CommandText = sql;
            return (T)Convert.ChangeType(command.ExecuteScalar()!, typeof(T), System.Globalization.CultureInfo.InvariantCulture);
        }

        public void Dispose()
        {
            Connection.Dispose();
            _directory.Dispose();
        }
    }
}
