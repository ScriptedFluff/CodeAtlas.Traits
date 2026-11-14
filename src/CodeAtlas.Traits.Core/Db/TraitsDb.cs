using CodeAtlas.Traits.Core.Db.Queries;
using Microsoft.Data.Sqlite;

namespace CodeAtlas.Traits.Core.Db;

/// <summary>
/// WAL-mode SQLite database wrapper for <c>.codeatlas/traits.db</c>.
/// </summary>
public sealed class TraitsDb : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly bool _readOnly;

    public SqliteConnection Connection => _connection;
    public string DbFilePath { get; }
    public TraitQueries Traits { get; }

    public TraitsDb(string dbPath, bool readOnly = false)
    {
        // ── 1. Declarations ──
        string? directory;
        string connectionString;
        string? aiJsonlPath;

        // ── 2. Preconditions & Guard Clauses ──
        DbFilePath = dbPath;
        _readOnly  = readOnly;
        if (!readOnly)
        {
            directory = Path.GetDirectoryName(dbPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }
        }

        // ── 3. Intent-First Narrative ──
        connectionString = readOnly
            ? $"Data Source={dbPath};Mode=ReadOnly;Cache=Shared"
            : $"Data Source={dbPath};Cache=Shared";

        _connection = new SqliteConnection(connectionString);
        _connection.Open();

        using (var pragma = _connection.CreateCommand())
        {
            pragma.CommandText = readOnly
                ? "PRAGMA foreign_keys=ON;"
                : "PRAGMA journal_mode=WAL; PRAGMA foreign_keys=ON;";
            pragma.ExecuteNonQuery();
        }

        aiJsonlPath = AiTraitsJsonlStore.ResolveJsonlPathFromDbPath(dbPath);
        Traits      = new TraitQueries(_connection, aiJsonlPath);
    }

    public void Migrate()
    {
        MigrationRunner.Migrate(_connection);
        if (!_readOnly)
        {
            Traits.ImportAiTraitsFromJsonlIfPresent();
        }
    }

    public void Dispose()
    {
        _connection.Dispose();
    }
}
