using Cratebase.Core.Models;

namespace Cratebase.Import;

/// <summary>
/// Löst einen TraktorPath gegen die bereits über den eigenen Scanner bekannten LibraryRoots auf –
/// ohne dass der Nutzer manuell ein Laufwerksbuchstaben-Mapping pflegen muss. Idee: Traktors
/// eigener Pfad und unser LibraryRoot.Path überlappen sich meistens in den letzten/ersten
/// Ordnersegmenten (z.B. Traktor "M:/Musik/DnB/..." vs. unser Root ".../Musik/Musik",
/// gemountet unter einem anderen Namen) – wir suchen die größte solche Überlappung und
/// verifizieren sie, indem der resultierende relative Pfad tatsächlich ein bekannter Track
/// sein muss. Kein Treffer ⇒ kein Match, keine Rätselraten-Zuordnung.
/// </summary>
public sealed class TraktorPathResolver
{
    private readonly List<RootIndex> _roots = [];
    private readonly Dictionary<long, TraktorRootMapping> _learnedMappings = new();

    /// <summary>Pro LibraryRoot.Id die beim Auflösen gelernte Traktor-Zuordnung (Laufwerksbuchstabe +
    /// überlappende Pfad-Segmente, s. TraktorRootMapping) - für den NML-Export genutzt, damit
    /// exportierte Playlist-Einträge auf einen Pfad zeigen, den Traktor selbst wiedererkennt. Nur die
    /// ERSTE erfolgreiche Auflösung pro Root wird gemerkt (spätere Einträge desselben Roots liefern
    /// ohnehin dieselben überlappenden Segmente, s. TryResolveTrackId).</summary>
    public IReadOnlyDictionary<long, TraktorRootMapping> LearnedMappings => _learnedMappings;

    /// <summary>Lädt die bekannten relative_paths eines Roots einmalig vor (nicht pro Traktor-Eintrag neu abfragen).</summary>
    public void AddRoot(LibraryRoot root, IReadOnlyDictionary<string, long> relativePathToTrackId)
    {
        var segments = root.Path
            .Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToArray();

        // Case-insensitive Lookup, da Windows/Traktor-Pfade und unsere (ggf. auf Linux
        // gescannten) relative_paths in der Groß-/Kleinschreibung abweichen können.
        var lookup = new Dictionary<string, long>(relativePathToTrackId, StringComparer.OrdinalIgnoreCase);
        _roots.Add(new RootIndex(root, segments, lookup));
    }

    public long? TryResolveTrackId(TraktorPath path)
    {
        foreach (var root in _roots)
        {
            for (var overlap = Math.Min(root.PathSegments.Length, path.DirSegments.Count); overlap >= 0; overlap--)
            {
                if (!SegmentsMatch(root.PathSegments, path.DirSegments, overlap))
                {
                    continue;
                }

                var remainingDirSegments = path.DirSegments.Skip(overlap);
                var candidateRelativePath = string.Join('/', remainingDirSegments.Append(path.FileName));

                if (root.KnownRelativePaths.TryGetValue(candidateRelativePath, out var trackId))
                {
                    if (!_learnedMappings.ContainsKey(root.Root.Id))
                    {
                        _learnedMappings[root.Root.Id] = new TraktorRootMapping(path.Volume, path.DirSegments.Take(overlap).ToList());
                    }

                    return trackId;
                }
            }
        }

        return null;
    }

    /// <summary>Vergleicht die letzten <paramref name="count"/> Segmente von <paramref name="rootSegments"/> mit den ersten <paramref name="count"/> von <paramref name="traktorDirSegments"/>.</summary>
    private static bool SegmentsMatch(IReadOnlyList<string> rootSegments, IReadOnlyList<string> traktorDirSegments, int count)
    {
        if (count == 0)
        {
            return true; // Fallback: kompletter Traktor-Pfad direkt relativ zum Root probieren.
        }

        if (count > rootSegments.Count || count > traktorDirSegments.Count)
        {
            return false;
        }

        for (var i = 0; i < count; i++)
        {
            var rootSegment = rootSegments[rootSegments.Count - count + i];
            var traktorSegment = traktorDirSegments[i];
            if (!string.Equals(rootSegment, traktorSegment, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    private sealed record RootIndex(LibraryRoot Root, string[] PathSegments, Dictionary<string, long> KnownRelativePaths);
}

/// <summary>Wie Traktor einen bestimmten LibraryRoot selbst benennt - Laufwerksbuchstabe plus die
/// Pfad-Segmente, die beim Import als mit diesem Root überlappend erkannt wurden (s.
/// TraktorPathResolver.LearnedMappings). Grundlage für den NML-Export: jeder Track unter diesem Root
/// (egal ob ursprünglich aus Traktor importiert oder nur lokal gescannt) lässt sich daraus einen
/// Traktor-verständlichen Pfad zurückrechnen (Volume + DirSegments + RelativePath-Rest).</summary>
public sealed record TraktorRootMapping(string Volume, IReadOnlyList<string> DirSegments);
