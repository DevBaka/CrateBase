namespace Cratebase.Core.Models;

/// <summary>Ein Übergang/Track-Einstieg innerhalb einer laufenden Set-Aufnahme - manuell per Button
/// gesetzt oder automatisch über eine Now-Playing-Quelle erkannt (s. INowPlayingReader). Reines
/// Session-Objekt, nicht in der SQLite-DB persistiert - die Aufnahme ist ein einmaliger Workflow
/// (aufnehmen → Tracklist erzeugen → als Text neben der Audiodatei mitgeben), kein
/// Bibliotheks-Datensatz.</summary>
public sealed class SetTracklistMarker
{
    public TimeSpan Position { get; set; }
    public string Artist { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
}
