using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using Cratebase.Core.Abstractions;
using Cratebase.Core.Models;

namespace Cratebase.Audio;

/// <summary>
/// Nimmt über einen externen ffmpeg-Prozess von einem Audio-Eingabegerät auf (s. IAudioRecorder-Doku).
/// Bewusst ffmpeg statt LibVLC (obwohl LibVLCSharp hier schon Projekt-Abhängigkeit für die Wiedergabe
/// ist, s. LibVlcAudioPlayer): LibVLCs Capture-Geräte-Enumeration ist plattformübergreifend deutlich
/// dünner dokumentiert/getestet als ffmpegs (pulse/dshow/avfoundation sind Standard-ffmpeg-Demuxer),
/// UND ffmpeg ist bereits als externe Abhängigkeit etabliert (s. FfmpegWaveformGenerator in
/// Cratebase.Analysis, gleiches "Prozess starten, PATH-Abhängigkeit, nie werfen sondern degradieren"-
/// Muster wird hier bewusst wiederverwendet).
///
/// Live gegen ein echtes Audio-Interface unter Linux (PipeWire/PulseAudio) verifiziert - Windows
/// (DirectShow) und macOS (AVFoundation) sind nach denselben, gut dokumentierten ffmpeg-Demuxer-
/// Konventionen implementiert, aber mangels verfügbarer Testinstallation NICHT live getestet
/// (s. ARCHITECTURE.md für die Einschränkung).
/// </summary>
public sealed class FfmpegAudioRecorder : IAudioRecorder
{
    private Process? _process;
    private Stopwatch? _stopwatch;

    public bool IsRecording => _process is { HasExited: false };

    public async Task<IReadOnlyList<AudioCaptureDevice>> GetAvailableDevicesAsync(CancellationToken ct = default)
    {
        try
        {
            if (OperatingSystem.IsLinux())
            {
                return await ListLinuxDevicesAsync(ct);
            }

            if (OperatingSystem.IsWindows())
            {
                return await ListDshowDevicesAsync(ct);
            }

            if (OperatingSystem.IsMacOS())
            {
                return await ListAvfoundationDevicesAsync(ct);
            }
        }
        catch (Exception)
        {
            // fehlendes pactl/ffmpeg o.ä. darf die App nicht zum Absturz bringen - einfach "keine
            // Geräte gefunden" anzeigen, s. IAudioRecorder.GetAvailableDevicesAsync-Doku.
        }

        return [];
    }

    /// <summary>PulseAudio/PipeWire-pulse (Standard auf praktisch jeder heutigen Linux-Distribution,
    /// auch dort, wo intern PipeWire statt "echtem" PulseAudio läuft) - `pactl list short sources`
    /// listet sowohl echte Eingänge als auch ".monitor"-Quellen (der Signalabgriff eines Ausgangs,
    /// z.B. um genau das aufzunehmen, was gerade über den Mixer-Ausgang läuft, ohne ein Kabel
    /// physisch zurückzuschleifen) - für einen Set-Recorder ist Letzteres oft genau der gewünschte Weg.</summary>
    private static async Task<IReadOnlyList<AudioCaptureDevice>> ListLinuxDevicesAsync(CancellationToken ct)
    {
        var output = await RunAndCaptureAsync("pactl", ["list", "short", "sources"], ct);
        var devices = new List<AudioCaptureDevice>();
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var columns = line.Split('\t');
            if (columns.Length < 2)
            {
                continue;
            }

            var name = columns[1];
            devices.Add(new AudioCaptureDevice(name, PrettifyLinuxDeviceName(name)));
        }

