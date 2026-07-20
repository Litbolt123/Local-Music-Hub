using LocalMusicHub.Models;
using LocalMusicHub.Services;
using Microsoft.Data.Sqlite;

namespace LocalMusicHub.Data;

public sealed class LibraryRepository
{
    private readonly LibraryDatabase _db;
    private readonly object _gate = new();

    public LibraryRepository(LibraryDatabase db) => _db = db;

    public void UpsertTrack(LibraryTrack track)
    {
        lock (_gate)
        {
            var existing = GetTrackByPath(track.FilePath);
            if (existing is not null)
                track.ReviewStatus = existing.ReviewStatus;
            else if (App.Settings.MarkNewImportsAsInbox)
                track.ReviewStatus = "inbox";

            using var cmd = _db.Connection.CreateCommand();
            cmd.CommandText = """
                INSERT INTO tracks (
                    file_path, title, artist, album, album_artist, track_number, year, genre,
                    duration_ms, bitrate, format, date_added_utc, file_modified_utc, cover_art,
                    play_count, last_played_utc, rating, cue_start_ms, cue_end_ms, review_status,
                    comment, date_released)
                VALUES (
                    $file_path, $title, $artist, $album, $album_artist, $track_number, $year, $genre,
                    $duration_ms, $bitrate, $format, $date_added_utc, $file_modified_utc, $cover_art,
                    $play_count, $last_played_utc, $rating, $cue_start_ms, $cue_end_ms, $review_status,
                    $comment, $date_released)
                ON CONFLICT(file_path) DO UPDATE SET
                    title = excluded.title,
                    artist = excluded.artist,
                    album = excluded.album,
                    album_artist = excluded.album_artist,
                    track_number = excluded.track_number,
                    year = excluded.year,
                    genre = excluded.genre,
                    duration_ms = excluded.duration_ms,
                    bitrate = excluded.bitrate,
                    format = excluded.format,
                    file_modified_utc = excluded.file_modified_utc,
                    cover_art = excluded.cover_art,
                    cue_start_ms = excluded.cue_start_ms,
                    cue_end_ms = excluded.cue_end_ms,
                    comment = excluded.comment,
                    date_released = excluded.date_released;
                """;
            AddTrackParams(cmd, track);
            cmd.ExecuteNonQuery();
        }
    }

    public LibraryTrack? GetTrackById(long id)
    {
        lock (_gate)
        {
            using var cmd = _db.Connection.CreateCommand();
            cmd.CommandText = "SELECT * FROM tracks WHERE id = $id";
            cmd.Parameters.AddWithValue("$id", id);
            return ReadTracks(cmd).FirstOrDefault();
        }
    }

    public LibraryTrack? GetTrackByPath(string path)
    {
        lock (_gate)
        {
            using var cmd = _db.Connection.CreateCommand();
            cmd.CommandText = "SELECT * FROM tracks WHERE file_path = $path COLLATE NOCASE";
            cmd.Parameters.AddWithValue("$path", path);
            return ReadTracks(cmd).FirstOrDefault();
        }
    }

    public void UpdateCoverArt(long trackId, byte[]? coverArt)
    {
        lock (_gate)
        {
            using var cmd = _db.Connection.CreateCommand();
            cmd.CommandText = """
                UPDATE tracks SET
                    cover_art = $cover_art,
                    file_modified_utc = $file_modified_utc
                WHERE id = $id;
                """;
            cmd.Parameters.AddWithValue("$id", trackId);
            cmd.Parameters.AddWithValue("$cover_art",
                coverArt is { Length: > 0 } ? coverArt : DBNull.Value);
            cmd.Parameters.AddWithValue("$file_modified_utc", DateTime.UtcNow.ToString("O"));
            cmd.ExecuteNonQuery();
        }
    }

    public void UpdateTrackMetadata(LibraryTrack track)
    {
        lock (_gate)
        {
            using var cmd = _db.Connection.CreateCommand();
            cmd.CommandText = """
                UPDATE tracks SET
                    title = $title,
                    artist = $artist,
                    album = $album,
                    album_artist = $album_artist,
                    track_number = $track_number,
                    year = $year,
                    genre = $genre,
                    cover_art = $cover_art,
                    file_modified_utc = $file_modified_utc,
                    comment = $comment,
                    date_released = $date_released
                WHERE id = $id;
                """;
            cmd.Parameters.AddWithValue("$id", track.Id);
            cmd.Parameters.AddWithValue("$title", track.Title);
            cmd.Parameters.AddWithValue("$artist", track.Artist);
            cmd.Parameters.AddWithValue("$album", track.Album);
            cmd.Parameters.AddWithValue("$album_artist", track.AlbumArtist);
            cmd.Parameters.AddWithValue("$track_number", (object?)track.TrackNumber ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$year", (object?)track.Year ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$genre", track.Genre);
            cmd.Parameters.AddWithValue("$cover_art", (object?)track.CoverArt ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$file_modified_utc", track.FileModifiedUtc.ToString("O"));
            cmd.Parameters.AddWithValue("$comment", track.Comment);
            cmd.Parameters.AddWithValue("$date_released", track.DateReleased);
            cmd.ExecuteNonQuery();
        }
    }

    public bool UpdateFilePath(long trackId, string newPath)
    {
        lock (_gate)
        {
            var normalized = Path.GetFullPath(newPath);
            using var cmd = _db.Connection.CreateCommand();
            cmd.CommandText = """
                UPDATE tracks SET
                    file_path = $new_path,
                    file_modified_utc = $modified
                WHERE id = $id;
                """;
            cmd.Parameters.AddWithValue("$id", trackId);
            cmd.Parameters.AddWithValue("$new_path", normalized);
            cmd.Parameters.AddWithValue("$modified", DateTime.UtcNow.ToString("O"));
            return cmd.ExecuteNonQuery() > 0;
        }
    }

    public bool MigrateFilePath(string oldPath, string newPath)
    {
        lock (_gate)
        {
            if (!File.Exists(newPath))
                return false;

            using var find = _db.Connection.CreateCommand();
            find.CommandText = "SELECT id FROM tracks WHERE file_path = $old COLLATE NOCASE";
            find.Parameters.AddWithValue("$old", oldPath);
            var idObj = find.ExecuteScalar();
            if (idObj is null or DBNull)
                return false;

            var id = Convert.ToInt64(idObj);
            var normalized = Path.GetFullPath(newPath);

            using var exists = _db.Connection.CreateCommand();
            exists.CommandText = "SELECT id FROM tracks WHERE file_path = $new COLLATE NOCASE AND id != $id";
            exists.Parameters.AddWithValue("$new", normalized);
            exists.Parameters.AddWithValue("$id", id);
            if (exists.ExecuteScalar() is not null)
                return false;

            using var cmd = _db.Connection.CreateCommand();
            cmd.CommandText = """
                UPDATE tracks SET
                    file_path = $new_path,
                    file_modified_utc = $modified
                WHERE id = $id;
                """;
            cmd.Parameters.AddWithValue("$id", id);
            cmd.Parameters.AddWithValue("$new_path", normalized);
            cmd.Parameters.AddWithValue("$modified", DateTime.UtcNow.ToString("O"));
            return cmd.ExecuteNonQuery() > 0;
        }
    }

    public void RecordPlay(long trackId)
    {
        lock (_gate)
        {
            using var cmd = _db.Connection.CreateCommand();
            cmd.CommandText = """
                UPDATE tracks SET
                    play_count = play_count + 1,
                    last_played_utc = $now
                WHERE id = $id;
                """;
            cmd.Parameters.AddWithValue("$id", trackId);
            cmd.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("O"));
            cmd.ExecuteNonQuery();
        }
    }

    public void SetRating(long trackId, int rating)
    {
        lock (_gate)
        {
            using var cmd = _db.Connection.CreateCommand();
            cmd.CommandText = "UPDATE tracks SET rating = $rating WHERE id = $id";
            cmd.Parameters.AddWithValue("$id", trackId);
            cmd.Parameters.AddWithValue("$rating", Math.Clamp(rating, 0, 5));
            cmd.ExecuteNonQuery();
        }
    }

