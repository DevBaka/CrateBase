using System.Globalization;
using System.Xml;
using System.Xml.Linq;

namespace Cratebase.Import;

/// <summary>
/// Liest eine Traktor collection.nml (Format-Version 20, Traktor Pro 3/4). Die COLLECTION wird
/// per XmlReader gestreamt (Dateien realistisch &gt;50MB bei &gt;40k Tracks – ein XDocument.Load
/// über die ganze Datei würde unserem eigenen "nicht alles in den RAM laden"-Grundsatz
/// widersprechen), pro &lt;ENTRY&gt; aber als kleines XElement geparst (einfacher/robuster als
/// manuelles Attribut-für-Attribut-Reader-Traversieren, und pro Entry unproblematisch groß).
/// PLAYLISTS ist dagegen typischerweise klein genug (~wenige MB, s. Kommentar in
/// ReadPlaylistTree) für einen einzigen XElement.Parse.
/// </summary>
public sealed class TraktorNmlReader
{
    public IEnumerable<TraktorTrackEntry> ReadCollection(string nmlPath)
    {
        using var reader = XmlReader.Create(nmlPath, new XmlReaderSettings { IgnoreWhitespace = true, DtdProcessing = DtdProcessing.Ignore });

        // Bewusst kein "while (reader.Read())": ReadOuterXml() positioniert den Reader schon auf
        // den nächsten Knoten (analog zu einem impliziten Read()) - ein zusätzlicher Read() am
        // Schleifenkopf würde nach jedem gelesenen ENTRY einen weiteren Knoten verschlucken und
        // damit jeden zweiten Track überspringen (an echten Daten verifiziert: 40344 Einträge
        // wurden ohne diesen Fix zu exakt 20172 - der Hälfte).
        var hasNode = reader.Read();
        while (hasNode)
        {
            if (reader.NodeType != XmlNodeType.Element)
            {
                hasNode = reader.Read();
                continue;
            }

            if (reader.Name == "PLAYLISTS")
            {
                yield break; // Collection ist vorbei - Rest der Datei ist für ReadCollection irrelevant.
            }

            if (reader.Name != "ENTRY" || reader.Depth != 2)
            {
                hasNode = reader.Read();
                continue;
            }

            var xml = reader.ReadOuterXml(); // liest UND überspringt das Element in einem Schritt - reader.Read() NICHT erneut aufrufen
            var entry = ParseEntry(XElement.Parse(xml));
            if (entry is not null)
            {
                yield return entry;
            }

            hasNode = true; // Reader steht bereits auf dem Folgeknoten
        }
    }

    /// <summary>Playlisten-Baum als Ganzes - der Abschnitt ist bei ~40k Tracks nur ein Bruchteil der
    /// Dateigröße (Cue-Points/Analyse-Daten dominieren die COLLECTION), daher unproblematisch als
    /// ein XElement zu laden statt auch hier zu streamen.</summary>
    public TraktorPlaylistNode? ReadPlaylistTree(string nmlPath)
    {
        using var reader = XmlReader.Create(nmlPath, new XmlReaderSettings { IgnoreWhitespace = true, DtdProcessing = DtdProcessing.Ignore });
        while (reader.Read())
        {
            if (reader.NodeType != XmlNodeType.Element || reader.Name != "PLAYLISTS")
            {
                continue;
            }

            var playlists = XElement.Parse(reader.ReadOuterXml());
            var rootNode = playlists.Element("NODE");
            return rootNode is null ? null : ParseNode(rootNode);
        }

        return null;
    }

    private static TraktorTrackEntry? ParseEntry(XElement entry)
    {
        var location = entry.Element("LOCATION");
        if (location is null)
        {
            return null; // ohne LOCATION nichts, worauf sich der Import beziehen könnte
        }

        var volume = (string?)location.Attribute("VOLUME") ?? "";
        var dir = (string?)location.Attribute("DIR") ?? "";
        var file = (string?)location.Attribute("FILE") ?? "";

        var info = entry.Element("INFO");
        var tempo = entry.Element("TEMPO");

        return new TraktorTrackEntry
        {
            AudioId = (string?)entry.Attribute("AUDIO_ID"),
            Path = TraktorPath.FromLocation(volume, dir, file),
            Title = (string?)entry.Attribute("TITLE"),
            Artist = (string?)entry.Attribute("ARTIST"),
            Album = (string?)entry.Element("ALBUM")?.Attribute("TITLE"),
            Genre = (string?)info?.Attribute("GENRE"),
            Comment = (string?)info?.Attribute("COMMENT"),
            MusicalKey = (string?)info?.Attribute("KEY"),
            Bpm = (double?)tempo?.Attribute("BPM"),
            PlayCount = (int?)info?.Attribute("PLAYCOUNT"),
            LastPlayedUtc = ParseTraktorDate((string?)info?.Attribute("LAST_PLAYED")),
            RankingRaw = (int?)info?.Attribute("RANKING"),
            Cues = entry.Elements("CUE_V2").Select(ParseCue).ToList(),
        };
    }

    private static TraktorCue ParseCue(XElement cue) => new()
    {
        Name = (string?)cue.Attribute("NAME"),
        TraktorType = (int?)cue.Attribute("TYPE") ?? 0,
        StartMs = (double?)cue.Attribute("START") ?? 0,
        LengthMs = (double?)cue.Attribute("LEN") ?? 0,
        HotcueIndex = (int?)cue.Attribute("HOTCUE") ?? -1,
    };

    private static TraktorPlaylistNode ParseNode(XElement node)
    {
        var name = (string?)node.Attribute("NAME") ?? "";
        var typeRaw = (string?)node.Attribute("TYPE") ?? "FOLDER";

        if (typeRaw == "PLAYLIST")
        {
            var keys = node.Element("PLAYLIST")?
                .Elements("ENTRY")
                .Select(e => (string?)e.Element("PRIMARYKEY")?.Attribute("KEY"))
                .Where(key => !string.IsNullOrEmpty(key))
                .Select(key => TraktorPath.FromCombinedKey(key!))
                .OfType<TraktorPath>()
                .ToList() ?? [];

            return new TraktorPlaylistNode { Name = name, NodeType = TraktorNodeType.Playlist, TrackKeys = keys };
        }

        var children = node.Element("SUBNODES")?
            .Elements("NODE")
            .Select(ParseNode)
            .ToList() ?? [];

        return new TraktorPlaylistNode { Name = name, NodeType = TraktorNodeType.Folder, Children = children };
    }

    private static DateTime? ParseTraktorDate(string? raw) =>
        !string.IsNullOrWhiteSpace(raw) &&
        DateTime.TryParseExact(raw, "yyyy/M/d", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : null;
}