        return devices;
    }

    private static string PrettifyLinuxDeviceName(string rawName)
    {
        var isMonitor = rawName.EndsWith(".monitor", StringComparison.OrdinalIgnoreCase);
        var name = rawName
            .Replace(".monitor", "", StringComparison.OrdinalIgnoreCase)
            .Replace("alsa_input.", "", StringComparison.OrdinalIgnoreCase)
            .Replace("alsa_output.", "", StringComparison.OrdinalIgnoreCase)
            .Replace('_', ' ')
            .Replace('.', ' ')
            .Trim();
        return isMonitor ? $"{name} (Ausgang-Abgriff/Monitor)" : name;
    }

    /// <summary>DirectShow (Windows) - `ffmpeg -f dshow -list_devices true -i dummy` gibt die
    /// Geräteliste auf stderr aus und beendet sich mit Fehlercode (das ist bei dieser ffmpeg-Abfrage
    /// normal, kein echter Fehler).</summary>
    private static async Task<IReadOnlyList<AudioCaptureDevice>> ListDshowDevicesAsync(CancellationToken ct)
    {
        var stderr = await RunAndCaptureStdErrAsync("ffmpeg", ["-hide_banner", "-list_devices", "true", "-f", "dshow", "-i", "dummy"], ct);
        var devices = new List<AudioCaptureDevice>();
        var inAudioSection = false;
        foreach (var line in stderr.Split('\n'))
        {
            if (line.Contains("DirectShow audio devices", StringComparison.OrdinalIgnoreCase))
            {
                inAudioSection = true;
                continue;
            }

            if (line.Contains("DirectShow video devices", StringComparison.OrdinalIgnoreCase))
            {
                inAudioSection = false;
                continue;
            }

            if (!inAudioSection)
            {
                continue;
            }

            var match = Regex.Match(line, "\"([^\"]+)\"");
            if (match.Success && !line.Contains("Alternative name", StringComparison.OrdinalIgnoreCase))
            {
                var name = match.Groups[1].Value;
                devices.Add(new AudioCaptureDevice(name, name));
            }
        }

        return devices;
    }

    /// <summary>AVFoundation (macOS) - dieselbe "list_devices true" Konvention wie dshow, Geräte
    /// werden hier aber über einen numerischen Index statt eines Namens angesprochen.</summary>
    private static async Task<IReadOnlyList<AudioCaptureDevice>> ListAvfoundationDevicesAsync(CancellationToken ct)
    {
        var stderr = await RunAndCaptureStdErrAsync("ffmpeg", ["-hide_banner", "-f", "avfoundation", "-list_devices", "true", "-i", ""], ct);
        var devices = new List<AudioCaptureDevice>();
        var inAudioSection = false;
        foreach (var line in stderr.Split('\n'))
        {
            if (line.Contains("AVFoundation audio devices", StringComparison.OrdinalIgnoreCase))
            {
                inAudioSection = true;
                continue;
            }

            if (!inAudioSection)
            {
                continue;
            }

            var match = Regex.Match(line, @"\[(\d+)\]\s+(.+)$");
            if (match.Success)
            {
                var index = match.Groups[1].Value;
                var name = match.Groups[2].Value.Trim();
                devices.Add(new AudioCaptureDevice(index, name));
            }
        }

        return devices;
    }

    public async Task StartAsync(string deviceId, RecordingFormat format, string outputPath, CancellationToken ct = default)
    {
        if (IsRecording)
        {
            throw new InvalidOperationException("Es läuft bereits eine Aufnahme.");
        }

        var directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var args = new List<string> { "-y", "-loglevel", "error" };
        args.AddRange(BuildInputArgs(deviceId));
        args.AddRange(BuildCodecArgs(format));
        // Explizit statt aus der Dateiendung geraten: ffmpeg wählt den Container sonst über die
        // Endung von outputPath und bricht mit "Unable to choose an output format" ab, falls die
        // nicht zum gewählten Format passt (z.B. falsch/ohne Endung gespeichert) - live als echter
        // Fehlerfall aufgetreten, s. ARCHITECTURE.md.
        args.AddRange(["-f", GetFfmpegMuxerName(format)]);
        args.Add(outputPath);

        var startInfo = new ProcessStartInfo
        {
            FileName = "ffmpeg",
            RedirectStandardInput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        var stderrBuilder = new StringBuilder();
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                stderrBuilder.AppendLine(e.Data);
            }
        };

        process.Start();
        process.BeginErrorReadLine();

        // Kurz abwarten und prüfen, ob ffmpeg sofort wieder beendet ist (z.B. unbekanntes Gerät,
        // fehlende Rechte) - sonst würde "Aufnahme gestartet" angezeigt, obwohl gar nichts
        // aufgenommen wird. Bewusst KEIN zusätzlicher Dateigrößen-Check hier: FLAC puffert vor dem
        // ersten Flush auf die Platte länger als diese Wartezeit (live verifiziert - ein
        // Dateigrößen-Check hätte selbst funktionierende Aufnahmen fälschlich als fehlgeschlagen
        // gemeldet). Bekannte Einschränkung: ein unbekannter Pulse/PipeWire-Quellenname lässt ffmpeg
        // beobachtet nicht sofort fehlschlagen, sondern unbegrenzt hängen, ohne je Daten zu liefern -
        // das fängt dieser Check (noch) nicht ab, s. ARCHITECTURE.md.
        await Task.Delay(500, ct);
        if (process.HasExited)
        {
            var message = stderrBuilder.ToString().Trim();
            process.Dispose();
            throw new InvalidOperationException(string.IsNullOrEmpty(message)
                ? "ffmpeg wurde sofort wieder beendet (Gerät nicht verfügbar?)."
                : message);
        }

        _process = process;
        _stopwatch = Stopwatch.StartNew();
    }

    public async Task<TimeSpan> StopAsync(CancellationToken ct = default)
    {
        if (_process is not { HasExited: false } process)
        {
            return TimeSpan.Zero;
        }

        var elapsed = _stopwatch?.Elapsed ?? TimeSpan.Zero;
        try
        {
            // "q" ist ffmpegs eigene Tastatursteuerung zum geordneten Beenden - der Encoder finalisiert
            // dabei die Datei korrekt (Header/Trailer, s. FLAC-Rundlauf-Test in ARCHITECTURE.md), anders
            // als ein Kill(), das eine unvollständige/kaputte Datei hinterlassen würde.
            await process.StandardInput.WriteAsync("q");
            await process.StandardInput.FlushAsync(ct);

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(5));
            await process.WaitForExitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        finally
        {
            process.Dispose();
            _process = null;
            _stopwatch = null;
        }

        return elapsed;
    }

    private static IEnumerable<string> BuildInputArgs(string deviceId)
    {
        if (OperatingSystem.IsLinux())
        {
            return ["-f", "pulse", "-i", deviceId];
        }

        if (OperatingSystem.IsWindows())
        {
            return ["-f", "dshow", "-i", $"audio={deviceId}"];
        }

        if (OperatingSystem.IsMacOS())
        {
            return ["-f", "avfoundation", "-i", $":{deviceId}"];
        }

        throw new PlatformNotSupportedException("Aufnahme wird auf dieser Plattform nicht unterstützt.");
    }

    private static IEnumerable<string> BuildCodecArgs(RecordingFormat format) => format switch
    {
        RecordingFormat.Flac => ["-c:a", "flac"],
        RecordingFormat.Wav => ["-c:a", "pcm_s24le"],
        RecordingFormat.Mp3320 => ["-c:a", "libmp3lame", "-b:a", "320k"],
        _ => throw new ArgumentOutOfRangeException(nameof(format)),
    };

    private static string GetFfmpegMuxerName(RecordingFormat format) => format switch
    {
        RecordingFormat.Flac => "flac",
        RecordingFormat.Wav => "wav",
        RecordingFormat.Mp3320 => "mp3",
        _ => throw new ArgumentOutOfRangeException(nameof(format)),
    };

    /// <summary>Für den Speichern-Dialog im UI, damit der vorgeschlagene Dateiname immer zum
    /// gewählten Format passt.</summary>
    public static string GetFileExtension(RecordingFormat format) => format switch
    {
        RecordingFormat.Flac => "flac",
        RecordingFormat.Wav => "wav",
        RecordingFormat.Mp3320 => "mp3",
        _ => throw new ArgumentOutOfRangeException(nameof(format)),
    };

    private static async Task<string> RunAndCaptureAsync(string fileName, IReadOnlyList<string> args, CancellationToken ct)
    {
        var startInfo = new ProcessStartInfo { FileName = fileName, RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
        foreach (var arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException($"{fileName} konnte nicht gestartet werden.");
        var output = await process.StandardOutput.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);
        return output;
    }

    /// <summary>ffmpegs Geräte-Auflistung schreibt absichtlich auf stderr (stdout bleibt für die
    /// eigentlichen Mediendaten reserviert) und beendet sich dabei mit einem Nicht-Null-Exitcode -
    /// beides ist für diese spezielle Abfrage normal, deshalb wird der Exitcode hier bewusst
    /// ignoriert statt als Fehler behandelt.</summary>
    private static async Task<string> RunAndCaptureStdErrAsync(string fileName, IReadOnlyList<string> args, CancellationToken ct)
    {
        var startInfo = new ProcessStartInfo { FileName = fileName, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        foreach (var arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException($"{fileName} konnte nicht gestartet werden.");
        var output = await process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);
        return output;
    }
}
