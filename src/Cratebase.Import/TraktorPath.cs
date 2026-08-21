namespace Cratebase.Import;

/// <summary>
/// Traktor kodiert Pfade mit "/:" als Trennzeichen zwischen Laufwerk/Ordnern/Datei, sowohl
/// in &lt;LOCATION DIR="..." FILE="..." VOLUME="..."&gt; (Collection-Einträge) als auch im
/// kombinierten &lt;PRIMARYKEY KEY="M:/:Musik/:.../:Track.flac"&gt; (Playlist-Einträge).
/// Diese Klasse normalisiert beide Formen auf dieselbe Struktur, damit der Rest des Importers
/// nur eine Pfad-Repräsentation kennt.
/// </summary>
public sealed record TraktorPath(string Volume, IReadOnlyList<string> DirSegments, string FileName)
{
    /// <summary>Aus getrennten LOCATION-Attributen (DIR/FILE/VOLUME).</summary>
    public static TraktorPath FromLocation(string volume, string dir, string file)
    {
        var segments = SplitSegments(dir);
        return new TraktorPath(volume, segments, file);
    }

    /// <summary>Aus einem kombinierten PRIMARYKEY-KEY-String ("M:/:Ordner/:Datei.mp3").</summary>
    public static TraktorPath? FromCombinedKey(string key)
    {
        var parts = key.Split("/:");
        if (parts.Length < 2)
        {
            return null;
        }

        var volume = parts[0];
        var fileName = parts[^1];
        var dirSegments = parts[1..^1];
        return new TraktorPath(volume, dirSegments, fileName);
    }

    private static IReadOnlyList<string> SplitSegments(string dir) =>
        dir.Split("/:", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
