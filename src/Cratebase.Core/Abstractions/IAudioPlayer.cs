namespace Cratebase.Core.Abstractions;

public enum PlaybackState
{
    Stopped,
    Playing,
    Paused,
}

/// <summary>
/// Abstraktion über die Wiedergabe-Engine (s. ARCHITECTURE.md 4.A – die konkrete Engine
/// ist noch offen zwischen ManagedBass/NAudio; dieses MVP implementiert sie mit LibVLC,
/// weil das heute zuverlässig auf Linux/Windows/Mac ohne manuelle Lizenz-/Binary-Klärung
/// läuft. Austausch der Engine betrifft nur Cratebase.Audio, nicht die UI.
/// </summary>
public interface IAudioPlayer : IDisposable
{
    PlaybackState State { get; }
    TimeSpan Position { get; }
    TimeSpan Duration { get; }

    /// <summary>0.0 – 1.0</summary>
    float Volume { get; set; }

    /// <summary>Feuert, wenn der Track natürlich zu Ende gespielt wurde (nicht bei manuellem Stop).</summary>
    event EventHandler? PlaybackEnded;

    /// <summary>Feuert regelmäßig während der Wiedergabe, für die Seek-Leiste.</summary>
    event EventHandler? PositionChanged;

    void Load(string absoluteFilePath);
    void Play();
    void Pause();
    void Stop();
    void Seek(TimeSpan position);
}
