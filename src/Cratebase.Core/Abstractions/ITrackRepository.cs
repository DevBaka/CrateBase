using Cratebase.Core.Models;

namespace Cratebase.Core.Abstractions;

/// <summary>Bekannter Datei-Fingerabdruck für die inkrementelle Scan-Erkennung (s. ILibraryScanner).</summary>
public readonly record struct TrackFingerprint(long TrackId, long FileSizeBytes, DateTime FileModifiedAtUtc);

/// <summary>
/// Einziger Zugriffspunkt auf Track-Daten. Nur die Implementierung in Cratebase.Data
/// spricht SQL – alle anderen Module (Scanning, UI) sehen nur dieses Interface.
/// Suche/Filter laufen als SQL-Query mit LIMIT/OFFSET, nicht als In-Memory-Scan über
/// geladene Objekte (harter Skalierungs-Constraint aus ARCHITECTURE.md).
/// </summary>
public interface ITrackRepository
{
    Task<Track?> GetByIdAsync(long id, CancellationToken ct = default);

    /// <summary>Fügt einen Track ein oder aktualisiert ihn (Match über LibraryRootId+RelativePath). Gibt die Id zurück.</summary>
    Task<long> UpsertAsync(Track track, CancellationToken ct = default);

    /// <summary>Fingerabdrücke aller bekannten (nicht als "missing" markierten) Tracks eines Roots, für den inkrementellen Scan-Abgleich.</summary>
    Task<IReadOnlyDictionary<string, TrackFingerprint>> GetFingerprintsByPathAsync(long libraryRootId, CancellationToken ct = default);

    /// <summary>Markiert alle Tracks eines Roots als fehlend, deren RelativePath nicht in <paramref name="presentRelativePaths"/> enthalten ist.</summary>
    Task<int> MarkMissingExceptAsync(long libraryRootId, IReadOnlyCollection<string> presentRelativePaths, DateTime scanTimeUtc, CancellationToken ct = default);

    /// <summary>
    /// Suche/Filter über die Bibliothek (oder eine einzelne Playlist, wenn <paramref name="playlistNodeId"/> gesetzt ist).
    /// <paramref name="searchText"/> filtert auf Titel/Artist/Album (SQL LIKE über indizierte Spalten – FTS5 ist eine spätere Optimierung, keine Verhaltensänderung).
    /// </summary>
    Task<IReadOnlyList<Track>> SearchAsync(string? searchText, long? playlistNodeId, bool includeMissing, int limit, int offset, CancellationToken ct = default);

    Task<int> CountAsync(string? searchText, long? playlistNodeId, bool includeMissing, CancellationToken ct = default);
}
