namespace Cratebase.Core.Abstractions;

/// <summary>Woher die automatische Übergangserkennung den "jetzt läuft"-Text liest (s.
/// INowPlayingReader-Doku) - Nutzer-Feedback nennt beide Wege ("Traktor History file oder
/// alternativ Now Playing Tracklist file").</summary>
public enum NowPlayingSourceType
{
    /// <summary>Eine einfache Textdatei, deren gesamter Inhalt bei jedem Trackwechsel überschrieben
    /// wird (Traktors eingebaute "Now Playing"-Textausgabe, z.B. für Stream-Overlays gedacht) -
    /// primärer, zuverlässig live aktualisierter Weg.</summary>
    PlainTextFile,

    /// <summary>Traktors History-Playlist innerhalb einer NML-Datei - bestenmöglich nachgebaut ohne
    /// eine echte Traktor-Installation zum Gegenprüfen verfügbar zu haben (s. ARCHITECTURE.md für
    /// die Einschränkung). Traktor schreibt seine collection.nml typischerweise nicht laufend
    /// während des Betriebs, sondern beim Beenden/explizit - für eine LIVE-Erkennung während der
    /// Aufnahme deshalb weniger zuverlässig als die Textdatei-Variante.</summary>
    TraktorHistoryNml,
}

/// <summary>Liest den aktuell erkannten "Artist - Titel"-Text aus einer Now-Playing-Quelle - wird von
/// der Set-Recorder-Aufnahme regelmäßig abgefragt (Polling statt FileSystemWatcher, s. MainViewModel-
/// Doku: robuster gegenüber Netzlaufwerken/Dateisystem-Eigenheiten), ein Wechsel des zurückgegebenen
/// Texts löst einen automatischen Übergangs-Marker aus.</summary>
public interface INowPlayingReader
{
    /// <summary>Liefert null bei fehlender/leerer/kaputter Quelle - nie werfen (die Datei kann
    /// während des Schreibens durch die Fremdanwendung kurz inkonsistent sein; beim nächsten Poll
    /// einfach erneut versuchen, nie deswegen die laufende Aufnahme abbrechen).</summary>
    Task<string?> TryReadCurrentAsync(string sourcePath, NowPlayingSourceType sourceType, CancellationToken ct = default);
}
