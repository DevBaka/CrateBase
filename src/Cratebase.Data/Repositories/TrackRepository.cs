using Cratebase.Core.Abstractions;
using Cratebase.Core.Models;
using Dapper;
using Microsoft.Data.Sqlite;

namespace Cratebase.Data.Repositories;

public sealed class TrackRepository(SqliteConnectionFactory connectionFactory) : ITrackRepository
{
    public async Task UpdateMetadataAsync(Track track, IReadOnlyCollection<string> fields, CancellationToken ct = default)
    {
        var allowed = new Dictionary<string,(string Column,object? Value)>
        {
            ["title"]=("title",track.Title),["artist"]=("artist",track.Artist),["album"]=("album",track.Album),["albumartist"]=("album_artist",track.AlbumArtist),
            ["genre"]=("genre_raw",track.GenreRaw),["subgenre"]=("sub_genre",track.SubGenre),["comment"]=("comment",track.Comment),["comment2"]=("comment2",track.Comment2),
            ["color"]=("color_tag",track.ColorTag),["key"]=("musical_key",track.MusicalKey),["bpm"]=("bpm",track.Bpm),["year"]=("year",track.Year),["rating"]=("rating",track.Rating),["tracknumber"]=("track_number",track.TrackNumber)
        };
        if(fields.Count==0)return;
        var args=new DynamicParameters();args.Add("id",track.Id);args.Add("modified",SqliteDateTime.ToText(DateTime.UtcNow));
        var updates=new List<string>();
        foreach(var field in fields.Distinct())
        {
            if(!allowed.TryGetValue(field,out var item))throw new ArgumentException("Dieses Metadatenfeld ist nicht beschreibbar.");
            var parameter="v"+updates.Count;args.Add(parameter,item.Value);updates.Add(item.Column+"=@"+parameter);
        }
        updates.Add("date_modified=@modified");
        using var connection=connectionFactory.CreateConnection();
        await connection.ExecuteAsync(new CommandDefinition("UPDATE track SET "+string.Join(",",updates)+" WHERE id=@id",args,cancellationToken:ct));
    }

    internal const string SelectColumns = """
        t.id AS Id, t.club_ready AS ClubReady, t.extended AS Extended, t.intro AS Intro, t.trash AS Trash, t.library_root_id AS LibraryRootId, t.relative_path AS RelativePath,
        t.file_size AS FileSizeBytes, t.file_mtime AS FileModifiedAtUtc, t.format AS Format,
        t.duration_ms AS DurationMs, t.bitrate_kbps AS BitrateKbps, t.sample_rate_hz AS SampleRateHz, t.channels AS Channels,
        t.title AS Title, t.artist AS Artist, t.album AS Album, t.album_artist AS AlbumArtist,
        t.track_number AS TrackNumber, t.year AS Year, t.genre_raw AS GenreRaw, t.sub_genre AS SubGenre,
        t.comment AS Comment, t.comment2 AS Comment2, t.color_tag AS ColorTag,
        t.bpm AS Bpm, t.musical_key AS MusicalKey, t.rating AS Rating, t.play_count AS PlayCount, t.last_played_at AS LastPlayedAtUtc,
        t.date_added AS DateAddedUtc, t.date_modified AS DateModifiedUtc, t.missing_since AS MissingSinceUtc
        """;

