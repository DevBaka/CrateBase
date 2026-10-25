namespace Cratebase.Data;

/// <summary>
/// DDL für das MVP-Schema. Enthält inzwischen zusätzlich zu den Phase-1-Tabellen
/// (LibraryRoot, Track, PlaylistNode, PlaylistTrack) die für den Traktor-Import
/// (Phase 2) nötige cue_point-Tabelle. SmartPlaylistRule, Tag/TrackTag,
/// ExternalMapping, AnalysisJob, AiSuggestion, Midi/Slots folgen gemäß
/// ARCHITECTURE.md Abschnitt 2 weiterhin als eigene, additive Migrationen –
/// alle CREATE-Statements sind bewusst "IF NOT EXISTS", sodass diese Datei auf
/// einer bereits befüllten DB gefahrlos erneut läuft (s. DatabaseInitializer).
/// </summary>
internal static class Schema
{
    public const int CurrentVersion = 7;

    /// <summary>
    /// Spalten, die nachträglich zu einer bestehenden `track`-Tabelle dazukamen (Tag-Editor,
    /// s. ARCHITECTURE.md Abschnitt 14). SQLite kennt kein "ALTER TABLE ... ADD COLUMN IF NOT
    /// EXISTS" - DatabaseInitializer prüft deshalb selbst per PRAGMA table_info, ob die Spalte
    /// schon existiert, bevor es das ALTER ausführt (sonst Fehler bei jedem zweiten Start).
    /// Für brandneue DBs stehen dieselben Spalten zusätzlich direkt im CREATE TABLE unten -
    /// die Prüfung sorgt dann einfach dafür, dass hier nichts doppelt versucht wird.
    /// </summary>
    public static readonly (string Column, string Ddl)[] TrackColumnMigrations =
    [
        ("club_ready", "ALTER TABLE track ADD COLUMN club_ready INTEGER NOT NULL DEFAULT 0;"),
        ("extended", "ALTER TABLE track ADD COLUMN extended INTEGER NOT NULL DEFAULT 0;"),
        ("intro", "ALTER TABLE track ADD COLUMN intro INTEGER NOT NULL DEFAULT 0;"),
        ("trash", "ALTER TABLE track ADD COLUMN trash INTEGER NOT NULL DEFAULT 0;"),
        ("comment2", "ALTER TABLE track ADD COLUMN comment2 TEXT NULL;"),
        ("sub_genre", "ALTER TABLE track ADD COLUMN sub_genre TEXT NULL;"),
        ("color_tag", "ALTER TABLE track ADD COLUMN color_tag TEXT NULL;"),
    ];

    /// <summary>Spalten für den NML-Export (s. ARCHITECTURE.md Abschnitt 20) - merkt sich pro
    /// LibraryRoot, wie Traktor denselben Ordner selbst benennt (Laufwerksbuchstabe + Pfad-Segmente
    /// in Traktors "/:"-Notation), automatisch gelernt beim Traktor-Import (s. TraktorPathResolver).
    /// Ohne das könnten neu exportierte Playlisten-Einträge nicht auf einen Pfad zurückgeführt
    /// werden, den Traktor selbst wiedererkennt.</summary>
    public static readonly (string Column, string Ddl)[] LibraryRootColumnMigrations =
    [
        ("traktor_volume", "ALTER TABLE library_root ADD COLUMN traktor_volume TEXT NULL;"),
        ("traktor_dir_prefix", "ALTER TABLE library_root ADD COLUMN traktor_dir_prefix TEXT NULL;"),
    ];

    public const string Sql = """
        PRAGMA foreign_keys = ON;
        CREATE TABLE IF NOT EXISTS workspace_state (key TEXT PRIMARY KEY, json TEXT NOT NULL);

        CREATE TABLE IF NOT EXISTS schema_version (
            version INTEGER NOT NULL
        );

        CREATE TABLE IF NOT EXISTS library_root (
            id              INTEGER PRIMARY KEY AUTOINCREMENT,
            path            TEXT NOT NULL UNIQUE,
            label           TEXT NOT NULL,
            watch_enabled   INTEGER NOT NULL DEFAULT 1,
            last_scanned_at TEXT NULL,
            traktor_volume     TEXT NULL,
            traktor_dir_prefix TEXT NULL
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
            sub_genre        TEXT NULL,
            comment          TEXT NULL,
            comment2         TEXT NULL,
            color_tag        TEXT NULL,

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

        CREATE TABLE IF NOT EXISTS cue_point (
            id              INTEGER PRIMARY KEY AUTOINCREMENT,
            track_id        INTEGER NOT NULL REFERENCES track(id) ON DELETE CASCADE,
            type            TEXT NOT NULL,
            slot_index      INTEGER NULL,
            position_ms     INTEGER NOT NULL,
            loop_length_ms  INTEGER NULL,
            label           TEXT NULL,
            source          TEXT NOT NULL
        );

        CREATE INDEX IF NOT EXISTS ix_cue_point_track ON cue_point(track_id);
        CREATE INDEX IF NOT EXISTS ix_cue_point_track_source ON cue_point(track_id, source);
        """;
}
