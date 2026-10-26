namespace Cratebase.Core.Models;

public enum LibraryTargetKind { Collection, ClubCollection, Playlist, PlaylistFolder, Directory, TraktorCollection, ClubUnsorted, UnreachableFiles, SmartPlaylist }

public sealed record LibraryTarget(LibraryTargetKind Kind, string Name, long? PlaylistId = null, string? Path = null);

public sealed record TrackQuery(LibraryTarget Source, string? Search = null, string Sort = "default", bool Descending = false, int Limit = 500, int Offset = 0, IReadOnlyList<MetadataFilter>? Filters = null, string SearchField = "all", IReadOnlyList<MetadataFilter>? SmartRules = null, bool SmartMatchAny = false);
public sealed class TrackTotals
{
    public int Count { get; set; }
    public long Bytes { get; set; }
    public long DurationMs { get; set; }
    public int UnknownSizes { get; set; }
}
