using Cratebase.Core.Abstractions;
using Cratebase.Core.Models;

namespace Cratebase.Import;

/// <summary>
/// Traktor-Reader (s. ARCHITECTURE.md Phase 2, "Reader zuerst"): liest eine collection.nml
/// und verknüpft ihre Einträge mit bereits über den eigenen Scanner bekannten Tracks. Nicht
/// zuordenbare Einträge (Track noch nicht gescannt, o.ä.) werden übersprungen und gezählt,
/// nie geraten - s. TraktorPathResolver. Playlisten landen gebündelt unter einem eigenen
/// "Traktor Import"-Wurzelordner (s. ARCHITECTURE.md 4.E: bewusst getrennt vom Rest des
/// Playlist-Baums, solange das Verhältnis Festplatten-/Playlist-Struktur nicht final geklärt ist).
/// </summary>
public sealed class TraktorCollectionImporter(
    ITrackRepository trackRepository,
    ILibraryRootRepository libraryRootRepository,
    IPlaylistRepository playlistRepository,
    ICuePointRepository cuePointRepository) : ICollectionImporter
{
    private const string ImportSource = "imported:traktor";
    private const string ImportRootFolderName = "Traktor Import";
    private const int ProgressReportInterval = 500;

    public string DisplayName => "Traktor (NML)";

    public async Task<ImportSummary> ImportAsync(string sourceFilePath, IProgress<ImportProgress>? progress, CancellationToken ct = default)
    {
        var resolver = await BuildResolverAsync(ct);
        var reader = new TraktorNmlReader();

        var entriesSeen = 0;
        var tracksMatched = 0;
        var tracksUnmatched = 0;
        var cuePointsImported = 0;

        foreach (var entry in reader.ReadCollection(sourceFilePath))
        {
            ct.ThrowIfCancellationRequested();
            entriesSeen++;

            var trackId = resolver.TryResolveTrackId(entry.Path);
            if (trackId is null)
            {
                tracksUnmatched++;
            }
            else
            {
                tracksMatched++;
                await MergeIntoTrackAsync(trackId.Value, entry, ct);
                cuePointsImported += await ImportCuesAsync(trackId.Value, entry, ct);
            }

            if (entriesSeen % ProgressReportInterval == 0)
            {
                progress?.Report(new ImportProgress(entriesSeen, tracksMatched, tracksUnmatched, 0, cuePointsImported, entry.Title));
            }
        }

        var traktorPlaylistRoot = reader.ReadPlaylistTree(sourceFilePath);
        var (playlistsCreated, tracksLinked) = traktorPlaylistRoot is null
            ? (0, 0)
            : await ImportPlaylistTreeAsync(traktorPlaylistRoot, resolver, ct);

        await PersistLearnedTraktorMappingsAsync(resolver, ct);

        var summary = new ImportSummary(entriesSeen, tracksMatched, tracksUnmatched, playlistsCreated, tracksLinked, cuePointsImported);
        progress?.Report(new ImportProgress(entriesSeen, tracksMatched, tracksUnmatched, playlistsCreated, cuePointsImported, null));
        return summary;
    }

    /// <summary>Merkt sich für jeden LibraryRoot, wie Traktor ihn selbst benennt (s. LibraryRoot.
    /// TraktorVolume-Doku und TraktorPathResolver.LearnedMappings) - Grundlage für den NML-Export
    /// (s. ARCHITECTURE.md Abschnitt 20). Überschreibt einen zuvor gelernten Wert bewusst mit dem
    /// aktuellen Import-Durchlauf (falls sich die Traktor-Ordnerstruktur seither geändert hat).</summary>
    private async Task PersistLearnedTraktorMappingsAsync(TraktorPathResolver resolver, CancellationToken ct)
    {
        foreach (var (rootId, mapping) in resolver.LearnedMappings)
        {
            await libraryRootRepository.SetTraktorMappingAsync(rootId, mapping.Volume, string.Join("/:", mapping.DirSegments), ct);
        }
    }

    private async Task<TraktorPathResolver> BuildResolverAsync(CancellationToken ct)
    {
        var resolver = new TraktorPathResolver();
        var roots = await libraryRootRepository.GetAllAsync(ct);
        foreach (var root in roots)
        {
            var fingerprints = await trackRepository.GetFingerprintsByPathAsync(root.Id, ct);
            var lookup = fingerprints.ToDictionary(kv => kv.Key, kv => kv.Value.TrackId);
            resolver.AddRoot(root, lookup);
        }

        return resolver;
    }

    /// <summary>Reichert einen bereits bekannten Track mit Traktor-Daten an - nur Felder, die
    /// bei uns noch leer/0 sind (s. ITrackRepository.UpdateAsync-Doku: bewusst nicht "Traktor
    /// gewinnt immer", sondern "Traktor füllt Lücken").</summary>
    private async Task MergeIntoTrackAsync(long trackId, TraktorTrackEntry entry, CancellationToken ct)
    {
        var track = await trackRepository.GetByIdAsync(trackId, ct);
        if (track is null)
        {
            return;
        }

        var changed = false;

        if (string.IsNullOrWhiteSpace(track.GenreRaw) && !string.IsNullOrWhiteSpace(entry.Genre))
        {
            track.GenreRaw = entry.Genre;
            changed = true;
        }

        if (string.IsNullOrWhiteSpace(track.MusicalKey) && !string.IsNullOrWhiteSpace(entry.MusicalKey))
        {
            track.MusicalKey = entry.MusicalKey;
            changed = true;
        }

        if (string.IsNullOrWhiteSpace(track.Comment) && !string.IsNullOrWhiteSpace(entry.Comment))
        {
            track.Comment = entry.Comment;
            changed = true;
        }

        if (track.Bpm is null && entry.Bpm is > 0)
        {
            track.Bpm = entry.Bpm;
            changed = true;
        }

        if (track.Rating == 0 && entry.RatingStars is > 0)
        {
            track.Rating = entry.RatingStars.Value;
            changed = true;
        }

        if (track.PlayCount == 0 && entry.PlayCount is > 0)
        {
            track.PlayCount = entry.PlayCount.Value;
            changed = true;
        }

        if (track.LastPlayedAtUtc is null && entry.LastPlayedUtc is not null)
        {
            track.LastPlayedAtUtc = entry.LastPlayedUtc;
            changed = true;
        }

        if (!changed)
        {
            return;
        }

        track.DateModifiedUtc = DateTime.UtcNow;
        await trackRepository.UpdateAsync(track, ct);
    }

    private async Task<int> ImportCuesAsync(long trackId, TraktorTrackEntry entry, CancellationToken ct)
    {
        if (entry.Cues.Count == 0)
        {
            return 0;
        }

        var cuePoints = entry.Cues.Select(c => new CuePoint
        {
            TrackId = trackId,
            Type = MapCueType(c.TraktorType),
            SlotIndex = c.HotcueIndex >= 0 ? c.HotcueIndex : null,
            PositionMs = (long)Math.Round(c.StartMs),
            LoopLengthMs = c.LengthMs > 0 ? (long)Math.Round(c.LengthMs) : null,
            Label = string.IsNullOrWhiteSpace(c.Name) || c.Name == "n.n." ? null : c.Name,
            Source = ImportSource,
        }).ToList();

        await cuePointRepository.ReplaceForTrackAsync(trackId, ImportSource, cuePoints, ct);
        return cuePoints.Count;
    }

    /// <summary>Traktors interner CUE_V2-Typ-Code (aus realen NML-Dateien verifiziert: 0 und 4 kommen vor, s. TraktorModels.cs für die vollständige, aus öffentlich bekannten Traktor-NML-Parsern übernommene Tabelle).</summary>
    private static string MapCueType(int traktorType) => traktorType switch
    {
        0 => "hot_cue",
        1 => "fade_in",
        2 => "fade_out",
        3 => "load",
        4 => "grid_marker",
        5 => "loop",
        _ => "other",
    };

    private async Task<(int PlaylistsCreated, int TracksLinked)> ImportPlaylistTreeAsync(TraktorPlaylistNode traktorRoot, TraktorPathResolver resolver, CancellationToken ct)
    {
        var existingNodes = await playlistRepository.GetAllNodesAsync(ct);
        // -1 als Sentinel für "Root-Ebene" (ParentId == null), da -1 nie eine echte Id ist.
        // Bewusst kein ToDictionary (würde bei zwei gleichnamigen Knoten unter demselben Parent -
        // z.B. mehrfach angelegte "Neue Playlist" - mit "Ein Element mit demselben Schlüssel wurde
        // bereits hinzugefügt" abstürzen und JEDEN Traktor-Import blockieren, nicht nur den Namens-
        // konflikt selbst). GroupBy+First: bei Duplikaten zählt deterministisch der erste Treffer,
        // der Import läuft trotzdem durch.
        var nodeIndex = existingNodes
            .GroupBy(n => (n.ParentId ?? -1, n.Name))
            .ToDictionary(g => g.Key, g => g.First().Id);

        var counters = new ImportCounters();
        var importRootKey = (-1L, ImportRootFolderName);
        if (!nodeIndex.TryGetValue(importRootKey, out var importRootId))
        {
            var created = await playlistRepository.CreateAsync(ImportRootFolderName, PlaylistNodeType.Folder, null, ct);
            importRootId = created.Id;
            nodeIndex[importRootKey] = importRootId;
            counters.PlaylistsCreated++;
        }

        foreach (var child in traktorRoot.Children)
        {
            ct.ThrowIfCancellationRequested();
            await ImportNodeAsync(child, importRootId, nodeIndex, resolver, counters, ct);
        }

        return (counters.PlaylistsCreated, counters.TracksLinked);
    }

    private async Task ImportNodeAsync(TraktorPlaylistNode node, long parentId, Dictionary<(long, string), long> nodeIndex, TraktorPathResolver resolver, ImportCounters counters, CancellationToken ct)
    {
        var key = (parentId, node.Name);
        if (!nodeIndex.TryGetValue(key, out var nodeId))
        {
            var nodeType = node.NodeType == TraktorNodeType.Folder ? PlaylistNodeType.Folder : PlaylistNodeType.Playlist;
            var created = await playlistRepository.CreateAsync(node.Name, nodeType, parentId, ct);
            nodeId = created.Id;
            nodeIndex[key] = nodeId;
            counters.PlaylistsCreated++;
        }

        if (node.NodeType == TraktorNodeType.Playlist)
        {
            var trackIds = node.TrackKeys
                .Select(resolver.TryResolveTrackId)
                .OfType<long>()
                .Distinct()
                .ToList();

            if (trackIds.Count > 0)
            {
                await playlistRepository.AddTracksAsync(nodeId, trackIds, ct);
                counters.TracksLinked += trackIds.Count;
            }

            return;
        }

        foreach (var child in node.Children)
        {
            ct.ThrowIfCancellationRequested();
            await ImportNodeAsync(child, nodeId, nodeIndex, resolver, counters, ct);
        }
    }

    private sealed class ImportCounters
    {
        public int PlaylistsCreated;
        public int TracksLinked;
    }
}
