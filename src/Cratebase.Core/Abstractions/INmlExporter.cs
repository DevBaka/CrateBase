namespace Cratebase.Core.Abstractions;

public sealed record NmlExportSummary(int PlaylistsExported, int TracksExported, int TracksSkippedNoTraktorPath);

/// <summary>
/// Schreibt den kompletten Cratebase-Playlist-Baum als eigenständige, gültige Traktor-NML-Datei -
/// zum manuellen Import in Traktor über "Collection importieren" (nicht: automatischer/live Sync,
/// und niemals ein Schreiben in Traktors eigene collection.nml, s. ARCHITECTURE.md Abschnitt 20 für
/// die Begründung). Tracks, deren LibraryRoot noch keine gelernte Traktor-Zuordnung hat (s.
/// LibraryRoot.TraktorVolume-Doku - typischerweise: dieser Root wurde noch nie erfolgreich mit
/// einem Traktor-Import abgeglichen), werden übersprungen statt einen geratenen Pfad zu erfinden.
/// </summary>
public interface INmlExporter
{
    Task<NmlExportSummary> ExportAsync(string outputPath, CancellationToken ct = default);
}
