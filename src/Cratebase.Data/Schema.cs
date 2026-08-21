namespace Cratebase.Data;

/// <summary>
/// DDL für das MVP-Schema. Nur die Tabellen, die Scanner/Player/Playlist-Baum
/// tatsächlich benutzen (LibraryRoot, Track, PlaylistNode, PlaylistTrack) –
/// CuePoint, SmartPlaylistRule, Tag/TrackTag, ExternalMapping, AnalysisJob,
/// AiSuggestion, Midi/Slots folgen gemäß ARCHITECTURE.md Abschnitt 2 in
/// späteren Phasen als eigene Migration, statt hier schon spekulativ
/// mitzuschleppen. <see cref="schema_version"/> ist der Ansatzpunkt dafür.
/// </summary>
internal static class Schema
{
    public const int CurrentVersion = 1;

    public const string Sql = """
        PRAGMA foreign_keys = ON;

        CREATE TABLE IF NOT EXISTS schema_version (
            version INTEGER NOT NULL
        );

        CREATE TABLE IF NOT EXISTS library_root (
            id              INTEGER PRIMARY KEY AUTOINCREMENT,
            path            TEXT NOT NULL UNIQUE,
            label           TEXT NOT NULL,
            watch_enabled   INTEGER NOT NULL DEFAULT 1,
            last_scanned_at TEXT NULL
        );

        CREATE TABLE IF NOT EXISTS track (
            id               INTEGER PRIMARY KEY AUTOINCREMENT,
            library_root_id  INTEGER NOT NULL REFERENCES library_root(id) ON DELETE CASCADE,
            relative_path    TEXT NOT NULL,
            file_size        INTEGER NOT NULL,
            file_mtime       TEXT NOT NULL,
            format           TEXT NOT NULL,
            duration_ms      INTEGER NOT NULL DEFAULT 0,
            bitrate_kbps     INTEGER NULL,
            sample_rate_hz   INTEGER NULL,
            channels         INTEGER NULL,

            title            TEXT NULL,
            artist           TEXT NULL,
            album            TEXT NULL,
            album_artist     TEXT NULL,
            track_number     INTEGER NULL,
            year             INTEGER NULL,
            genre_raw        TEXT NULL,
            comment          TEXT NULL,

            bpm              REAL NULL,
            musical_key      TEXT NULL,
            rating           INTEGER NOT NULL DEFAULT 0,
            play_count       INTEGER NOT NULL DEFAULT 0,
            last_played_at   TEXT NULL,

            date_added       TEXT NOT NULL,
            date_modified    TEXT NOT NULL,
            missing_since    TEXT NULL,

            UNIQUE (library_root_id, relative_path)
        );

        CREATE INDEX IF NOT EXISTS ix_track_library_root ON track(library_root_id);
        CREATE INDEX IF NOT EXISTS ix_track_artist_title ON track(artist, title);
        CREATE INDEX IF NOT EXISTS ix_track_genre ON track(genre_raw);
        CREATE INDEX IF NOT EXISTS ix_track_bpm ON track(bpm);
        CREATE INDEX IF NOT EXISTS ix_track_missing ON track(missing_since);

        CREATE TABLE IF NOT EXISTS playlist_node (
            id          INTEGER PRIMARY KEY AUTOINCREMENT,
            parent_id   INTEGER NULL REFERENCES playlist_node(id) ON DELETE CASCADE,
            node_type   TEXT NOT NULL,
            name        TEXT NOT NULL,
            sort_order  INTEGER NOT NULL DEFAULT 0,
            created_at  TEXT NOT NULL,
            updated_at  TEXT NOT NULL
        );

        CREATE INDEX IF NOT EXISTS ix_playlist_node_parent ON playlist_node(parent_id);

        CREATE TABLE IF NOT EXISTS playlist_track (
            playlist_node_id  INTEGER NOT NULL REFERENCES playlist_node(id) ON DELETE CASCADE,
            track_id          INTEGER NOT NULL REFERENCES track(id) ON DELETE CASCADE,
            position          INTEGER NOT NULL,
            date_added        TEXT NOT NULL,
            PRIMARY KEY (playlist_node_id, track_id)
        );

        CREATE INDEX IF NOT EXISTS ix_playlist_track_playlist ON playlist_track(playlist_node_id);
        CREATE INDEX IF NOT EXISTS ix_playlist_track_track ON playlist_track(track_id);
        """;
}
