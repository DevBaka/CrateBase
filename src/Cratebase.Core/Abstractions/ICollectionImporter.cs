namespace Cratebase.Core.Abstractions;

public sealed record ImportProgress(int EntriesSeen, int TracksMatched, int TracksUnmatched, int PlaylistsCreated, int CuePointsImported, string? CurrentItem);

public sealed record ImportSummary(int TotalEntries, int TracksMatched, int TracksUnmatched, int PlaylistsCreated, int PlaylistTracksLinked, int CuePointsImported);

/// <summary>
/// Ein DJ-Software-Collection-Reader (Traktor zuerst, s. ARCHITECTURE.md Phase 2 — "Reader
/// zuerst, Writer danach"). Bewusst ein einziges, werkzeugunabhängiges Interface: die UI
/// kennt nur "Quelle wählen, importieren", nicht welches Tool dahintersteckt. Matcht Einträge
/// gegen bereits über den eigenen Scanner bekannte Tracks (s. Cratebase.Import) — importiert
/// also an, statt eine eigene, parallele Bibliothek aufzubauen.
/// </summary>
public interface ICollectionImporter
{
    string DisplayName { get; }

    Task<ImportSummary> ImportAsync(string sourceFilePath, IProgress<ImportProgress>? progress, CancellationToken ct = default);
}