    public async Task<Track?> GetByIdAsync(long id, CancellationToken ct = default)
    {
        using var connection = connectionFactory.CreateConnection();
        return await connection.QuerySingleOrDefaultAsync<Track>(new CommandDefinition(
            $"SELECT {SelectColumns} FROM track t WHERE t.id = @id;", new { id }, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<Track>> GetByIdsAsync(IReadOnlyCollection<long> ids, CancellationToken ct = default)
    {
        if (ids.Count == 0)
        {
            return [];
        }

        using var connection = connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<Track>(new CommandDefinition(
            $"SELECT {SelectColumns} FROM track t WHERE t.id IN @ids;", new { ids }, cancellationToken: ct));
        return rows.ToList();
    }

    public async Task<IReadOnlyList<string>> GetDistinctTextValuesAsync(TrackTextField field, CancellationToken ct = default)
    {
        var column = field switch
        {
            TrackTextField.GenreRaw => "genre_raw",
            TrackTextField.SubGenre => "sub_genre",
            TrackTextField.Comment => "comment",
            TrackTextField.Comment2 => "comment2",
            TrackTextField.ColorTag => "color_tag",
            _ => throw new ArgumentOutOfRangeException(nameof(field)),
        };

        using var connection = connectionFactory.CreateConnection();
        // column kommt ausschließlich aus dem obigen, festen switch (nie aus Nutzereingabe) -
        // Interpolation hier ist kein Injection-Risiko, s. TrackTextField-Doku im Interface.
        var rows = await connection.QueryAsync<string>(new CommandDefinition(
            $"""
            SELECT DISTINCT {column} FROM track
            WHERE {column} IS NOT NULL AND TRIM({column}) != ''
            ORDER BY {column} COLLATE NOCASE;
            """,
            cancellationToken: ct));
        return rows.ToList();
    }

    /// <summary>
    /// Insert-or-Update über den UNIQUE(library_root_id, relative_path)-Index. Rating, PlayCount,
    /// LastPlayedAtUtc, DateAddedUtc UND (seit dem Tag-Editor) auch GenreRaw/Comment werden bei
    /// einem Konflikt (Track war schon bekannt) bewusst NICHT überschrieben – sobald ein Track
    /// einmal in der DB steht, gewinnt eine manuelle Bearbeitung dauerhaft gegen einen späteren
    /// Rescan, selbst wenn sich das Tag in der Datei extern geändert hat. SubGenre/Comment2/
    /// ColorTag haben ohnehin keine Entsprechung in Datei-Tags und tauchen deshalb gar nicht erst
    /// im ON CONFLICT-Zweig auf - die kommen ausschließlich über <see cref="UpdateAsync"/> (Tag-
    /// Editor) rein. Für die Erstanlage (kein Konflikt) fließen GenreRaw/Comment weiterhin ganz
    /// normal aus der gescannten Datei ein.
    /// </summary>
    public async Task<long> UpsertAsync(Track track, CancellationToken ct = default)
    {
        using var connection = connectionFactory.CreateConnection();
        return await UpsertOnConnectionAsync(connection,null,track,ct);
    }

    public async Task<IReadOnlyList<long>> UpsertBatchAsync(IReadOnlyList<Track> tracks,CancellationToken ct=default)
    {
        using var connection=connectionFactory.CreateConnection();
        using var transaction=connection.BeginTransaction();
        var ids=new List<long>(tracks.Count);
        foreach(var track in tracks) ids.Add(await UpsertOnConnectionAsync(connection,transaction,track,ct));
        transaction.Commit(); return ids;
    }

    private static async Task<long> UpsertOnConnectionAsync(SqliteConnection connection,SqliteTransaction? transaction,Track track,CancellationToken ct)
    {

        var parameters = new
        {
            track.LibraryRootId,
            track.RelativePath,
            track.FileSizeBytes,
            FileModifiedAtUtc = SqliteDateTime.ToText(track.FileModifiedAtUtc),
            track.Format,
            track.DurationMs,
            track.BitrateKbps,
            track.SampleRateHz,
            track.Channels,
            track.Title,
            track.Artist,
            track.Album,
            track.AlbumArtist,
            track.TrackNumber,
            track.Year,
            track.GenreRaw,
            track.Comment,
            track.Bpm,
            track.MusicalKey,
            DateAddedUtc = SqliteDateTime.ToText(track.DateAddedUtc),
            DateModifiedUtc = SqliteDateTime.ToText(track.DateModifiedUtc),
        };

        return await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            """
            INSERT INTO track (
                library_root_id, relative_path, file_size, file_mtime, format,
                duration_ms, bitrate_kbps, sample_rate_hz, channels,
                title, artist, album, album_artist, track_number, year, genre_raw, comment,
                bpm, musical_key, rating, play_count, last_played_at,
                date_added, date_modified, missing_since
            ) VALUES (
                @LibraryRootId, @RelativePath, @FileSizeBytes, @FileModifiedAtUtc, @Format,
                @DurationMs, @BitrateKbps, @SampleRateHz, @Channels,
                @Title, @Artist, @Album, @AlbumArtist, @TrackNumber, @Year, @GenreRaw, @Comment,
                @Bpm, @MusicalKey, 0, 0, NULL,
                @DateAddedUtc, @DateModifiedUtc, NULL
            )
            ON CONFLICT (library_root_id, relative_path) DO UPDATE SET
                file_size = excluded.file_size,
                file_mtime = excluded.file_mtime,
                format = excluded.format,
                duration_ms = excluded.duration_ms,
                bitrate_kbps = excluded.bitrate_kbps,
                sample_rate_hz = excluded.sample_rate_hz,
                channels = excluded.channels,
                title = COALESCE(track.title, excluded.title),
                artist = COALESCE(track.artist, excluded.artist),
                album = COALESCE(track.album, excluded.album),
                album_artist = excluded.album_artist,
                track_number = excluded.track_number,
                year = excluded.year,
                bpm = COALESCE(track.bpm, excluded.bpm),
                musical_key = COALESCE(track.musical_key, excluded.musical_key),
                date_modified = excluded.date_modified,
                missing_since = NULL
            RETURNING id;
            """,
            parameters,
            transaction: transaction,
            cancellationToken: ct));
    }

    public async Task<Track?> FindByPathAsync(long libraryRootId, string relativePath, CancellationToken ct = default)
    {
        using var connection = connectionFactory.CreateConnection();
        return await connection.QuerySingleOrDefaultAsync<Track>(new CommandDefinition(
            $"SELECT {SelectColumns} FROM track t WHERE t.library_root_id = @libraryRootId AND t.relative_path = @relativePath;",
            new { libraryRootId, relativePath },
            cancellationToken: ct));
    }

    public async Task UpdateAsync(Track track, CancellationToken ct = default)
    {
        using var connection = connectionFactory.CreateConnection();
        await UpdateOnConnectionAsync(connection,null,track,ct);
    }

    public async Task UpdateBatchAsync(IReadOnlyList<Track> tracks,CancellationToken ct=default)
    {
        using var connection=connectionFactory.CreateConnection();
        using var transaction=connection.BeginTransaction();
        foreach(var track in tracks) await UpdateOnConnectionAsync(connection,transaction,track,ct);
        transaction.Commit();
    }

    private static async Task UpdateOnConnectionAsync(SqliteConnection connection,SqliteTransaction? transaction,Track track,CancellationToken ct)
    {

        var parameters = new
        {
            track.Id,
            track.Title,
            track.Artist,
            track.Album,
            track.AlbumArtist,
            track.TrackNumber,
            track.Year,
            track.GenreRaw,
            track.SubGenre,
            track.Comment,
            track.Comment2,
            track.ColorTag,
            track.Bpm,
            track.MusicalKey,
            track.Rating,
            track.PlayCount,
            track.DurationMs, track.BitrateKbps, track.SampleRateHz, track.Channels, track.Format,
            MissingSinceUtc = SqliteDateTime.ToText(track.MissingSinceUtc),
            LastPlayedAtUtc = SqliteDateTime.ToText(track.LastPlayedAtUtc),
            DateModifiedUtc = SqliteDateTime.ToText(track.DateModifiedUtc),
        };

        await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE track SET
                title = @Title, artist = @Artist, album = @Album, album_artist = @AlbumArtist,
                track_number = @TrackNumber, year = @Year, genre_raw = @GenreRaw, sub_genre = @SubGenre,
                comment = @Comment, comment2 = @Comment2, color_tag = @ColorTag,
                bpm = @Bpm, musical_key = @MusicalKey, rating = @Rating, play_count = @PlayCount,
                last_played_at = @LastPlayedAtUtc, date_modified = @DateModifiedUtc, missing_since = @MissingSinceUtc,
                duration_ms = CASE WHEN @DurationMs > 0 THEN @DurationMs ELSE duration_ms END,
                bitrate_kbps = COALESCE(@BitrateKbps, bitrate_kbps), sample_rate_hz = COALESCE(@SampleRateHz, sample_rate_hz),
                channels = COALESCE(@Channels, channels), format = COALESCE(NULLIF(@Format,''), format)
            WHERE id = @Id;
            """,
            parameters,
            transaction: transaction,
            cancellationToken: ct));
    }

    public async Task SetFileAvailabilityAsync(long id,bool reachable,CancellationToken ct=default)
    {
        using var connection=connectionFactory.CreateConnection();
        await connection.ExecuteAsync(new CommandDefinition("UPDATE track SET missing_since=CASE WHEN @reachable THEN NULL ELSE COALESCE(missing_since,@now) END WHERE id=@id",new {id,reachable,now=SqliteDateTime.ToText(DateTime.UtcNow)},cancellationToken:ct));
    }

    public async Task<IReadOnlyDictionary<string, TrackFingerprint>> GetFingerprintsByPathAsync(long libraryRootId, CancellationToken ct = default)
    {
        using var connection = connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<FingerprintRow>(new CommandDefinition(
            """
            SELECT relative_path AS RelativePath, id AS TrackId, file_size AS FileSizeBytes, file_mtime AS FileModifiedAtUtc
            FROM track
            WHERE library_root_id = @libraryRootId AND missing_since IS NULL;
            """,
            new { libraryRootId },
            cancellationToken: ct));

        return rows.ToDictionary(
            row => row.RelativePath,
            row => new TrackFingerprint(row.TrackId, row.FileSizeBytes, row.FileModifiedAtUtc));
    }

    /// <summary>
    /// Markiert Tracks als "missing" statt sie zu löschen, wenn ihr Pfad im aktuellen Scan
    /// nicht mehr auftaucht (externe Festplatte kurz nicht gemountet, Datei umbenannt o.ä.
    /// sollen keine Playlist-Mitgliedschaften/Ratings zerstören). Der Abgleich läuft über
    /// eine Temp-Table statt eines riesigen IN(...)-Parameterlisten, damit auch ~100k Pfade
    /// nicht am SQLite-Parameterlimit scheitern.
    /// </summary>
    public async Task<int> MarkMissingExceptAsync(long libraryRootId, IReadOnlyCollection<string> presentRelativePaths, DateTime scanTimeUtc, CancellationToken ct = default)
    {
        using var connection = connectionFactory.CreateConnection();
        using var transaction = connection.BeginTransaction();

        using (var createTemp = connection.CreateCommand())
        {
            createTemp.Transaction = transaction;
            createTemp.CommandText = "CREATE TEMP TABLE present_path (path TEXT PRIMARY KEY);";
            createTemp.ExecuteNonQuery();
        }

        if (presentRelativePaths.Count > 0)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                "INSERT OR IGNORE INTO present_path (path) VALUES (@path);",
                presentRelativePaths.Select(path => new { path }),
                transaction,
                cancellationToken: ct));
        }

        var affected = await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE track
            SET missing_since = @scanTimeUtc
            WHERE library_root_id = @libraryRootId
              AND missing_since IS NULL
              AND relative_path NOT IN (SELECT path FROM present_path);
            """,
            new { libraryRootId, scanTimeUtc = SqliteDateTime.ToText(scanTimeUtc) },
            transaction,
            cancellationToken: ct));

