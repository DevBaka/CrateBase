namespace Cratebase.Core.Models;

/// <summary>Ein Übergang/Track-Einstieg innerhalb einer laufenden Set-Aufnahme - manuell per Button
/// gesetzt oder automatisch über eine Now-Playing-Quelle erkannt (s. INowPlayingReader). Reines
/// Session-Objekt, nicht in der SQLite-DB persistiert - die Aufnahme ist ein einmaliger Workflow
/// (aufnehmen → Tracklist erzeugen → als Text neben der Audiodatei mitgeben), kein
/// Bibliotheks-Datensatz.</summary>
public sealed class SetTracklistMarker
{
    public TimeSpan Position { get; set; }
    public string PositionText
    {
        get=>HistoryAudioReport.Timestamp(Position.TotalSeconds);
        set
        {
            var parts=value.Split(':');
            if(parts.Length is <2 or >3||parts.Any(p=>!int.TryParse(p,out var n)||n<0)||!int.TryParse(parts[^1],out var seconds)||seconds>59||parts.Length==3&&int.Parse(parts[1])>59)
                throw new ArgumentException("Zeit bitte als MM:SS oder H:MM:SS eingeben.");
            var total=parts.Length==2?(long)int.Parse(parts[0])*60+seconds:(long)int.Parse(parts[0])*3600+int.Parse(parts[1])*60+seconds;
            Position=TimeSpan.FromSeconds(total);
        }
    }
    public string Artist { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
}
