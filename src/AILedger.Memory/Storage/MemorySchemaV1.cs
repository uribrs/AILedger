namespace AILedger.Memory.Storage;

public static class MemorySchemaV1
{
    public const int Version = 1;

    public static IReadOnlyList<string> CreateStatements { get; } =
    [
        """
        CREATE TABLE memory_metadata (
            key TEXT PRIMARY KEY,
            value TEXT NOT NULL
        ) STRICT
        """,
        """
        CREATE TABLE sources (
            id TEXT PRIMARY KEY,
            kind TEXT NOT NULL,
            canonical_path TEXT NOT NULL,
            repository TEXT,
            task_id TEXT,
            checkpoint_json TEXT,
            checkpointed_at TEXT
        ) STRICT
        """,
        """
        CREATE TABLE documents (
            id TEXT PRIMARY KEY,
            kind TEXT NOT NULL,
            text TEXT NOT NULL,
            repository TEXT,
            task_id TEXT,
            work_item_id TEXT,
            run_id TEXT,
            actor_id TEXT,
            source_timestamp TEXT NOT NULL,
            source_version TEXT NOT NULL,
            authority INTEGER NOT NULL,
            lifecycle TEXT NOT NULL,
            tags_json TEXT NOT NULL,
            related_ids_json TEXT NOT NULL,
            citations_json TEXT NOT NULL,
            content_hash TEXT NOT NULL,
            normalizer_version TEXT NOT NULL,
            source_id TEXT NOT NULL REFERENCES sources(id)
        ) STRICT
        """,
        """
        CREATE TABLE document_relations (
            document_id TEXT NOT NULL REFERENCES documents(id) ON DELETE CASCADE,
            kind TEXT NOT NULL,
            target_id TEXT NOT NULL,
            PRIMARY KEY (document_id, kind, target_id)
        ) STRICT
        """,
        "CREATE INDEX documents_source_idx ON documents(source_id)",
        "CREATE INDEX documents_filter_idx ON documents(lifecycle, kind, repository, task_id, source_timestamp)",
        """
        CREATE TABLE embedding_identities (
            id INTEGER PRIMARY KEY CHECK (id = 1),
            provider TEXT NOT NULL,
            model TEXT NOT NULL,
            dimensions INTEGER NOT NULL CHECK (dimensions BETWEEN 1 AND 4096),
            version TEXT NOT NULL
        ) STRICT
        """,
        """
        CREATE TABLE document_embeddings (
            document_id TEXT NOT NULL REFERENCES documents(id) ON DELETE CASCADE,
            content_hash TEXT NOT NULL,
            provider TEXT NOT NULL,
            model TEXT NOT NULL,
            dimensions INTEGER NOT NULL CHECK (dimensions BETWEEN 1 AND 4096),
            embedding_version TEXT NOT NULL,
            chunk_index INTEGER NOT NULL CHECK (chunk_index >= 0),
            chunk_count INTEGER NOT NULL CHECK (chunk_count > 0 AND chunk_index < chunk_count),
            vector BLOB NOT NULL,
            embedded_at TEXT NOT NULL,
            PRIMARY KEY (document_id, content_hash, provider, model, dimensions, embedding_version, chunk_index)
        ) STRICT
        """,
        "CREATE INDEX document_embeddings_identity_idx ON document_embeddings(provider, model, dimensions, embedding_version)",
        """
        CREATE TABLE index_runs (
            id TEXT PRIMARY KEY,
            kind TEXT NOT NULL,
            outcome TEXT NOT NULL,
            started_at TEXT NOT NULL,
            completed_at TEXT,
            statistics_json TEXT NOT NULL,
            failure_code TEXT,
            failure_message TEXT
        ) STRICT
        """,
        "CREATE INDEX index_runs_started_idx ON index_runs(started_at DESC)",
        """
        CREATE VIRTUAL TABLE documents_fts USING fts5(
            text,
            tags_json,
            content='documents',
            content_rowid='rowid',
            tokenize='unicode61'
        )
        """,
        """
        CREATE TRIGGER documents_ai AFTER INSERT ON documents BEGIN
            INSERT INTO documents_fts(rowid, text, tags_json)
            VALUES (new.rowid, new.text, new.tags_json);
        END
        """,
        """
        CREATE TRIGGER documents_ad AFTER DELETE ON documents BEGIN
            INSERT INTO documents_fts(documents_fts, rowid, text, tags_json)
            VALUES ('delete', old.rowid, old.text, old.tags_json);
        END
        """,
        """
        CREATE TRIGGER documents_au AFTER UPDATE ON documents BEGIN
            INSERT INTO documents_fts(documents_fts, rowid, text, tags_json)
            VALUES ('delete', old.rowid, old.text, old.tags_json);
            INSERT INTO documents_fts(rowid, text, tags_json)
            VALUES (new.rowid, new.text, new.tags_json);
        END
        """,
        $"INSERT INTO memory_metadata(key, value) VALUES ('schema_version', '{Version}')"
    ];
}