    public void UpdateReplayGain(long trackId, double trackGainDb, float trackPeak, double? albumGainDb = null, float? albumPeak = null)
    {
        lock (_gate)
        {
            using var cmd = _db.Connection.CreateCommand();
            cmd.CommandText = """
                UPDATE tracks SET
                    replaygain_track_db = $track_db,
                    replaygain_track_peak = $track_peak,
                    replaygain_album_db = COALESCE($album_db, replaygain_album_db),
                    replaygain_album_peak = COALESCE($album_peak, replaygain_album_peak)
                WHERE id = $id;
                """;
            cmd.Parameters.AddWithValue("$id", trackId);
            cmd.Parameters.AddWithValue("$track_db", trackGainDb);
            cmd.Parameters.AddWithValue("$track_peak", trackPeak);
            cmd.Parameters.AddWithValue("$album_db", (object?)albumGainDb ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$album_peak", (object?)albumPeak ?? DBNull.Value);
            cmd.ExecuteNonQuery();
        }
    }

    public void RemovePath(string path)
    {
        lock (_gate)
        {
            using var idCmd = _db.Connection.CreateCommand();
            idCmd.CommandText = "SELECT id FROM tracks WHERE file_path = $path COLLATE NOCASE";
            idCmd.Parameters.AddWithValue("$path", path);
            var idObj = idCmd.ExecuteScalar();
            if (idObj is null or DBNull)
                return;

            var id = Convert.ToInt64(idObj);
            using var delPt = _db.Connection.CreateCommand();
            delPt.CommandText = "DELETE FROM playlist_tracks WHERE track_id = $id";
            delPt.Parameters.AddWithValue("$id", id);
            delPt.ExecuteNonQuery();

            using var cmd = _db.Connection.CreateCommand();
            cmd.CommandText = "DELETE FROM tracks WHERE id = $id";
            cmd.Parameters.AddWithValue("$id", id);
            cmd.ExecuteNonQuery();
        }
    }

    public int RemoveDeadEntries()
    {
        lock (_gate)
        {
            using var select = _db.Connection.CreateCommand();
            select.CommandText = "SELECT id, file_path FROM tracks";
            var toDelete = new List<long>();
            using (var reader = select.ExecuteReader())
            {
                while (reader.Read())
                {
                    var path = reader.GetString(1);
                    if (!File.Exists(path))
                        toDelete.Add(reader.GetInt64(0));
                }
            }

            foreach (var id in toDelete)
            {
                using var delPt = _db.Connection.CreateCommand();
                delPt.CommandText = "DELETE FROM playlist_tracks WHERE track_id = $id";
                delPt.Parameters.AddWithValue("$id", id);
                delPt.ExecuteNonQuery();

                using var del = _db.Connection.CreateCommand();
                del.CommandText = "DELETE FROM tracks WHERE id = $id";
                del.Parameters.AddWithValue("$id", id);
                del.ExecuteNonQuery();
            }

            return toDelete.Count;
        }
    }

    public void RemoveMissingPaths(IEnumerable<string> existingPaths)
    {
        lock (_gate)
        {
            var keep = existingPaths.ToHashSet(StringComparer.OrdinalIgnoreCase);
            using var select = _db.Connection.CreateCommand();
            select.CommandText = "SELECT file_path FROM tracks";
            var toDelete = new List<string>();
            using (var reader = select.ExecuteReader())
            {
                while (reader.Read())
                {
                    var path = reader.GetString(0);
                    if (keep.Contains(path))
                        continue;
                    var audioPath = CuePathHelper.ResolveAudioPath(path);
                    if (keep.Contains(audioPath))
                        continue;
                    toDelete.Add(path);
                }
            }

            foreach (var path in toDelete)
            {
                using var idCmd = _db.Connection.CreateCommand();
                idCmd.CommandText = "SELECT id FROM tracks WHERE file_path = $path";
                idCmd.Parameters.AddWithValue("$path", path);
                var idObj = idCmd.ExecuteScalar();
                if (idObj is long or int)
                {
                    var id = Convert.ToInt64(idObj);
                    using var delPt = _db.Connection.CreateCommand();
                    delPt.CommandText = "DELETE FROM playlist_tracks WHERE track_id = $id";
                    delPt.Parameters.AddWithValue("$id", id);
                    delPt.ExecuteNonQuery();
                }

                using var del = _db.Connection.CreateCommand();
                del.CommandText = "DELETE FROM tracks WHERE file_path = $path";
                del.Parameters.AddWithValue("$path", path);
                del.ExecuteNonQuery();
            }
        }
    }

    public void RemoveMissingPathsUnderRoots(IEnumerable<string> roots, IEnumerable<string> existingPaths)
    {
        var rootPaths = roots
            .Where(r => !string.IsNullOrWhiteSpace(r) && Directory.Exists(r))
            .Select(NormalizeDirectoryPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (rootPaths.Count == 0)
            return;

        lock (_gate)
        {
            var keep = existingPaths.ToHashSet(StringComparer.OrdinalIgnoreCase);
            using var select = _db.Connection.CreateCommand();
            select.CommandText = "SELECT file_path FROM tracks";
            var toDelete = new List<string>();
            using (var reader = select.ExecuteReader())
            {
                while (reader.Read())
                {
                    var path = reader.GetString(0);
                    if (!IsUnderAnyRoot(path, rootPaths))
                        continue;
                    if (keep.Contains(path))
                        continue;
                    var audioPath = CuePathHelper.ResolveAudioPath(path);
                    if (keep.Contains(audioPath))
                        continue;
                    toDelete.Add(path);
                }
            }

            foreach (var path in toDelete)
            {
                using var idCmd = _db.Connection.CreateCommand();
                idCmd.CommandText = "SELECT id FROM tracks WHERE file_path = $path";
                idCmd.Parameters.AddWithValue("$path", path);
                var idObj = idCmd.ExecuteScalar();
                if (idObj is long or int)
                {
                    var id = Convert.ToInt64(idObj);
                    using var delPt = _db.Connection.CreateCommand();
                    delPt.CommandText = "DELETE FROM playlist_tracks WHERE track_id = $id";
                    delPt.Parameters.AddWithValue("$id", id);
                    delPt.ExecuteNonQuery();
                }

                using var del = _db.Connection.CreateCommand();
                del.CommandText = "DELETE FROM tracks WHERE file_path = $path";
                del.Parameters.AddWithValue("$path", path);
                del.ExecuteNonQuery();
            }
        }
    }

    private static string NormalizeDirectoryPath(string path)
    {
        var full = Path.GetFullPath(path);
        return full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static bool IsUnderAnyRoot(string filePath, IReadOnlyList<string> rootPaths)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return false;

        string fullFile;
        try
        {
            fullFile = Path.GetFullPath(filePath);
        }
        catch
        {
            return false;
        }

        foreach (var root in rootPaths)
        {
            if (string.Equals(fullFile, root, StringComparison.OrdinalIgnoreCase))
                return true;

            var prefix = root + Path.DirectorySeparatorChar;
            if (fullFile.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    public IReadOnlyList<LibraryTrack> GetAllTracks(string? search = null) =>
        QueryTracks(search, filters: null);

    public IReadOnlyList<LibraryTrack> GetInboxTracks(string? search = null) =>
        QueryTracks(search, filters: null, reviewStatus: "inbox");

    public void SetReviewStatus(IEnumerable<long> trackIds, string status)
    {
        lock (_gate)
        {
            foreach (var id in trackIds)
            {
                using var cmd = _db.Connection.CreateCommand();
                cmd.CommandText = "UPDATE tracks SET review_status = $status WHERE id = $id";
                cmd.Parameters.AddWithValue("$status", status);
                cmd.Parameters.AddWithValue("$id", id);
                cmd.ExecuteNonQuery();
            }
        }
    }

    public ArtistLibraryStats GetArtistStats(string artist)
    {
        lock (_gate)
        {
            using var cmd = _db.Connection.CreateCommand();
            cmd.CommandText = """
                SELECT COUNT(*), COALESCE(SUM(play_count), 0), COUNT(DISTINCT album)
                FROM tracks
                WHERE artist = $artist COLLATE NOCASE OR album_artist = $artist COLLATE NOCASE
                """;
            cmd.Parameters.AddWithValue("$artist", artist);
            using var reader = cmd.ExecuteReader();
            reader.Read();
            return new ArtistLibraryStats
            {
                TrackCount = reader.GetInt32(0),
                PlayCount = reader.GetInt32(1),
                AlbumCount = reader.GetInt32(2),
            };
        }
    }

    /// <summary>Tracks matching optional text search and/or smart-playlist style filter rules.</summary>
    public IReadOnlyList<LibraryTrack> QueryTracks(string? search = null, SmartPlaylistRules? filters = null, string? reviewStatus = null)
    {
        lock (_gate)
        {
            using var cmd = _db.Connection.CreateCommand();
            var clauses = new List<string>();

            if (filters is { Rules.Count: > 0 })
            {
                var where = SmartPlaylistEvaluator.BuildWhereClause(filters, cmd);
                if (!string.IsNullOrWhiteSpace(where) && where != "1=1")
                    clauses.Add(where);
            }

            if (!string.IsNullOrWhiteSpace(search))
            {
                clauses.Add("""
                    (
                        title LIKE $q OR artist LIKE $q OR album LIKE $q OR album_artist LIKE $q OR genre LIKE $q
                    )
                    """);
                cmd.Parameters.AddWithValue("$q", $"%{search.Trim()}%");
            }

            if (!string.IsNullOrWhiteSpace(reviewStatus))
            {
                clauses.Add("review_status = $review_status");
                cmd.Parameters.AddWithValue("$review_status", reviewStatus);
            }

            var whereSql = clauses.Count == 0 ? "1=1" : string.Join(" AND ", clauses);
            cmd.CommandText = $"""
                SELECT * FROM tracks
                WHERE {whereSql}
                ORDER BY album_artist, album, track_number, title
                """;
            return ReadTracks(cmd);
        }
    }

    public IReadOnlyList<LibraryTrack> GetTracksForAlbum(string album)
    {
        lock (_gate)
        {
            using var cmd = _db.Connection.CreateCommand();
            cmd.CommandText = """
                SELECT * FROM tracks
                WHERE LOWER(TRIM(COALESCE(NULLIF(album, ''), 'Unknown Album'))) = $key
                ORDER BY track_number, title
                """;
            cmd.Parameters.AddWithValue("$key", NormalizeAlbumKey(album));
            return ReadTracks(cmd);
        }
    }

    public IReadOnlyList<LibraryAlbum> GetAlbums()
    {
        lock (_gate)
        {
            using var cmd = _db.Connection.CreateCommand();
            // Metadata only — no cover blobs (covers loaded once per album below).
            cmd.CommandText = """
                SELECT
                    LOWER(TRIM(COALESCE(NULLIF(album, ''), 'Unknown Album'))) AS album_key,
                    COALESCE(NULLIF(album, ''), 'Unknown Album') AS album_name,
                    album_artist,
                    artist,
                    year
                FROM tracks
                """;

            var groups = new Dictionary<string, AlbumAgg>(StringComparer.Ordinal);
            using (var reader = cmd.ExecuteReader())
            {
                while (reader.Read())
                {
                    var key = reader.GetString(0);
                    if (!groups.TryGetValue(key, out var agg))
                    {
                        agg = new AlbumAgg
                        {
                            Key = key,
                            Album = reader.GetString(1),
                        };
                        groups[key] = agg;
                    }

                    agg.TrackCount++;
                    var albumArtist = reader.IsDBNull(2) ? "" : reader.GetString(2);
                    var artist = reader.IsDBNull(3) ? "" : reader.GetString(3);
                    if (!string.IsNullOrWhiteSpace(albumArtist))
                        agg.AlbumArtistVotes[albumArtist] = agg.AlbumArtistVotes.GetValueOrDefault(albumArtist) + 1;
                    if (!string.IsNullOrWhiteSpace(artist))
                        agg.ArtistVotes[artist] = agg.ArtistVotes.GetValueOrDefault(artist) + 1;
                    if (!reader.IsDBNull(4))
                    {
                        var year = reader.GetInt32(4);
                        if (year > 0 && (agg.Year is null || year < agg.Year))
                            agg.Year = year;
                    }
                }
            }

            var albums = new List<LibraryAlbum>(groups.Count);
            var coverMap = LoadAlbumCoverMap(groups.Keys);
            foreach (var agg in groups.Values)
            {
                albums.Add(new LibraryAlbum
                {
                    Key = agg.Key,
                    Album = agg.Album,
                    AlbumArtist = PickCanonicalArtist(agg.AlbumArtistVotes, agg.ArtistVotes),
                    TrackCount = agg.TrackCount,
                    Year = agg.Year,
                    CoverArt = coverMap.GetValueOrDefault(agg.Key),
                });
            }

            return albums
                .OrderBy(a => a.AlbumArtist, StringComparer.OrdinalIgnoreCase)
                .ThenBy(a => a.Album, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }

    private byte[]? LoadOneCoverForAlbum(string albumKey) =>
        LoadAlbumCoverMap([albumKey]).GetValueOrDefault(albumKey);

    private Dictionary<string, byte[]> LoadAlbumCoverMap(IEnumerable<string> keys)
    {
        var keyList = keys.Distinct(StringComparer.Ordinal).ToList();
        if (keyList.Count == 0)
            return new Dictionary<string, byte[]>(StringComparer.Ordinal);

        using var cmd = _db.Connection.CreateCommand();
        var placeholders = string.Join(",", keyList.Select((_, i) => $"$k{i}"));
        for (var i = 0; i < keyList.Count; i++)
            cmd.Parameters.AddWithValue($"$k{i}", keyList[i]);

        cmd.CommandText = $"""
            SELECT album_key, cover_art FROM (
                SELECT LOWER(TRIM(COALESCE(NULLIF(album, ''), 'Unknown Album'))) AS album_key,
                       cover_art,
                       ROW_NUMBER() OVER (
                           PARTITION BY LOWER(TRIM(COALESCE(NULLIF(album, ''), 'Unknown Album')))
                           ORDER BY id) AS rn
                FROM tracks
                WHERE cover_art IS NOT NULL
                  AND LOWER(TRIM(COALESCE(NULLIF(album, ''), 'Unknown Album'))) IN ({placeholders})
            )
            WHERE rn = 1
            """;

        var map = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            if (!reader.IsDBNull(1))
                map[reader.GetString(0)] = (byte[])reader.GetValue(1);
        }

        return map;
    }

    private static string PickCanonicalArtist(
        Dictionary<string, int> albumArtistVotes,
        Dictionary<string, int> artistVotes)
    {
        var albumArtist = albumArtistVotes
            .OrderByDescending(kv => kv.Value)
            .Select(kv => kv.Key)
            .FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(albumArtist))
            return albumArtist;

        var trackArtist = artistVotes
            .OrderByDescending(kv => kv.Value)
            .Select(kv => kv.Key)
            .FirstOrDefault();
        return string.IsNullOrWhiteSpace(trackArtist) ? "Unknown Artist" : trackArtist;
    }

    private sealed class AlbumAgg
    {
        public required string Key { get; init; }
        public required string Album { get; init; }
        public int TrackCount { get; set; }
        public int? Year { get; set; }
        public Dictionary<string, int> AlbumArtistVotes { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, int> ArtistVotes { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<LibraryArtist> GetArtists()
    {
        lock (_gate)
        {
            using var cmd = _db.Connection.CreateCommand();
            cmd.CommandText = """
                SELECT COALESCE(NULLIF(artist, ''), album_artist, 'Unknown Artist') AS name,
                       COUNT(*),
                       COUNT(DISTINCT LOWER(TRIM(COALESCE(NULLIF(album, ''), 'Unknown Album'))))
                FROM tracks
                GROUP BY name
                ORDER BY name
                """;
            var list = new List<LibraryArtist>();
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                list.Add(new LibraryArtist
                {
                    Name = reader.GetString(0),
                    TrackCount = reader.GetInt32(1),
                    AlbumCount = reader.GetInt32(2),
                });
            }

            return list;
        }
    }

    public IReadOnlyList<LibraryGenre> GetGenres()
    {
        lock (_gate)
        {
            using var cmd = _db.Connection.CreateCommand();
            cmd.CommandText = """
                SELECT TRIM(genre), TRIM(COALESCE(NULLIF(album, ''), 'Unknown Album'))
                FROM tracks
                WHERE TRIM(COALESCE(genre, '')) != ''
                """;
            var counts = new Dictionary<string, (int Tracks, HashSet<string> Albums)>(StringComparer.OrdinalIgnoreCase);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var genreRaw = reader.GetString(0);
                var album = reader.GetString(1);
                foreach (var name in GenreNormalizer.SplitGenres(genreRaw))
                {
                    if (!counts.TryGetValue(name, out var entry))
                        entry = (0, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
                    entry.Albums.Add(album);
                    counts[name] = (entry.Tracks + 1, entry.Albums);
                }
            }

            return counts
                .OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
                .Select(kv => new LibraryGenre
                {
                    Name = kv.Key,
                    TrackCount = kv.Value.Tracks,
                    AlbumCount = kv.Value.Albums.Count,
                })
                .ToList();
        }
    }

    public IReadOnlyList<LibraryTrack> GetTracksForGenre(string genre)
    {
        lock (_gate)
        {
            using var cmd = _db.Connection.CreateCommand();
            cmd.CommandText = """
                SELECT * FROM tracks
                WHERE TRIM(COALESCE(genre, '')) != ''
                ORDER BY album_artist, album, track_number, title
                """;
            return ReadTracks(cmd)
                .Where(t => GenreNormalizer.ContainsGenre(t.Genre, genre))
                .ToList();
        }
    }

    public IReadOnlyList<string> GetDistinctArtists()
    {
        lock (_gate)
        {
            using var cmd = _db.Connection.CreateCommand();
            cmd.CommandText = """
                SELECT DISTINCT name FROM (
                    SELECT TRIM(artist) AS name FROM tracks WHERE TRIM(COALESCE(artist, '')) != ''
                    UNION
                    SELECT TRIM(album_artist) AS name FROM tracks WHERE TRIM(COALESCE(album_artist, '')) != ''
                )
                ORDER BY name COLLATE NOCASE
                """;
            return ReadStringList(cmd);
        }
    }

    public IReadOnlyList<string> GetDistinctAlbums()
    {
        lock (_gate)
        {
            using var cmd = _db.Connection.CreateCommand();
            cmd.CommandText = """
                SELECT DISTINCT TRIM(COALESCE(NULLIF(album, ''), 'Unknown Album')) AS name
                FROM tracks
                ORDER BY name COLLATE NOCASE
                """;
            return ReadStringList(cmd);
        }
    }

    public IReadOnlyList<string> GetDistinctGenres()
    {
        lock (_gate)
        {
            using var cmd = _db.Connection.CreateCommand();
            cmd.CommandText = """
                SELECT TRIM(genre)
                FROM tracks
                WHERE TRIM(COALESCE(genre, '')) != ''
                """;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var list = new List<string>();
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                foreach (var name in GenreNormalizer.SplitGenres(reader.GetString(0)))
                {
                    if (seen.Add(name))
                        list.Add(name);
                }
            }

            list.Sort(StringComparer.OrdinalIgnoreCase);
            return list;
        }
    }

    public IReadOnlyList<string> GetDistinctFormats()
    {
        lock (_gate)
        {
            using var cmd = _db.Connection.CreateCommand();
            cmd.CommandText = """
                SELECT DISTINCT UPPER(TRIM(format)) AS name
                FROM tracks
                WHERE TRIM(COALESCE(format, '')) != ''
                ORDER BY name COLLATE NOCASE
                """;
            return ReadStringList(cmd);
        }
    }

    private static List<string> ReadStringList(SqliteCommand cmd)
    {
        var list = new List<string>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var value = reader.GetString(0);
            if (!string.IsNullOrWhiteSpace(value))
                list.Add(value);
        }

        return list;
    }

    public int TrackCount()
    {
        lock (_gate)
        {
            using var cmd = _db.Connection.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM tracks";
            return Convert.ToInt32(cmd.ExecuteScalar());
        }
    }

    public LibraryStatistics GetStatistics()
    {
        lock (_gate)
        {
            var stats = new LibraryStatistics
            {
                TrackCount = ScalarInt("SELECT COUNT(*) FROM tracks"),
                AlbumCount = ScalarInt("""
                    SELECT COUNT(DISTINCT LOWER(TRIM(COALESCE(NULLIF(album, ''), 'Unknown Album')))) FROM tracks
                    """),
                ArtistCount = ScalarInt("""
                    SELECT COUNT(DISTINCT COALESCE(NULLIF(artist, ''), album_artist, 'Unknown Artist')) FROM tracks
                    """),
                TotalDuration = TimeSpan.FromMilliseconds(ScalarLong("SELECT COALESCE(SUM(duration_ms), 0) FROM tracks")),
                NeverPlayedCount = ScalarInt("SELECT COUNT(*) FROM tracks WHERE play_count = 0"),
                RecentlyAddedCount = ScalarInt(
                    "SELECT COUNT(*) FROM tracks WHERE date_added_utc >= $cutoff",
                    ("$cutoff", DateTime.UtcNow.AddDays(-30).ToString("O"))),
                FormatBreakdown = ReadStatCounts("""
                    SELECT COALESCE(NULLIF(format, ''), 'Unknown') AS label, COUNT(*) AS cnt
                    FROM tracks GROUP BY label ORDER BY cnt DESC, label
                    """),
                RatingBreakdown = ReadStatCounts("""
                    SELECT CAST(rating AS TEXT) AS label, COUNT(*) AS cnt
                    FROM tracks GROUP BY rating ORDER BY rating DESC
                    """),
                TopArtistsByPlays = ReadRankedItems("""
                    SELECT COALESCE(NULLIF(artist, ''), album_artist, 'Unknown Artist') AS name,
                           SUM(play_count) AS cnt
                    FROM tracks
                    GROUP BY name
                    ORDER BY cnt DESC, name
                    LIMIT 10
                    """),
                TopTracksByPlays = ReadRankedTracks("""
                    SELECT id, title, artist, play_count
                    FROM tracks
                    ORDER BY play_count DESC, title
                    LIMIT 10
                    """),
            };
            return stats;
        }
    }

    public IReadOnlyList<DuplicateGroup> FindDuplicateGroups()
    {
        lock (_gate)
        {
            using var cmd = _db.Connection.CreateCommand();
            cmd.CommandText = """
                SELECT * FROM tracks
                ORDER BY LOWER(TRIM(title)), LOWER(TRIM(artist)), duration_ms, file_path
                """;
            var tracks = ReadTracks(cmd);
            var groups = new Dictionary<string, List<LibraryTrack>>(StringComparer.Ordinal);
            foreach (var track in tracks)
            {
                var title = NormalizeDupKey(track.Title);
                var artist = NormalizeDupKey(string.IsNullOrWhiteSpace(track.Artist) ? track.AlbumArtist : track.Artist);
                if (string.IsNullOrWhiteSpace(title))
                    continue;

                var durationBucket = (long)Math.Round(track.Duration.TotalMilliseconds / 1000.0);
                var key = $"{title}|{artist}|{durationBucket}";
                if (!groups.TryGetValue(key, out var list))
                {
                    list = [];
                    groups[key] = list;
                }

                list.Add(track);
            }

            return groups.Values
                .Where(g => g.Count > 1)
                .Select(g => new DuplicateGroup
                {
                    Key = $"{g[0].DisplayTitle} · {g[0].DisplayArtist}",
                    Tracks = g.OrderBy(t => t.FilePath, StringComparer.OrdinalIgnoreCase).ToList(),
                })
                .OrderByDescending(g => g.Tracks.Count)
                .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }

    private int ScalarInt(string sql, params (string Name, object Value)[] parameters)
    {
        using var cmd = _db.Connection.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, value) in parameters)
            cmd.Parameters.AddWithValue(name, value);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private long ScalarLong(string sql)
    {
        using var cmd = _db.Connection.CreateCommand();
        cmd.CommandText = sql;
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    private List<StatCount> ReadStatCounts(string sql)
    {
        using var cmd = _db.Connection.CreateCommand();
        cmd.CommandText = sql;
        var list = new List<StatCount>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new StatCount
            {
                Label = reader.GetString(0),
                Count = reader.GetInt32(1),
            });
        }

        return list;
    }

    private List<RankedItem> ReadRankedItems(string sql)
    {
        using var cmd = _db.Connection.CreateCommand();
        cmd.CommandText = sql;
        var list = new List<RankedItem>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new RankedItem
            {
                Name = reader.GetString(0),
                Count = reader.GetInt32(1),
            });
        }

        return list;
    }

    private List<RankedTrack> ReadRankedTracks(string sql)
    {
        using var cmd = _db.Connection.CreateCommand();
        cmd.CommandText = sql;
        var list = new List<RankedTrack>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new RankedTrack
            {
                Id = reader.GetInt64(0),
                Title = reader.GetString(1),
                Artist = reader.GetString(2),
                PlayCount = reader.GetInt32(3),
            });
        }

        return list;
    }

