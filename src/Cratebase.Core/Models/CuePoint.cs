namespace Cratebase.Core.Models;

/// <summary>
/// Herstellerunabhängiges Cue-Point-Modell (s. ARCHITECTURE.md 2.7) — Import-Adapter (z.B.
/// Traktor) mappen ihr eigenes Format hierauf, statt dass sich die App an ein Vendor-Format bindet.
/// </summary>
public sealed class CuePoint
{
    public long Id { get; set; }
    public long TrackId { get; set; }

    /// <summary>"hot_cue" | "grid_marker" | "loop" | "fade_in" | "fade_out" | "load" | "other" — bewusst string statt Enum, damit Import-Adapter neue Typen ergänzen können, ohne das Schema zu ändern.</summary>
    public string Type { get; set; } = "hot_cue";

    /// <summary>Hotcue-Pad-Index (z.B. 0-7), null wenn nicht pad-gebunden (z.B. Grid-Marker).</summary>
    public int? SlotIndex { get; set; }

    public long PositionMs { get; set; }
    public long? LoopLengthMs { get; set; }
    public string? Label { get; set; }

    /// <summary>"manual" | "imported:traktor" | ... — Herkunft, u.a. damit ein Reimport gezielt nur seine eigenen Cues ersetzen kann.</summary>
    public string Source { get; set; } = "manual";
}
