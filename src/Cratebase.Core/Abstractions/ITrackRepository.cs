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

    /// <summary>Für den Datei-Browser: liefert den Track zu einem bekannten (library_root_id, relative_path)-Paar, oder null, falls die Datei noch nicht gescannt ist.</summary>
    Task<Track?> FindByPathAsync(long libraryRootId, string relativePath, CancellationToken ct = default);

    /// <summary>Fügt einen Track ein oder aktualisiert ihn (Match über LibraryRootId+RelativePath). Gibt die Id zurück.
    /// Rating/PlayCount/LastPlayedAtUtc/DateAddedUtc werden bei einem Konflikt NICHT überschrieben (s. Implementierung) -
    /// für Updates, die genau diese Felder setzen sollen (z.B. DJ-Software-Import), <see cref="UpdateAsync"/> verwenden.</summary>
    Task<long> UpsertAsync(Track track, CancellationToken ct = default);

    /// <summary>Voller Update-by-Id, überschreibt alle unten stehenden Felder inkl. Rating/PlayCount/LastPlayedAtUtc.
    /// Für Datei-Metadaten-Rescans <see cref="UpsertAsync"/> verwenden (schützt Nutzerdaten) - dies hier ist für
    /// Fälle gedacht, die genau diese Felder bewusst setzen wollen (Tag-Editor, DJ-Software-Import).</summary>
    Task UpdateAsync(Track track, CancellationToken ct = default);

    /// <summary>Fingerabdrücke aller bekannten (nicht als "missing" markierten) Tracks eines Roots, für den inkrementellen Scan-Abgleich.</summary>
    Task<IReadOnlyDictionary<string, TrackFingerprint>> GetFingerprintsByPathAsync(long libraryRootId, CancellationToken ct = default);

    /// <summary>Markiert alle Tracks eines Roots als fehlend, deren RelativePath nicht in <paramref name="presentRelativePaths"/> enthalten ist.</summary>
    Task<int> MarkMissingExceptAsync(long libraryRootId, IReadOnlyCollection<string> presentRelativePaths, DateTime scanTimeUtc, CancellationToken ct = default);

    /// <summary>
    /// Suche/Filter über die Bibliothek, oder über eine Menge von Playlisten, wenn <paramref name="playlistNodeIds"/>
    /// gesetzt ist (mehrere IDs = Ordner-Aggregation über alle darin verschachtelten Playlisten, s. MainViewModel).
    /// Bei genau einer Id wird nach der manuellen Playlist-Reihenfolge sortiert, bei mehreren (kein einzelnes
    /// "richtiges" Ordering über verschiedene Playlisten hinweg) nach Artist/Album/Titel wie in der Bibliotheksansicht.
    /// <paramref name="searchText"/> filtert auf Titel/Artist/Album (SQL LIKE über indizierte Spalten – FTS5 ist eine spätere Optimierung, keine Verhaltensänderung).
    /// </summary>
    Task<IReadOnlyList<Track>> SearchAsync(string? searchText, IReadOnlyCollection<long>? playlistNodeIds, bool includeMissing, int limit, int offset, CancellationToken ct = default);

    Task<int> CountAsync(string? searchText, IReadOnlyCollection<long>? playlistNodeIds, bool includeMissing, CancellationToken ct = default);
}
