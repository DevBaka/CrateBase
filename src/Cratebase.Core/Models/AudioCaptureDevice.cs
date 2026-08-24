namespace Cratebase.Core.Models;

/// <summary>Ein Audio-Aufnahmegerät (Capture-Device) - Id ist der plattformspezifische Bezeichner,
/// den IAudioRecorder direkt an ffmpeg weiterreicht (z.B. ein PulseAudio/PipeWire-Quellenname unter
/// Linux), DisplayName die für Menschen aufbereitete Anzeige.</summary>
public sealed record AudioCaptureDevice(string Id, string DisplayName);

/// <summary>Ausgabeformat der Aufnahme - FLAC/WAV verlustfrei ("bestenfalls verlustfrei mit hoher
/// Qualität", Nutzer-Feedback), MP3 als kleinere, verlustbehaftete Alternative für schnelles Teilen.</summary>
public enum RecordingFormat
{
    Flac,
    Wav,
    Mp3320,
}