    private static string NormalizeDupKey(string value) =>
        string.IsNullOrWhiteSpace(value) ? "" : value.Trim().ToLowerInvariant();

    public IReadOnlyList<LibraryPlaylist> GetPlaylists(bool includeCoverArt = true)
    {
        lock (_gate)
        {
            using var cmd = _db.Connection.CreateCommand();
            cmd.CommandText = """
                SELECT p.id, p.name, p.created_utc, p.modified_utc, p.is_smart, p.rules_json, p.folder_id
                FROM playlists p
                ORDER BY p.name COLLATE NOCASE
                """;
            var list = new List<LibraryPlaylist>();
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var isSmart = !reader.IsDBNull(4) && reader.GetInt32(4) != 0;
                var rulesJson = reader.IsDBNull(5) ? null : reader.GetString(5);
                var rules = SmartPlaylistRules.FromJson(rulesJson) ?? new SmartPlaylistRules();
                var id = reader.GetInt64(0);
                var trackCount = isSmart
                    ? CountSmartPlaylistTracks(rules)
                    : CountManualPlaylistTracks(id);
                long? folderId = reader.FieldCount > 6 && !reader.IsDBNull(6) ? reader.GetInt64(6) : null;

                list.Add(new LibraryPlaylist
                {
                    Id = id,
                    Name = reader.GetString(1),
                    CreatedUtc = DateTime.Parse(reader.GetString(2)),
                    ModifiedUtc = DateTime.Parse(reader.GetString(3)),
                    IsSmart = isSmart,
                    Rules = rules,
                    TrackCount = trackCount,
                    CoverArt = includeCoverArt ? LoadPlaylistCoverTiles(id, isSmart, rules) : null,
                    FolderId = folderId,
                });
            }

            return list;
        }
    }

    public LibraryPlaylist? GetPlaylist(long playlistId)
    {
        lock (_gate)
        {
            using var cmd = _db.Connection.CreateCommand();
            cmd.CommandText = """
                SELECT id, name, created_utc, modified_utc, is_smart, rules_json, folder_id
                FROM playlists WHERE id = $id
                """;
            cmd.Parameters.AddWithValue("$id", playlistId);
            using var reader = cmd.ExecuteReader();
            if (!reader.Read())
                return null;

            var isSmart = !reader.IsDBNull(4) && reader.GetInt32(4) != 0;
            var rulesJson = reader.IsDBNull(5) ? null : reader.GetString(5);
            var rules = SmartPlaylistRules.FromJson(rulesJson) ?? new SmartPlaylistRules();
            var id = reader.GetInt64(0);
            long? folderId = !reader.IsDBNull(6) ? reader.GetInt64(6) : null;
            return new LibraryPlaylist
            {
                Id = id,
                Name = reader.GetString(1),
                CreatedUtc = DateTime.Parse(reader.GetString(2)),
                ModifiedUtc = DateTime.Parse(reader.GetString(3)),
                IsSmart = isSmart,
                Rules = rules,
                TrackCount = isSmart ? CountSmartPlaylistTracks(rules) : CountManualPlaylistTracks(id),
                CoverArt = LoadPlaylistCoverTiles(id, isSmart, rules),
                FolderId = folderId,
            };
        }
    }

    public LibraryPlaylist CreatePlaylist(string name, long? folderId = null)
    {
        lock (_gate)
        {
            var now = DateTime.UtcNow.ToString("O");
            using var cmd = _db.Connection.CreateCommand();
            cmd.CommandText = """
                INSERT INTO playlists (name, created_utc, modified_utc, is_smart, folder_id)
                VALUES ($name, $now, $now, 0, $folder_id);
                SELECT last_insert_rowid();
                """;
            cmd.Parameters.AddWithValue("$name", name.Trim());
            cmd.Parameters.AddWithValue("$now", now);
            cmd.Parameters.AddWithValue("$folder_id", folderId.HasValue ? folderId.Value : DBNull.Value);
            var id = Convert.ToInt64(cmd.ExecuteScalar());
            return GetPlaylist(id)!;
        }
    }

    public LibraryPlaylist CreateSmartPlaylist(string name, SmartPlaylistRules rules)
    {
        lock (_gate)
        {
            var now = DateTime.UtcNow.ToString("O");
            var json = rules.ToJson();
            using var cmd = _db.Connection.CreateCommand();
            cmd.CommandText = """
                INSERT INTO playlists (name, created_utc, modified_utc, is_smart, rules_json)
                VALUES ($name, $now, $now, 1, $rules);
                SELECT last_insert_rowid();
                """;
            cmd.Parameters.AddWithValue("$name", name.Trim());
            cmd.Parameters.AddWithValue("$now", now);
            cmd.Parameters.AddWithValue("$rules", json);
            var id = Convert.ToInt64(cmd.ExecuteScalar());
            return new LibraryPlaylist
            {
                Id = id,
                Name = name.Trim(),
                CreatedUtc = DateTime.Parse(now),
                ModifiedUtc = DateTime.Parse(now),
                IsSmart = true,
                Rules = rules,
                TrackCount = CountSmartPlaylistTracks(rules),
            };
        }
    }

    public void UpdateSmartPlaylist(long playlistId, string name, SmartPlaylistRules rules)
    {
        lock (_gate)
        {
            using var cmd = _db.Connection.CreateCommand();
            cmd.CommandText = """
                UPDATE playlists
                SET name = $name, rules_json = $rules, modified_utc = $now
                WHERE id = $id AND is_smart = 1
                """;
            cmd.Parameters.AddWithValue("$id", playlistId);
            cmd.Parameters.AddWithValue("$name", name.Trim());
            cmd.Parameters.AddWithValue("$rules", rules.ToJson());
            cmd.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("O"));
            cmd.ExecuteNonQuery();
        }
    }

    public void RenamePlaylist(long playlistId, string name)
    {
        lock (_gate)
        {
            using var cmd = _db.Connection.CreateCommand();
            cmd.CommandText = """
                UPDATE playlists SET name = $name, modified_utc = $now WHERE id = $id
                """;
            cmd.Parameters.AddWithValue("$id", playlistId);
            cmd.Parameters.AddWithValue("$name", name.Trim());
            cmd.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("O"));
            cmd.ExecuteNonQuery();
        }
    }

    public void DeletePlaylist(long playlistId)
    {
        lock (_gate)
        {
            using var cmd = _db.Connection.CreateCommand();
            cmd.CommandText = """
                DELETE FROM playlist_tracks WHERE playlist_id = $id;
                DELETE FROM playlists WHERE id = $id;
                """;
            cmd.Parameters.AddWithValue("$id", playlistId);
            cmd.ExecuteNonQuery();
        }
    }

    public IReadOnlyList<PlaylistFolder> GetPlaylistFolders()
    {
        lock (_gate)
        {
            using var cmd = _db.Connection.CreateCommand();
            cmd.CommandText = """
                SELECT id, name, parent_id
                FROM playlist_folders
                ORDER BY name COLLATE NOCASE
                """;
            var list = new List<PlaylistFolder>();
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                list.Add(new PlaylistFolder
                {
                    Id = reader.GetInt64(0),
                    Name = reader.GetString(1),
                    ParentId = reader.IsDBNull(2) ? null : reader.GetInt64(2),
                });
            }

            return list;
        }
    }

    public PlaylistFolder CreatePlaylistFolder(string name, long? parentId = null)
    {
        lock (_gate)
        {
            using var cmd = _db.Connection.CreateCommand();
            cmd.CommandText = """
                INSERT INTO playlist_folders (name, parent_id)
                VALUES ($name, $parent_id);
                SELECT last_insert_rowid();
                """;
            cmd.Parameters.AddWithValue("$name", name.Trim());
            cmd.Parameters.AddWithValue("$parent_id", parentId.HasValue ? parentId.Value : DBNull.Value);
            var id = Convert.ToInt64(cmd.ExecuteScalar());
            return GetPlaylistFolders().First(f => f.Id == id);
        }
    }

    public void RenamePlaylistFolder(long folderId, string name)
    {
        lock (_gate)
        {
            using var cmd = _db.Connection.CreateCommand();
            cmd.CommandText = "UPDATE playlist_folders SET name = $name WHERE id = $id";
            cmd.Parameters.AddWithValue("$name", name.Trim());
            cmd.Parameters.AddWithValue("$id", folderId);
            cmd.ExecuteNonQuery();
        }
    }

    public void DeletePlaylistFolder(long folderId)
    {
        lock (_gate)
        {
            using var clear = _db.Connection.CreateCommand();
            clear.CommandText = "UPDATE playlists SET folder_id = NULL WHERE folder_id = $id";
            clear.Parameters.AddWithValue("$id", folderId);
            clear.ExecuteNonQuery();

            using var cmd = _db.Connection.CreateCommand();
            cmd.CommandText = "DELETE FROM playlist_folders WHERE id = $id";
            cmd.Parameters.AddWithValue("$id", folderId);
            cmd.ExecuteNonQuery();
        }
    }

    public void MovePlaylistToFolder(long playlistId, long? folderId)
    {
        lock (_gate)
        {
            using var cmd = _db.Connection.CreateCommand();
            cmd.CommandText = "UPDATE playlists SET folder_id = $folder_id, modified_utc = $modified WHERE id = $id";
            cmd.Parameters.AddWithValue("$folder_id", folderId.HasValue ? folderId.Value : DBNull.Value);
            cmd.Parameters.AddWithValue("$modified", DateTime.UtcNow.ToString("O"));
            cmd.Parameters.AddWithValue("$id", playlistId);
            cmd.ExecuteNonQuery();
        }
    }

    public IReadOnlyList<PlaylistTreeNode> GetPlaylistTree(bool includeCoverArt = false)
    {
        lock (_gate)
        {
            var folders = GetPlaylistFolders();
            var playlists = GetPlaylists(includeCoverArt);
            return BuildPlaylistTreeNodes(null, folders, playlists);
        }
    }

    private static List<PlaylistTreeNode> BuildPlaylistTreeNodes(
        long? parentFolderId,
        IReadOnlyList<PlaylistFolder> folders,
        IReadOnlyList<LibraryPlaylist> playlists)
    {
        var nodes = new List<PlaylistTreeNode>();
        foreach (var folder in folders.Where(f => f.ParentId == parentFolderId).OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase))
        {
            var node = new PlaylistTreeNode
            {
                IsFolder = true,
                FolderId = folder.Id,
                Name = folder.Name,
            };
            node.Children.AddRange(BuildPlaylistTreeNodes(folder.Id, folders, playlists));
            nodes.Add(node);
        }

        foreach (var playlist in playlists.Where(p => p.FolderId == parentFolderId).OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase))
        {
            nodes.Add(new PlaylistTreeNode
            {
                IsFolder = false,
                Playlist = playlist,
                Name = playlist.Name,
            });
        }

        return nodes;
    }

    public void AddTracksToPlaylist(long playlistId, IEnumerable<long> trackIds)
    {
        lock (_gate)
        {
            if (IsSmartPlaylist(playlistId))
                return;

            using var maxCmd = _db.Connection.CreateCommand();
            maxCmd.CommandText = "SELECT COALESCE(MAX(position), -1) FROM playlist_tracks WHERE playlist_id = $id";
            maxCmd.Parameters.AddWithValue("$id", playlistId);
            var position = Convert.ToInt32(maxCmd.ExecuteScalar()) + 1;

            foreach (var trackId in trackIds.Distinct())
            {
                using var exists = _db.Connection.CreateCommand();
                exists.CommandText = "SELECT 1 FROM playlist_tracks WHERE playlist_id = $pid AND track_id = $tid";
                exists.Parameters.AddWithValue("$pid", playlistId);
                exists.Parameters.AddWithValue("$tid", trackId);
                if (exists.ExecuteScalar() is not null)
                    continue;

                using var insert = _db.Connection.CreateCommand();
                insert.CommandText = """
                    INSERT INTO playlist_tracks (playlist_id, track_id, position)
                    VALUES ($pid, $tid, $pos)
                    """;
                insert.Parameters.AddWithValue("$pid", playlistId);
                insert.Parameters.AddWithValue("$tid", trackId);
                insert.Parameters.AddWithValue("$pos", position++);
                insert.ExecuteNonQuery();
            }

            TouchPlaylist(playlistId);
        }
    }

    public void RemoveTrackFromPlaylist(long playlistId, long trackId)
    {
        lock (_gate)
        {
            if (IsSmartPlaylist(playlistId))
                return;

            using var cmd = _db.Connection.CreateCommand();
            cmd.CommandText = "DELETE FROM playlist_tracks WHERE playlist_id = $pid AND track_id = $tid";
            cmd.Parameters.AddWithValue("$pid", playlistId);
            cmd.Parameters.AddWithValue("$tid", trackId);
            cmd.ExecuteNonQuery();
            TouchPlaylist(playlistId);
        }
    }

    public IReadOnlyList<LibraryTrack> GetPlaylistTracks(long playlistId)
    {
        lock (_gate)
        {
            using var meta = _db.Connection.CreateCommand();
            meta.CommandText = "SELECT is_smart, rules_json FROM playlists WHERE id = $id";
            meta.Parameters.AddWithValue("$id", playlistId);
            using var reader = meta.ExecuteReader();
            if (!reader.Read())
                return [];

            var isSmart = !reader.IsDBNull(0) && reader.GetInt32(0) != 0;
            if (isSmart)
            {
                var rulesJson = reader.IsDBNull(1) ? null : reader.GetString(1);
                var rules = SmartPlaylistRules.FromJson(rulesJson) ?? new SmartPlaylistRules();
                return QuerySmartPlaylistTracks(rules);
            }

            using var cmd = _db.Connection.CreateCommand();
            cmd.CommandText = """
                SELECT t.* FROM tracks t
                INNER JOIN playlist_tracks pt ON pt.track_id = t.id
                WHERE pt.playlist_id = $id
                ORDER BY pt.position, t.title
                """;
            cmd.Parameters.AddWithValue("$id", playlistId);
            return ReadTracks(cmd);
        }
    }

    private int CountManualPlaylistTracks(long playlistId)
    {
        using var cmd = _db.Connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM playlist_tracks WHERE playlist_id = $id";
        cmd.Parameters.AddWithValue("$id", playlistId);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private int CountSmartPlaylistTracks(SmartPlaylistRules rules)
    {
        using var cmd = _db.Connection.CreateCommand();
        var where = SmartPlaylistEvaluator.BuildWhereClause(rules, cmd);
        cmd.CommandText = $"SELECT COUNT(*) FROM tracks WHERE {where}";
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    public int CountTracksMatching(SmartPlaylistRules rules)
    {
        lock (_gate)
            return CountSmartPlaylistTracks(rules);
    }

    public IReadOnlyList<LibraryAlbum> GetRecentlyPlayedAlbums(int limit = 12)
    {
        lock (_gate)
        {
            using var cmd = _db.Connection.CreateCommand();
            cmd.CommandText = """
                SELECT
                    LOWER(TRIM(COALESCE(NULLIF(album, ''), 'Unknown Album'))) AS album_key,
                    COALESCE(NULLIF(album, ''), 'Unknown Album') AS album_name,
                    MAX(last_played_utc) AS last_played
                FROM tracks
                WHERE last_played_utc IS NOT NULL AND TRIM(last_played_utc) != ''
                GROUP BY album_key
                ORDER BY last_played DESC
                LIMIT $limit
                """;
            cmd.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 50));

            var albums = new List<LibraryAlbum>();
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                albums.Add(new LibraryAlbum
                {
                    Key = reader.GetString(0),
                    Album = reader.GetString(1),
                    AlbumArtist = "",
                });
            }

            return MaterializeAlbums(albums);
        }
    }

    public IReadOnlyList<LibraryAlbum> GetRecentlyAddedAlbums(int limit = 12)
    {
        lock (_gate)
        {
            using var cmd = _db.Connection.CreateCommand();
            cmd.CommandText = """
                SELECT
                    LOWER(TRIM(COALESCE(NULLIF(album, ''), 'Unknown Album'))) AS album_key,
                    COALESCE(NULLIF(album, ''), 'Unknown Album') AS album_name,
                    MAX(date_added_utc) AS added
                FROM tracks
                GROUP BY album_key
                ORDER BY added DESC
                LIMIT $limit
                """;
            cmd.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 50));

            var albums = new List<LibraryAlbum>();
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                albums.Add(new LibraryAlbum
                {
                    Key = reader.GetString(0),
                    Album = reader.GetString(1),
                    AlbumArtist = "",
                });
            }

            return MaterializeAlbums(albums);
        }
    }

    private List<LibraryAlbum> MaterializeAlbums(List<LibraryAlbum> shells)
    {
        if (shells.Count == 0)
            return shells;

        var keys = shells.Select(a => a.Key).ToList();
        var summaries = LoadAlbumSummaries(keys);
        var coverMap = LoadAlbumCoverMap(keys);
        for (var i = 0; i < shells.Count; i++)
        {
            var shell = shells[i];
            summaries.TryGetValue(shell.Key, out var summary);
            shells[i] = new LibraryAlbum
            {
                Key = shell.Key,
                Album = shell.Album,
                AlbumArtist = summary?.AlbumArtist ?? "Unknown Artist",
                TrackCount = summary?.TrackCount ?? 0,
                Year = summary?.Year,
                CoverArt = coverMap.GetValueOrDefault(shell.Key),
            };
        }

        return shells;
    }

    private Dictionary<string, AlbumSummary> LoadAlbumSummaries(IReadOnlyList<string> keys)
    {
        if (keys.Count == 0)
            return new Dictionary<string, AlbumSummary>(StringComparer.Ordinal);

        using var cmd = _db.Connection.CreateCommand();
        var placeholders = string.Join(",", keys.Select((_, i) => $"$k{i}"));
        for (var i = 0; i < keys.Count; i++)
            cmd.Parameters.AddWithValue($"$k{i}", keys[i]);

        cmd.CommandText = $"""
            SELECT
                LOWER(TRIM(COALESCE(NULLIF(album, ''), 'Unknown Album'))) AS album_key,
                album_artist,
                artist,
                year
            FROM tracks
            WHERE LOWER(TRIM(COALESCE(NULLIF(album, ''), 'Unknown Album'))) IN ({placeholders})
            """;

        var groups = new Dictionary<string, AlbumAgg>(StringComparer.Ordinal);
        using (var reader = cmd.ExecuteReader())
        {
            while (reader.Read())
            {
                var key = reader.GetString(0);
                if (!groups.TryGetValue(key, out var agg))
                {
                    agg = new AlbumAgg { Key = key, Album = key };
                    groups[key] = agg;
                }

                agg.TrackCount++;
                var albumArtist = reader.IsDBNull(1) ? "" : reader.GetString(1);
                var artist = reader.IsDBNull(2) ? "" : reader.GetString(2);
                if (!string.IsNullOrWhiteSpace(albumArtist))
                    agg.AlbumArtistVotes[albumArtist] = agg.AlbumArtistVotes.GetValueOrDefault(albumArtist) + 1;
                if (!string.IsNullOrWhiteSpace(artist))
                    agg.ArtistVotes[artist] = agg.ArtistVotes.GetValueOrDefault(artist) + 1;
                if (!reader.IsDBNull(3))
                {
                    var year = reader.GetInt32(3);
                    if (year > 0 && (agg.Year is null || year < agg.Year))
                        agg.Year = year;
                }
            }
        }

        return groups.ToDictionary(
            kv => kv.Key,
            kv => new AlbumSummary
            {
                TrackCount = kv.Value.TrackCount,
                AlbumArtist = PickCanonicalArtist(kv.Value.AlbumArtistVotes, kv.Value.ArtistVotes),
                Year = kv.Value.Year,
            },
            StringComparer.Ordinal);
    }

    private sealed class AlbumSummary
    {
        public int TrackCount { get; init; }
        public string AlbumArtist { get; init; } = "";
        public int? Year { get; init; }
    }

    private LibraryAlbum BuildAlbumFromKey(string key, string albumName)
    {
        var summary = LoadAlbumSummaries([key]).GetValueOrDefault(key);
        return new LibraryAlbum
        {
            Key = key,
            Album = albumName,
            AlbumArtist = summary?.AlbumArtist ?? "Unknown Artist",
            TrackCount = summary?.TrackCount ?? 0,
            Year = summary?.Year,
            CoverArt = LoadOneCoverForAlbum(key),
        };
    }

    private List<LibraryTrack> QuerySmartPlaylistTracks(SmartPlaylistRules rules)
    {
        using var cmd = _db.Connection.CreateCommand();
        var where = SmartPlaylistEvaluator.BuildWhereClause(rules, cmd);
        cmd.CommandText = $"SELECT * FROM tracks WHERE {where} ORDER BY title COLLATE NOCASE";
        return ReadTracks(cmd);
    }

    private bool IsSmartPlaylist(long playlistId)
    {
        using var cmd = _db.Connection.CreateCommand();
        cmd.CommandText = "SELECT is_smart FROM playlists WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", playlistId);
        var result = cmd.ExecuteScalar();
        return result is not null && Convert.ToInt32(result) != 0;
    }

    private void TouchPlaylist(long playlistId)
    {
        using var cmd = _db.Connection.CreateCommand();
        cmd.CommandText = "UPDATE playlists SET modified_utc = $now WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", playlistId);
        cmd.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("O"));
        cmd.ExecuteNonQuery();
    }

    private static void AddTrackParams(SqliteCommand cmd, LibraryTrack track)
    {
        cmd.Parameters.AddWithValue("$file_path", track.FilePath);
        cmd.Parameters.AddWithValue("$title", track.Title);
        cmd.Parameters.AddWithValue("$artist", track.Artist);
        cmd.Parameters.AddWithValue("$album", track.Album);
        cmd.Parameters.AddWithValue("$album_artist", track.AlbumArtist);
        cmd.Parameters.AddWithValue("$track_number", (object?)track.TrackNumber ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$year", (object?)track.Year ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$genre", track.Genre);
        cmd.Parameters.AddWithValue("$duration_ms", (long)track.Duration.TotalMilliseconds);
        cmd.Parameters.AddWithValue("$bitrate", track.Bitrate);
        cmd.Parameters.AddWithValue("$format", track.Format);
        cmd.Parameters.AddWithValue("$date_added_utc", track.DateAddedUtc.ToString("O"));
        cmd.Parameters.AddWithValue("$file_modified_utc", track.FileModifiedUtc.ToString("O"));
        cmd.Parameters.AddWithValue("$cover_art", (object?)track.CoverArt ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$play_count", track.PlayCount);
        cmd.Parameters.AddWithValue("$last_played_utc",
            (object?)track.LastPlayedUtc?.ToString("O") ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$rating", Math.Clamp(track.Rating, 0, 5));
        cmd.Parameters.AddWithValue("$cue_start_ms", (object?)track.CueStartMs ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$cue_end_ms", (object?)track.CueEndMs ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$review_status", string.IsNullOrWhiteSpace(track.ReviewStatus) ? "none" : track.ReviewStatus);
        cmd.Parameters.AddWithValue("$comment", track.Comment);
        cmd.Parameters.AddWithValue("$date_released", track.DateReleased);
    }

    internal static string NormalizeAlbumKey(string album)
    {
        var name = string.IsNullOrWhiteSpace(album) ? "Unknown Album" : album.Trim();
        return name.ToLowerInvariant();
    }

    private static List<LibraryTrack> ReadTracks(SqliteCommand cmd)
    {
        var list = new List<LibraryTrack>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            list.Add(ReadTrack(reader));
        return list;
    }

    private static LibraryTrack ReadTrack(SqliteDataReader reader)
    {
        byte[]? cover = reader.IsDBNull(reader.GetOrdinal("cover_art"))
            ? null
            : (byte[])reader["cover_art"];

        DateTime? lastPlayed = null;
        var lastPlayedOrdinal = reader.GetOrdinal("last_played_utc");
        if (!reader.IsDBNull(lastPlayedOrdinal))
            lastPlayed = DateTime.Parse(reader.GetString(lastPlayedOrdinal));

        var playCount = 0;
        var playCountOrdinal = reader.GetOrdinal("play_count");
        if (!reader.IsDBNull(playCountOrdinal))
            playCount = reader.GetInt32(playCountOrdinal);

        var rating = 0;
        var ratingOrdinal = reader.GetOrdinal("rating");
        if (!reader.IsDBNull(ratingOrdinal))
            rating = reader.GetInt32(ratingOrdinal);

        return new LibraryTrack
        {
            Id = reader.GetInt64(reader.GetOrdinal("id")),
            FilePath = reader.GetString(reader.GetOrdinal("file_path")),
            Title = reader.GetString(reader.GetOrdinal("title")),
            Artist = reader.GetString(reader.GetOrdinal("artist")),
            Album = reader.GetString(reader.GetOrdinal("album")),
            AlbumArtist = reader.GetString(reader.GetOrdinal("album_artist")),
            TrackNumber = reader.IsDBNull(reader.GetOrdinal("track_number")) ? null : reader.GetInt32(reader.GetOrdinal("track_number")),
            Year = reader.IsDBNull(reader.GetOrdinal("year")) ? null : reader.GetInt32(reader.GetOrdinal("year")),
            Genre = reader.GetString(reader.GetOrdinal("genre")),
            DateReleased = ReadOptionalString(reader, "date_released"),
            Comment = ReadOptionalString(reader, "comment"),
            Duration = TimeSpan.FromMilliseconds(reader.GetInt64(reader.GetOrdinal("duration_ms"))),
            Bitrate = reader.GetInt32(reader.GetOrdinal("bitrate")),
            Format = reader.GetString(reader.GetOrdinal("format")),
            DateAddedUtc = DateTime.Parse(reader.GetString(reader.GetOrdinal("date_added_utc"))),
            FileModifiedUtc = DateTime.Parse(reader.GetString(reader.GetOrdinal("file_modified_utc"))),
            CoverArt = cover,
            PlayCount = playCount,
            LastPlayedUtc = lastPlayed,
            Rating = rating,
            ReplayGainTrackDb = ReadNullableDouble(reader, "replaygain_track_db"),
            ReplayGainAlbumDb = ReadNullableDouble(reader, "replaygain_album_db"),
            ReplayGainTrackPeak = ReadNullableFloat(reader, "replaygain_track_peak"),
            ReplayGainAlbumPeak = ReadNullableFloat(reader, "replaygain_album_peak"),
            CueStartMs = ReadNullableInt(reader, "cue_start_ms"),
            CueEndMs = ReadNullableInt(reader, "cue_end_ms"),
            ReviewStatus = ReadReviewStatus(reader),
        };
    }

    private static string ReadOptionalString(SqliteDataReader reader, string column)
    {
        try
        {
            var ordinal = reader.GetOrdinal(column);
            return reader.IsDBNull(ordinal) ? "" : reader.GetString(ordinal);
        }
        catch
        {
            return "";
        }
    }

    private static int? ReadNullableInt(SqliteDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : reader.GetInt32(ordinal);
    }

    private static string ReadReviewStatus(SqliteDataReader reader)
    {
        try
        {
            var ordinal = reader.GetOrdinal("review_status");
            return reader.IsDBNull(ordinal) ? "none" : reader.GetString(ordinal);
        }
        catch
        {
            return "none";
        }
    }

    private static double? ReadNullableDouble(SqliteDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : reader.GetDouble(ordinal);
    }

    private static float? ReadNullableFloat(SqliteDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : (float)reader.GetDouble(ordinal);
    }

    private byte[]? LoadPlaylistCoverTiles(long playlistId, bool isSmart, SmartPlaylistRules rules)
    {
        var tiles = new List<byte[]?>();
        if (isSmart)
        {
            using var cmd = _db.Connection.CreateCommand();
            var where = SmartPlaylistEvaluator.BuildWhereClause(rules, cmd);
            cmd.CommandText = $"""
                SELECT cover_art
                FROM (
                    SELECT cover_art,
                           ROW_NUMBER() OVER (
                               PARTITION BY LOWER(TRIM(COALESCE(NULLIF(album, ''), 'Unknown Album')))
                               ORDER BY id) AS rn
                    FROM tracks
                    WHERE {where} AND cover_art IS NOT NULL
                )
                WHERE rn = 1
                LIMIT 4
                """;
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                if (!reader.IsDBNull(0))
                    tiles.Add((byte[])reader.GetValue(0));
            }
        }
        else
        {
            using var cmd = _db.Connection.CreateCommand();
            cmd.CommandText = """
                SELECT cover_art
                FROM (
                    SELECT t.cover_art,
                           ROW_NUMBER() OVER (
                               PARTITION BY LOWER(TRIM(COALESCE(NULLIF(t.album, ''), 'Unknown Album')))
                               ORDER BY pt.position, t.id) AS rn
                    FROM tracks t
                    INNER JOIN playlist_tracks pt ON pt.track_id = t.id
                    WHERE pt.playlist_id = $id AND t.cover_art IS NOT NULL
                )
                WHERE rn = 1
                LIMIT 4
                """;
            cmd.Parameters.AddWithValue("$id", playlistId);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                if (!reader.IsDBNull(0))
                    tiles.Add((byte[])reader.GetValue(0));
            }
        }

        return CoverArtHelper.EncodeMosaicPng(DistinctCoverTiles(tiles), 128);
    }

    private static List<byte[]?> DistinctCoverTiles(IEnumerable<byte[]?> tiles)
    {
        var result = new List<byte[]?>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var tile in tiles)
        {
            if (tile is not { Length: > 0 })
                continue;

            var key = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(tile));
            if (!seen.Add(key))
                continue;

            result.Add(tile);
            if (result.Count >= 4)
                break;
        }

        return result;
    }
}
