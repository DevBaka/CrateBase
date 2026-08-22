using System.Globalization;
using System.Text;
using System.Xml;
using Cratebase.Core.Abstractions;
using Cratebase.Core.Models;

namespace Cratebase.Import;

/// <summary>
/// Schreibt den kompletten Cratebase-Playlist-Baum als eigenständige NML-Datei (s.
/// INmlExporter-Doku - bewusst kein Schreiben in Traktors eigene collection.nml, s.
/// ARCHITECTURE.md Abschnitt 20). Struktur/Feldnamen spiegeln bewusst genau das, was
/// TraktorNmlReader beim Lesen erwartet - der eigene Reader ist der einzige verfügbare
/// Cross-Check ohne eine echte Traktor-Installation zur Hand zu haben.
/// </summary>
public sealed class TraktorNmlWriter(
    ITrackRepository trackRepository,
    ILibraryRootRepository libraryRootRepository,
    IPlaylistRepository playlistRepository) : INmlExporter
{
    public async Task<NmlExportSummary> ExportAsync(string outputPath, CancellationToken ct = default)
    {
        var roots = (await libraryRootRepository.GetAllAsync(ct)).ToDictionary(r => r.Id);
        var allNodes = await playlistRepository.GetAllNodesAsync(ct);
        var childrenByParent = allNodes
            .GroupBy(n => n.ParentId ?? -1L)
            .ToDictionary(g => g.Key, g => g.OrderBy(n => n.SortOrder).ThenBy(n => n.Name, StringComparer.OrdinalIgnoreCase).ToList());

        var exportTracksById = new Dictionary<long, ExportTrack>();
        var skippedTrackIds = new HashSet<long>();
        var playlistsExported = 0;

        async Task<ExportPlaylistNode> BuildNodeAsync(PlaylistNode node)
        {
            if (node.NodeType == PlaylistNodeType.Playlist)
            {
                playlistsExported++;
                var tracks = await trackRepository.SearchAsync(null, [node.Id], includeMissing: false, limit: 1_000_000, offset: 0, ct);
                var exportTracks = new List<ExportTrack>();
                foreach (var track in tracks)
                {
                    if (skippedTrackIds.Contains(track.Id))
                    {
                        continue;
                    }

                    if (!exportTracksById.TryGetValue(track.Id, out var exportTrack))
                    {
                        exportTrack = TryBuildExportTrack(track, roots);
                        if (exportTrack is null)
                        {
                            skippedTrackIds.Add(track.Id);
                            continue;
                        }

                        exportTracksById[track.Id] = exportTrack;
                    }

                    exportTracks.Add(exportTrack);
                }

                return new ExportPlaylistNode(node.Name, IsPlaylist: true, Tracks: exportTracks, Children: []);
            }

            var children = new List<ExportPlaylistNode>();
            if (childrenByParent.TryGetValue(node.Id, out var childNodes))
            {
                foreach (var child in childNodes)
                {
                    ct.ThrowIfCancellationRequested();
                    children.Add(await BuildNodeAsync(child));
                }
            }

            return new ExportPlaylistNode(node.Name, IsPlaylist: false, Tracks: [], Children: children);
        }

        var rootChildren = new List<ExportPlaylistNode>();
        if (childrenByParent.TryGetValue(-1L, out var topLevelNodes))
        {
            foreach (var node in topLevelNodes)
            {
                ct.ThrowIfCancellationRequested();
                rootChildren.Add(await BuildNodeAsync(node));
            }
        }

        WriteNml(outputPath, exportTracksById.Values, rootChildren);

        return new NmlExportSummary(playlistsExported, exportTracksById.Count, skippedTrackIds.Count);
    }

    /// <summary>Liefert null, wenn der LibraryRoot des Tracks noch nie erfolgreich mit einem
    /// Traktor-Import abgeglichen wurde (s. LibraryRoot.TraktorVolume-Doku) - lieber den Track
    /// (und jede Playlist-Zeile, die auf ihn zeigt) auslassen, als einen Pfad zu raten, den
    /// Traktor beim Reimport nicht wiedererkennen würde.</summary>
    private static ExportTrack? TryBuildExportTrack(Track track, IReadOnlyDictionary<long, LibraryRoot> roots)
    {
        if (!roots.TryGetValue(track.LibraryRootId, out var root) || string.IsNullOrEmpty(root.TraktorVolume))
        {
            return null;
        }

        var prefixSegments = string.IsNullOrEmpty(root.TraktorDirPrefix)
            ? []
            : root.TraktorDirPrefix.Split("/:", StringSplitOptions.RemoveEmptyEntries);

        var relativeSegments = track.RelativePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (relativeSegments.Length == 0)
        {
            return null;
        }

        var dirSegments = prefixSegments.Concat(relativeSegments[..^1]).ToList();
        var fileName = relativeSegments[^1];
        var path = new TraktorPath(root.TraktorVolume, dirSegments, fileName);

        return new ExportTrack(track.Title, track.Artist, track.Album, track.GenreRaw, track.Comment, track.MusicalKey, track.Bpm, path);
    }

    private static void WriteNml(string outputPath, IEnumerable<ExportTrack> tracks, IReadOnlyList<ExportPlaylistNode> rootChildren)
    {
        var trackList = tracks.ToList();
        var settings = new XmlWriterSettings { Indent = true, Encoding = Encoding.UTF8 };
        using var writer = XmlWriter.Create(outputPath, settings);

        writer.WriteStartDocument();
        writer.WriteStartElement("NML");
        writer.WriteAttributeString("VERSION", "19");

        writer.WriteStartElement("HEAD");
        writer.WriteAttributeString("COMPANY", "www.native-instruments.com");
        writer.WriteAttributeString("PROGRAM", "Traktor - Native Instruments");
        writer.WriteEndElement(); // HEAD

        writer.WriteElementString("MUSICFOLDERS", "");

        writer.WriteStartElement("COLLECTION");
        writer.WriteAttributeString("ENTRIES", trackList.Count.ToString(CultureInfo.InvariantCulture));
        foreach (var track in trackList)
        {
            WriteCollectionEntry(writer, track);
        }

        writer.WriteEndElement(); // COLLECTION

        writer.WriteStartElement("SETS");
        writer.WriteAttributeString("ENTRIES", "0");
        writer.WriteEndElement(); // SETS

        writer.WriteStartElement("PLAYLISTS");
        writer.WriteStartElement("NODE");
        writer.WriteAttributeString("TYPE", "FOLDER");
        writer.WriteAttributeString("NAME", "$ROOT");
        WriteSubnodes(writer, rootChildren);
        writer.WriteEndElement(); // NODE $ROOT
        writer.WriteEndElement(); // PLAYLISTS

        writer.WriteEndElement(); // NML
        writer.WriteEndDocument();
    }

    private static void WriteCollectionEntry(XmlWriter writer, ExportTrack track)
    {
        writer.WriteStartElement("ENTRY");
        if (!string.IsNullOrEmpty(track.Title))
        {
            writer.WriteAttributeString("TITLE", track.Title);
        }

        if (!string.IsNullOrEmpty(track.Artist))
        {
            writer.WriteAttributeString("ARTIST", track.Artist);
        }

        writer.WriteStartElement("LOCATION");
        writer.WriteAttributeString("DIR", track.Path.ToLocationDir());
        writer.WriteAttributeString("FILE", track.Path.FileName);
        writer.WriteAttributeString("VOLUME", track.Path.Volume);
        writer.WriteAttributeString("VOLUMEID", "");
        writer.WriteEndElement(); // LOCATION

        if (!string.IsNullOrEmpty(track.Album))
        {
            writer.WriteStartElement("ALBUM");
            writer.WriteAttributeString("TITLE", track.Album);
            writer.WriteEndElement();
        }

        if (!string.IsNullOrEmpty(track.Genre) || !string.IsNullOrEmpty(track.Comment) || !string.IsNullOrEmpty(track.MusicalKey))
        {
            writer.WriteStartElement("INFO");
            if (!string.IsNullOrEmpty(track.Genre))
            {
                writer.WriteAttributeString("GENRE", track.Genre);
            }

            if (!string.IsNullOrEmpty(track.Comment))
            {
                writer.WriteAttributeString("COMMENT", track.Comment);
            }

            if (!string.IsNullOrEmpty(track.MusicalKey))
            {
                writer.WriteAttributeString("KEY", track.MusicalKey);
            }

            writer.WriteEndElement(); // INFO
        }

        if (track.Bpm is > 0)
        {
            writer.WriteStartElement("TEMPO");
            writer.WriteAttributeString("BPM", track.Bpm.Value.ToString("0.###", CultureInfo.InvariantCulture));
            writer.WriteEndElement();
        }

        writer.WriteEndElement(); // ENTRY
    }

    private static void WriteSubnodes(XmlWriter writer, IReadOnlyList<ExportPlaylistNode> children)
    {
        writer.WriteStartElement("SUBNODES");
        writer.WriteAttributeString("COUNT", children.Count.ToString(CultureInfo.InvariantCulture));

        foreach (var child in children)
        {
            writer.WriteStartElement("NODE");
            writer.WriteAttributeString("TYPE", child.IsPlaylist ? "PLAYLIST" : "FOLDER");
            writer.WriteAttributeString("NAME", child.Name);

            if (child.IsPlaylist)
            {
                writer.WriteStartElement("PLAYLIST");
                writer.WriteAttributeString("ENTRIES", child.Tracks.Count.ToString(CultureInfo.InvariantCulture));
                writer.WriteAttributeString("TYPE", "LIST");
                foreach (var track in child.Tracks)
                {
                    writer.WriteStartElement("ENTRY");
                    writer.WriteStartElement("PRIMARYKEY");
                    writer.WriteAttributeString("TYPE", "TRACK");
                    writer.WriteAttributeString("KEY", track.Path.ToCombinedKey());
                    writer.WriteEndElement(); // PRIMARYKEY
                    writer.WriteEndElement(); // ENTRY
                }

                writer.WriteEndElement(); // PLAYLIST
            }
            else
            {
                WriteSubnodes(writer, child.Children);
            }

            writer.WriteEndElement(); // NODE
        }

        writer.WriteEndElement(); // SUBNODES
    }

    private sealed record ExportTrack(string? Title, string? Artist, string? Album, string? Genre, string? Comment, string? MusicalKey, double? Bpm, TraktorPath Path);

    private sealed record ExportPlaylistNode(string Name, bool IsPlaylist, List<ExportTrack> Tracks, List<ExportPlaylistNode> Children);
}
