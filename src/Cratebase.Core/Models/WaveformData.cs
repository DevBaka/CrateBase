namespace Cratebase.Core.Models;

/// <summary>
/// Eine Spalte der Waveform-Anzeige: je ein normalisierter Pegel (0..1) pro Frequenzband
/// (Bass/Mitten/Höhen) statt eines einzelnen Amplitudenwerts – ergibt die "RGB-Waveform"
/// (rot=Bass, grün=Mitten, blau=Höhen), wie sie in gängiger DJ-Software üblich ist.
/// Kein echtes Spektrogramm (keine FFT) – drei einfache IIR-Filter (Tief-/Bandpass/Hochpass),
/// bewusst leichtgewichtig statt akademisch exakt.
/// </summary>
public readonly record struct WaveformColumn(float Low, float Mid, float High);

public sealed class WaveformData
{
    public IReadOnlyList<WaveformColumn> Columns { get; init; } = [];
}