        using (var dropTemp = connection.CreateCommand())
        {
            dropTemp.Transaction = transaction;
            dropTemp.CommandText = "DROP TABLE present_path;";
            dropTemp.ExecuteNonQuery();
        }

        transaction.Commit();
        return affected;
    }

    public async Task<IReadOnlyList<Track>> SearchAsync(string? searchText, IReadOnlyCollection<long>? playlistNodeIds, bool includeMissing, int limit, int offset, CancellationToken ct = default)
    {
        using var connection = connectionFactory.CreateConnection();
        var (whereSql, parameters) = BuildFilter(searchText, playlistNodeIds, includeMissing);
        var hasPlaylistFilter = playlistNodeIds is { Count: > 0 };
        var joinSql = hasPlaylistFilter
            ? "JOIN playlist_track pt ON pt.track_id = t.id AND pt.playlist_node_id IN @playlistNodeIds"
            : "";
        // Bei mehreren Playlisten (Ordner-Aggregation) kann ein Track über mehrere davon mehrfach
        // treffen - DISTINCT dedupliziert. Bei genau einer Playlist bleibt die manuelle
        // Track-Reihenfolge (pt.position) erhalten, die DISTINCT+ORDER-BY-Kombination unnötig macht.
        var isAggregated = playlistNodeIds is { Count: > 1 };
        var selectClause = isAggregated ? $"DISTINCT {SelectColumns}" : SelectColumns;
        var orderSql = playlistNodeIds is { Count: 1 }
            ? "ORDER BY pt.position"
            : "ORDER BY t.artist, t.album, t.track_number, t.title";

        parameters.Add("limit", limit);
        parameters.Add("offset", offset);

        var sql = $"""
            SELECT {selectClause}
            FROM track t
            {joinSql}
            {whereSql}
            {orderSql}
            LIMIT @limit OFFSET @offset;
            """;

        var rows = await connection.QueryAsync<Track>(new CommandDefinition(sql, parameters, cancellationToken: ct));
        return rows.ToList();
    }

    public async Task<int> CountAsync(string? searchText, IReadOnlyCollection<long>? playlistNodeIds, bool includeMissing, CancellationToken ct = default)
    {
        using var connection = connectionFactory.CreateConnection();
        var (whereSql, parameters) = BuildFilter(searchText, playlistNodeIds, includeMissing);
        var hasPlaylistFilter = playlistNodeIds is { Count: > 0 };
        var joinSql = hasPlaylistFilter
            ? "JOIN playlist_track pt ON pt.track_id = t.id AND pt.playlist_node_id IN @playlistNodeIds"
            : "";
        var countExpr = playlistNodeIds is { Count: > 1 } ? "COUNT(DISTINCT t.id)" : "COUNT(*)";

        var sql = $"SELECT {countExpr} FROM track t {joinSql} {whereSql};";
        return await connection.ExecuteScalarAsync<int>(new CommandDefinition(sql, parameters, cancellationToken: ct));
    }

    private static (string WhereSql, DynamicParameters Parameters) BuildFilter(string? searchText, IReadOnlyCollection<long>? playlistNodeIds, bool includeMissing)
    {
        var conditions = new List<string>();
        var parameters = new DynamicParameters();

        if (!includeMissing)
        {
            conditions.Add("t.missing_since IS NULL");
        }

        if (!string.IsNullOrWhiteSpace(searchText))
        {
            conditions.Add("(t.title LIKE @searchPattern OR t.artist LIKE @searchPattern OR t.album LIKE @searchPattern)");
            parameters.Add("searchPattern", $"%{searchText.Trim()}%");
        }

        if (playlistNodeIds is { Count: > 0 })
        {
            parameters.Add("playlistNodeIds", playlistNodeIds);
        }

        var whereSql = conditions.Count > 0 ? "WHERE " + string.Join(" AND ", conditions) : "";
        return (whereSql, parameters);
    }

    /// <summary>Plain Klasse statt Record – s. Begründung bei PlaylistRepository.NodeRow.</summary>
    private sealed class FingerprintRow
    {
        public string RelativePath { get; set; } = string.Empty;
        public long TrackId { get; set; }
        public long FileSizeBytes { get; set; }
        public DateTime FileModifiedAtUtc { get; set; }
    }
}
