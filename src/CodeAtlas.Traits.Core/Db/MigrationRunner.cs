using Microsoft.Data.Sqlite;

namespace CodeAtlas.Traits.Core.Db;

/// <summary>
/// Manages schema migrations for <c>.codeatlas/traits.db</c>.
/// </summary>
public static class MigrationRunner
{
    public static void Migrate(SqliteConnection connection)
    {
        // ── 1. Declarations ──
        object? currentVersionObj;
        int currentVersion;

        // ── 2. Intent-First Narrative ──
        EnsureMigrationsTable(connection);

        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText   = "SELECT MAX(version) FROM schema_migrations;";
            currentVersionObj = cmd.ExecuteScalar();
            currentVersion    = currentVersionObj is DBNull or null ? 0 : Convert.ToInt32(currentVersionObj);
        }

        if (currentVersion < 1)
        {
            ApplyMigration(connection, 1, M001_InitialSchema);
        }

        if (currentVersion < 2)
        {
            ApplyMigration(connection, 2, M002_TagSourceAndEmbeddings);
        }
    }

    private static void EnsureMigrationsTable(SqliteConnection connection)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS schema_migrations (
                version    INTEGER PRIMARY KEY,
                applied_at TEXT NOT NULL
            );
            """;
        cmd.ExecuteNonQuery();
    }

    private static void ApplyMigration(SqliteConnection connection, int version, string sql)
    {
        using var tx = connection.BeginTransaction();
        using var cmd = connection.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();

        cmd.CommandText = "INSERT INTO schema_migrations (version, applied_at) VALUES (@version, @now);";
        cmd.Parameters.AddWithValue("@version", version);
        cmd.Parameters.AddWithValue("@now", DateTime.UtcNow.ToString("O"));
        cmd.ExecuteNonQuery();

        tx.Commit();
    }

    private const string M001_InitialSchema = """
        CREATE TABLE IF NOT EXISTS symbol_snapshots (
            id                INTEGER PRIMARY KEY AUTOINCREMENT,
            symbol_name       TEXT    NOT NULL,
            symbol_type       TEXT    NOT NULL,
            class_name        TEXT,
            language          TEXT    NOT NULL,
            file_path         TEXT    NOT NULL,
            start_line        INTEGER NOT NULL,
            end_line          INTEGER NOT NULL,
            start_byte        INTEGER NOT NULL,
            end_byte          INTEGER NOT NULL,
            signature         TEXT,
            doc_comment       TEXT,
            body_hash         TEXT    NOT NULL,
            is_trivial        INTEGER NOT NULL DEFAULT 0,
            extracted_content TEXT
        );

        CREATE INDEX IF NOT EXISTS idx_snapshots_body_hash ON symbol_snapshots(body_hash);
        CREATE INDEX IF NOT EXISTS idx_snapshots_language  ON symbol_snapshots(language);
        CREATE INDEX IF NOT EXISTS idx_snapshots_file_path ON symbol_snapshots(file_path);

        CREATE TABLE IF NOT EXISTS trait_entries (
            body_hash     TEXT PRIMARY KEY,
            summary       TEXT    NOT NULL,
            is_utility    INTEGER NOT NULL DEFAULT 0,
            tagged_at_utc TEXT    NOT NULL
        );

        CREATE TABLE IF NOT EXISTS trait_tags (
            body_hash TEXT NOT NULL REFERENCES trait_entries(body_hash) ON DELETE CASCADE,
            tag       TEXT NOT NULL,
            PRIMARY KEY (body_hash, tag)
        );

        CREATE INDEX IF NOT EXISTS idx_trait_tags_tag ON trait_tags(tag);
        """;

    private const string M002_TagSourceAndEmbeddings = """
        ALTER TABLE trait_entries ADD COLUMN tag_source TEXT NOT NULL DEFAULT 'ai';
        ALTER TABLE trait_entries ADD COLUMN embedding_blob BLOB;
        """;
}
