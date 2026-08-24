using Cratebase.Core.Models;

namespace Cratebase.Core.Abstractions;

/// <summary>
/// Nimmt von einem Audio-Eingabegerät (Mixer-/Interface-Ausgang, System-Loopback o.ä.) verlustfrei
/// (oder wahlweise MP3) in eine Datei auf - für "Set Recorder" (s. ARCHITECTURE.md). Getrennt von
/// IAudioPlayer (Wiedergabe der Bibliothek): unterschiedliche Lebensdauer, unterschiedliche
/// zugrundeliegende Mechanik (Aufnahme läuft über einen externen ffmpeg-Prozess statt LibVLC, s.
/// FfmpegAudioRecorder-Doku für die Begründung).
/// </summary>
public interface IAudioRecorder
{
    bool IsRecording { get; }

    /// <summary>Listet die auf dieser Plattform verfügbaren Aufnahmegeräte. Kann eine leere Liste
    /// liefern (z.B. ffmpeg fehlt, oder keine Geräte gefunden) - nie werfen, das ist ein normaler,
    /// darstellbarer Zustand ("kein Gerät gefunden"), kein Programmfehler.</summary>
    Task<IReadOnlyList<AudioCaptureDevice>> GetAvailableDevicesAsync(CancellationToken ct = default);

    /// <summary>Startet die Aufnahme in outputPath. Wirft, wenn schon eine Aufnahme läuft oder das
    /// Gerät/Format nicht startbar ist (z.B. ffmpeg fehlt) - der Aufrufer zeigt das als Statusmeldung.</summary>
    Task StartAsync(string deviceId, RecordingFormat format, string outputPath, CancellationToken ct = default);

    /// <summary>Beendet die laufende Aufnahme geordnet (die Datei wird dabei sauber finalisiert,
    /// nicht abgewürgt) und liefert die tatsächliche Dauer. No-op, wenn gerade nichts aufgenommen wird.</summary>
    Task<TimeSpan> StopAsync(CancellationToken ct = default);
}
