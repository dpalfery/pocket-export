namespace PocketExport.Core.Database;

using Microsoft.Data.Sqlite;

/// <summary>
/// Manages SQLite connection lifecycle and schema initialization for the local metadata index.
/// </summary>
public sealed class SqliteIndexContext : IDisposable
{
    private readonly string _connectionString;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="SqliteIndexContext"/> class.
    /// </summary>
    /// <param name="databasePath">The absolute path to the local SQLite database file.</param>
    public SqliteIndexContext(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        DatabasePath = databasePath;

        var directory = Path.GetDirectoryName(databasePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            DefaultTimeout = 5
        };

        _connectionString = builder.ToString();
    }

    /// <summary>
    /// Gets the path to the SQLite database file.
    /// </summary>
    public string DatabasePath { get; }

    /// <summary>
    /// Creates a new <see cref="SqliteConnection"/> to the local database.
    /// </summary>
    /// <returns>An unopened <see cref="SqliteConnection"/> instance.</returns>
    public SqliteConnection CreateConnection()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return new SqliteConnection(_connectionString);
    }

    /// <summary>
    /// Initializes the database schema, creating tables and indexes if they do not exist.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the asynchronous operation.</param>
    public async Task InitializeDatabaseAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        using (var pragmaCmd = connection.CreateCommand())
        {
            pragmaCmd.CommandText = "PRAGMA journal_mode = WAL; PRAGMA foreign_keys = ON;";
            await pragmaCmd.ExecuteNonQueryAsync(cancellationToken);
        }

        const string schemaSql = """
            CREATE TABLE IF NOT EXISTS recordings (
                id TEXT PRIMARY KEY NOT NULL,
                pocket_id TEXT NOT NULL,
                created_at TEXT NOT NULL,
                title TEXT NOT NULL,
                duration_seconds REAL NOT NULL,
                staging_audio_path TEXT NOT NULL,
                pocket_metadata_path TEXT,
                routed_transcript_path TEXT,
                routed_summary_path TEXT,
                status TEXT NOT NULL,
                error_message TEXT,
                domain TEXT,
                customer TEXT,
                project TEXT,
                initiative TEXT,
                confidence REAL,
                template_id TEXT,
                updated_at TEXT
            );

            CREATE TABLE IF NOT EXISTS speakers (
                id TEXT PRIMARY KEY NOT NULL,
                display_name TEXT NOT NULL,
                organization TEXT NOT NULL,
                enrolled_at TEXT NOT NULL,
                source_recording_id TEXT NOT NULL,
                sample_start_ms INTEGER NOT NULL,
                sample_end_ms INTEGER NOT NULL,
                embedding TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS recording_speakers (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                recording_id TEXT NOT NULL,
                cluster_id TEXT NOT NULL,
                enrolled_speaker_id TEXT,
                display_name TEXT,
                match_confidence REAL NOT NULL DEFAULT 0.0,
                needs_disambiguation INTEGER NOT NULL DEFAULT 0,
                sample_start_ms INTEGER,
                sample_end_ms INTEGER,
                embedding_vector TEXT,
                FOREIGN KEY (recording_id) REFERENCES recordings(id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS proposed_retroactive_matches (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                recording_id TEXT NOT NULL,
                cluster_id TEXT NOT NULL,
                proposed_speaker_id TEXT NOT NULL,
                confidence REAL NOT NULL,
                status TEXT NOT NULL DEFAULT 'proposed',
                created_at TEXT NOT NULL,
                reviewed_at TEXT,
                FOREIGN KEY (recording_id) REFERENCES recordings(id) ON DELETE CASCADE
            );

            CREATE INDEX IF NOT EXISTS idx_recordings_status ON recordings(status);
            CREATE INDEX IF NOT EXISTS idx_recordings_domain ON recordings(domain);
            CREATE INDEX IF NOT EXISTS idx_recording_speakers_recording_id ON recording_speakers(recording_id);
            CREATE INDEX IF NOT EXISTS idx_proposed_matches_recording_id ON proposed_retroactive_matches(recording_id);
            """;

        using var schemaCmd = connection.CreateCommand();
        schemaCmd.CommandText = schemaSql;
        await schemaCmd.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _disposed = true;
    }
}
