using Microsoft.Data.Sqlite;

namespace LocalMusicHub.Data;

public sealed class LibraryDatabase : IDisposable
{
    private const int SchemaVersion = 2;
    private readonly SqliteConnection _connection;

    public LibraryDatabase(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        _connection = new SqliteConnection($"Data Source={path}");
        _connection.Open();
        using (var pragma = _connection.CreateCommand())
        {
            pragma.CommandText = """
                PRAGMA foreign_keys = ON;
                PRAGMA journal_mode = WAL;
                PRAGMA synchronous = NORMAL;
                """;
            pragma.ExecuteNonQuery();
        }

        EnsureSchema();
    }

    public SqliteConnection Connection => _connection;

    private void EnsureSchema()
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS tracks (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                file_path TEXT NOT NULL UNIQUE,
                title TEXT NOT NULL DEFAULT '',
                artist TEXT NOT NULL DEFAULT '',
                album TEXT NOT NULL DEFAULT '',
                album_artist TEXT NOT NULL DEFAULT '',
                track_number INTEGER,
                year INTEGER,
                genre TEXT NOT NULL DEFAULT '',
                duration_ms INTEGER NOT NULL DEFAULT 0,
                bitrate INTEGER NOT NULL DEFAULT 0,
                format TEXT NOT NULL DEFAULT '',
                date_added_utc TEXT NOT NULL,
                file_modified_utc TEXT NOT NULL,
                cover_art BLOB,
                play_count INTEGER NOT NULL DEFAULT 0,
                last_played_utc TEXT,
                rating INTEGER NOT NULL DEFAULT 0
            );
            CREATE INDEX IF NOT EXISTS idx_tracks_album ON tracks(album_artist, album);
            CREATE INDEX IF NOT EXISTS idx_tracks_artist ON tracks(artist);
            CREATE INDEX IF NOT EXISTS idx_tracks_title ON tracks(title);

            CREATE TABLE IF NOT EXISTS playlists (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                name TEXT NOT NULL,
                created_utc TEXT NOT NULL,
                modified_utc TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS playlist_tracks (
                playlist_id INTEGER NOT NULL,
                track_id INTEGER NOT NULL,
                position INTEGER NOT NULL,
                PRIMARY KEY (playlist_id, track_id),
                FOREIGN KEY (playlist_id) REFERENCES playlists(id) ON DELETE CASCADE,
                FOREIGN KEY (track_id) REFERENCES tracks(id) ON DELETE CASCADE
            );
            CREATE INDEX IF NOT EXISTS idx_playlist_tracks_pos ON playlist_tracks(playlist_id, position);

            CREATE TABLE IF NOT EXISTS playlist_folders (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                name TEXT NOT NULL,
                parent_id INTEGER,
                FOREIGN KEY (parent_id) REFERENCES playlist_folders(id) ON DELETE CASCADE
            );
            CREATE INDEX IF NOT EXISTS idx_playlist_folders_parent ON playlist_folders(parent_id);

            CREATE TABLE IF NOT EXISTS app_meta (
                key TEXT PRIMARY KEY,
                value TEXT NOT NULL
            );
            """;
        cmd.ExecuteNonQuery();

        if (GetSchemaVersion() >= SchemaVersion)
            return;

        EnsureColumn("tracks", "play_count", "INTEGER NOT NULL DEFAULT 0");
        EnsureColumn("tracks", "last_played_utc", "TEXT");
        EnsureColumn("tracks", "rating", "INTEGER NOT NULL DEFAULT 0");
        EnsureColumn("playlists", "is_smart", "INTEGER NOT NULL DEFAULT 0");
        EnsureColumn("playlists", "rules_json", "TEXT");
        EnsureColumn("tracks", "replaygain_track_db", "REAL");
        EnsureColumn("tracks", "replaygain_album_db", "REAL");
        EnsureColumn("tracks", "replaygain_track_peak", "REAL");
        EnsureColumn("tracks", "replaygain_album_peak", "REAL");
        EnsureColumn("playlists", "folder_id", "INTEGER");
        EnsureColumn("tracks", "cue_start_ms", "INTEGER");
        EnsureColumn("tracks", "cue_end_ms", "INTEGER");
        EnsureColumn("tracks", "review_status", "TEXT NOT NULL DEFAULT 'none'");
        EnsureColumn("tracks", "comment", "TEXT NOT NULL DEFAULT ''");
        EnsureColumn("tracks", "date_released", "TEXT NOT NULL DEFAULT ''");
        SetSchemaVersion(SchemaVersion);
    }

    private int GetSchemaVersion()
    {
        try
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "SELECT value FROM app_meta WHERE key = 'schema_version'";
            var result = cmd.ExecuteScalar();
            return result is string s && int.TryParse(s, out var version) ? version : 0;
        }
        catch
        {
            return 0;
        }
    }

    private void SetSchemaVersion(int version)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO app_meta (key, value) VALUES ('schema_version', $value)
            ON CONFLICT(key) DO UPDATE SET value = excluded.value
            """;
        cmd.Parameters.AddWithValue("$value", version.ToString());
        cmd.ExecuteNonQuery();
    }

    private void EnsureColumn(string table, string column, string definition)
    {
        using var check = _connection.CreateCommand();
        check.CommandText = $"PRAGMA table_info({table})";
        using var reader = check.ExecuteReader();
        while (reader.Read())
        {
            if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase))
                return;
        }

        using var alter = _connection.CreateCommand();
        alter.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {definition}";
        alter.ExecuteNonQuery();
    }

    public void Dispose() => _connection.Dispose();
}
