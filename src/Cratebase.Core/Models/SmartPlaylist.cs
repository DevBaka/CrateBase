namespace Cratebase.Core.Models;

/// <summary>Dynamic, read-only membership. The definition, not a snapshot of track IDs, is persisted.</summary>
public sealed record SmartPlaylist(string Id, string Name, LibraryTarget Source,
    IReadOnlyList<MetadataFilter> Rules, bool MatchAny = false, string Sort = "artist", bool Descending = false)
{
    public const string StateKey = "smart-playlists-v1";
    public LibraryTarget Target => new(LibraryTargetKind.SmartPlaylist, Name, Path: Id);
    public void Validate()
    {
        if (!Guid.TryParse(Id, out _) || string.IsNullOrWhiteSpace(Name) || Name.Length > 160)
            throw new ArgumentException("Bitte einen Namen mit höchstens 160 Zeichen vergeben.");
        if (!Enum.IsDefined(Source.Kind)||Source.Kind == LibraryTargetKind.SmartPlaylist) throw new ArgumentException("Ungültige oder verschachtelte Smart-Playlist-Quelle.");
        if (Rules.Count > 100) throw new ArgumentException("Höchstens 100 Regeln pro Smart-Playlist.");
        foreach (var rule in Rules) TrackMetadata.Validate(rule);
        if (Sort != "default") _ = TrackMetadata.Get(Sort);
    }
}
