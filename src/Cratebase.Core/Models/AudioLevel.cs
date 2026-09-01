namespace Cratebase.Core.Models;

/// <summary>Aktueller Spitzenpegel der laufenden Aufnahme, ein Wert je Kanal in dBFS (0 = Vollaussteuerung,
/// negative Werte = Headroom). Reine Messwerte - welche Schwellen als "Warnung" oder "Übersteuerung"
/// gelten, entscheidet die Anzeige (MainViewModel), nicht dieses Modell. Für die Live-Pegelanzeige im
/// Set Recorder gedacht (s. IAudioRecorder.CurrentLevel).</summary>
public sealed record AudioLevel(IReadOnlyList<double> ChannelPeaksDb);
