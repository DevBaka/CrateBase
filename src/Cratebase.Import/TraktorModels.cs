namespace Cratebase.Import;

public sealed class TraktorCue
{
    public string? Name { get; init; }

    /// <summary>Traktors interner Typ-Code: 0=Cue, 1=FadeIn, 2=FadeOut, 3=Load, 4=Grid, 5=Loop.</summary>
    public int TraktorType { get; init; }

    public double StartMs { get; init; }
    public double LengthMs { get; init; }

    /// <summary>Hotcue-Pad-Index, -1 wenn nicht pad-gebunden.</summary>
    public int HotcueIndex { get; init; } = -1;
}

public sealed class TraktorTrackEntry
{
    public string? AudioId { get; init; }
    public required TraktorPath Path { get; init; }
    public string? Title { get; init; }
    public string? Artist { get; init; }
    public string? Album { get; init; }
    public string? Genre { get; init; }
    public string? Comment { get; init; }

    /// <summary>Traktor liefert den Musical Key schon als lesbaren String (INFO/@KEY, z.B. "Ebm") – kein eigenes Mapping von Traktors internem Zahlencode nötig.</summary>
    public string? MusicalKey { get; init; }

    public double? Bpm { get; init; }
    public int? PlayCount { get; init; }
    public DateTime? LastPlayedUtc { get; init; }

    /// <summary>Traktor RANKING ist 0/51/102/153/204/255 (0-5 Sterne in 51er-Schritten).</summary>
    public int? RankingRaw { get; init; }

    public IReadOnlyList<TraktorCue> Cues { get; init; } = [];

    public int? RatingStars => RankingRaw is { } raw ? (int)Math.Round(raw / 51.0) : null;
}

public enum TraktorNodeType
{
    Folder,
    Playlist,
}

public sealed class TraktorPlaylistNode
{
    public required string Name { get; init; }
    public required TraktorNodeType NodeType { get; init; }
    public List<TraktorPlaylistNode> Children { get; init; } = [];
    public List<TraktorPath> TrackKeys { get; init; } = [];
}
