using Cratebase.Core.Models;

namespace Cratebase.Core.Abstractions;

public interface ICuePointRepository
{
    Task<IReadOnlyList<CuePoint>> GetForTrackAsync(long trackId, CancellationToken ct = default);

    /// <summary>
    /// Ersetzt alle Cue-Points einer bestimmten <paramref name="source"/> für einen Track
    /// (löschen + neu einfügen in einer Transaktion). Macht einen erneuten Import idempotent,
    /// ohne einzelne Cue-Points über Läufe hinweg abgleichen zu müssen — und lässt manuell
    /// angelegte Cues (source "manual") unangetastet.
    /// </summary>
    Task ReplaceForTrackAsync(long trackId, string source, IReadOnlyCollection<CuePoint> cuePoints, CancellationToken ct = default);
}
