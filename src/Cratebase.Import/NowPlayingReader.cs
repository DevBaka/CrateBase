using Cratebase.Core.Abstractions;

namespace Cratebase.Import;

/// <summary>Implementiert INowPlayingReader für beide vom Nutzer genannten Quellen (s. dortige Doku).
/// Wird während einer laufenden Set-Aufnahme im Sekundentakt abgefragt (Polling statt
/// FileSystemWatcher - robuster, s. MainViewModel: FileSystemWatcher-Ereignisse sind auf manchen
/// Dateisystemen/über Samba-Freigaben unzuverlässig, und Traktor bzw. die Now-Playing-Textausgabe
/// laufen typischerweise auf demselben Rechner, ein Poll alle 1-2s ist für Sekunden-genaue
/// Timestamps mehr als ausreichend).</summary>
public sealed class NowPlayingReader : INowPlayingReader
{
    public async Task<string?> TryReadCurrentAsync(string sourcePath, NowPlayingSourceType sourceType, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
        {
            return null;
        }

        try
        {
            return sourceType switch
            {
                NowPlayingSourceType.PlainTextFile => await ReadPlainTextAsync(sourcePath, ct),
                NowPlayingSourceType.TraktorHistoryNml => ReadFromHistoryNml(sourcePath),
                _ => null,
            };
        }
        catch (Exception)
        {
            // Die Quelldatei kann während des Schreibens durch die Fremdanwendung (Traktor o.ä.)
            // kurz inkonsistent/gesperrt sein - beim nächsten Poll einfach erneut versuchen, s.
            // INowPlayingReader-Doku. Nie die laufende Aufnahme deswegen abbrechen.
            return null;
        }
    }

    /// <summary>FileShare.ReadWrite, da die Quelle (Traktor o.ä.) die Datei parallel offen halten
    /// kann, während wir lesen - ein exklusiver Lesezugriff würde sonst regelmäßig fehlschlagen.</summary>
    private static async Task<string?> ReadPlainTextAsync(string path, CancellationToken ct)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);
        var text = await reader.ReadToEndAsync(ct);
        var trimmed = text.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    /// <summary>Bestenmöglich nachgebaut ohne eine echte Traktor-Installation zum Gegenprüfen - s.
    /// NowPlayingSourceType.TraktorHistoryNml-Doku für die Einschränkung (Traktor schreibt seine
    /// NML typischerweise nicht laufend, sondern beim Beenden/explizit exportiert - für eine LIVE-
    /// Erkennung während der Aufnahme deshalb nur bedingt geeignet, primär für den nachträglichen
    /// Abgleich gedacht). Sucht die Playlist, deren Name "History" enthält (bei mehreren datierten
    /// History-Playlisten die zuletzt einsortierte, üblicherweise die aktuellste), nimmt deren
    /// letzten Eintrag und löst ihn gegen die COLLECTION auf (PRIMARYKEY trägt nur den Pfad, Titel/
    /// Artist stehen nur im COLLECTION-ENTRY).</summary>
    private static string? ReadFromHistoryNml(string path)
    {
        var reader = new TraktorNmlReader();
        var tree = reader.ReadPlaylistTree(path);
        var historyPlaylist = tree is null ? null : FindNewestHistoryPlaylist(tree);
        if (historyPlaylist is null || historyPlaylist.TrackKeys.Count == 0)
        {
            return null;
        }

        var lastKey = historyPlaylist.TrackKeys[^1];
        var byKey = reader.ReadCollection(path).ToDictionary(e => e.Path.ToCombinedKey());
        if (!byKey.TryGetValue(lastKey.ToCombinedKey(), out var entry))
        {
            return null; // Pfad nicht in der COLLECTION gefunden - kann bei einer nur-Playlist-Export-Datei passieren
        }

        return string.IsNullOrWhiteSpace(entry.Artist)
            ? entry.Title
            : $"{entry.Artist} - {entry.Title}";
    }

    private static TraktorPlaylistNode? FindNewestHistoryPlaylist(TraktorPlaylistNode node)
    {
        TraktorPlaylistNode? best = null;
        void Walk(TraktorPlaylistNode current)
        {
            if (current.NodeType == TraktorNodeType.Playlist && current.Name.Contains("History", StringComparison.OrdinalIgnoreCase))
            {
                // Datierte History-Playlisten sortieren als Name (z.B. "History_2026-08-24") alphabetisch
                // korrekt chronologisch - die zuletzt gefundene ist damit die aktuellste.
                if (best is null || string.CompareOrdinal(current.Name, best.Name) >= 0)
                {
                    best = current;
                }
            }

            foreach (var child in current.Children)
            {
                Walk(child);
            }
        }

        Walk(node);
        return best;
    }
}
