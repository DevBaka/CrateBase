using Cratebase.Core.Models;

namespace Cratebase.Core.Abstractions;

/// <summary>
/// Dekodiert eine Audiodatei zu einer <see cref="WaveformData"/> (3-Band-Peaks). Implementiert
/// in Cratebase.Analysis via externem ffmpeg-Prozess (kein Playback, nur Analyse) - liefert
/// bewusst null statt zu werfen, wenn ffmpeg fehlt oder die Datei nicht dekodierbar ist: eine
/// fehlende Waveform darf die App nicht stören, sie zeigt dann einfach nichts an.
/// </summary>
public interface IWaveformGenerator
{
    Task<WaveformData?> GenerateAsync(string absoluteFilePath, CancellationToken ct = default);
}

/// <summary>
/// Waveform-Cache auf Disk, nicht in der DB (s. ARCHITECTURE.md 2.9: "Cache auf Disk, nicht als
/// Blob in der DB"). Cache-Key aus Track-Id + Dateigröße + mtime, überlebt also keine
/// Datei-Verschiebung, aber ohne vollen Content-Hash berechnen zu müssen (s. 4.B) - für den
/// Prototyp ausreichend, echte Content-Hashes sind ein späterer Ausbau.
/// </summary>
public interface IWaveformCache
{
    Task<WaveformData?> TryLoadAsync(Track track, CancellationToken ct = default);

    Task SaveAsync(Track track, WaveformData data, CancellationToken ct = default);
}
