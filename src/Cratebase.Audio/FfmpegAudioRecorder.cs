using System.Diagnostics;
using System.Globalization;
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
    public string ExecutablePath { get; set; } = "ffmpeg";
    private Process? _process;
    private Stopwatch? _stopwatch;
    private CancellationTokenSource? _levelMonitorCts;
    private string? _levelFilePath;
    private volatile AudioLevel? _currentLevel;

    public bool IsRecording => _process is { HasExited: false };

    public AudioLevel? CurrentLevel => _currentLevel;

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
    private async Task<IReadOnlyList<AudioCaptureDevice>> ListDshowDevicesAsync(CancellationToken ct)
    {
        var stderr = await RunAndCaptureStdErrAsync(ExecutablePath, ["-hide_banner", "-list_devices", "true", "-f", "dshow", "-i", "dummy"], ct);
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
    private async Task<IReadOnlyList<AudioCaptureDevice>> ListAvfoundationDevicesAsync(CancellationToken ct)
    {
        var stderr = await RunAndCaptureStdErrAsync(ExecutablePath, ["-hide_banner", "-f", "avfoundation", "-list_devices", "true", "-i", ""], ct);
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

        var baseArgs = new List<string> { "-y", "-loglevel", "error" };
        baseArgs.AddRange(BuildInputArgs(deviceId));

        // Live-Pegelanzeige: astats/ametadata sind reine Passthrough-Filter (verändern die
        // Samples nicht) und laufen deshalb im selben -af wie die eigentliche Aufnahme mit, statt
        // über einen separaten asplit-Zweig - kein Qualitätsverlust, kein zweiter ffmpeg-Prozess.
        // asetnsamples erzwingt eine feste Blockgröße (~100ms bei 48kHz) für einen gleichmäßigen
        // Anzeige-Takt unabhängig von ffmpegs internem Puffer-Chunking.
        var levelFilePath = Path.Combine(Path.GetTempPath(), $"cratebase-levels-{Guid.NewGuid():N}.txt");
        var meteringArgs = new List<string>
        {
            "-af",
            $"asetnsamples=n=4800,astats=metadata=1:reset=1:measure_perchannel=Peak_level:measure_overall=none,ametadata=print:file={EscapeFilterGraphValue(levelFilePath)}",
        };

        var tailArgs = new List<string>();
        tailArgs.AddRange(BuildCodecArgs(format));
        // Explizit statt aus der Dateiendung geraten: ffmpeg wählt den Container sonst über die
        // Endung von outputPath und bricht mit "Unable to choose an output format" ab, falls die
        // nicht zum gewählten Format passt (z.B. falsch/ohne Endung gespeichert) - live als echter
        // Fehlerfall aufgetreten, s. ARCHITECTURE.md.
        tailArgs.AddRange(["-f", GetFfmpegMuxerName(format)]);
        tailArgs.Add(outputPath);

        var withMetering = new List<string>(baseArgs);
        withMetering.AddRange(meteringArgs);
        withMetering.AddRange(tailArgs);

        var (process, stderr) = await TryLaunchAsync(withMetering, ct);
        var meteringActive = process is not null;

        if (process is null)
        {
            // Pegelmessung darf die eigentliche Aufnahme nie verhindern (z.B. ein exotischer Pfad,
            // den die Filtergraph-Eskapierung nicht abdeckt) - degradiert still auf "kein Meter"
            // statt die Aufnahme komplett scheitern zu lassen, s. IAudioRecorder.CurrentLevel-Doku.
            var withoutMetering = new List<string>(baseArgs);
            withoutMetering.AddRange(tailArgs);
            (process, stderr) = await TryLaunchAsync(withoutMetering, ct);
            meteringActive = false;
        }

        if (process is null)
        {
            throw new InvalidOperationException(string.IsNullOrEmpty(stderr)
                ? "ffmpeg wurde sofort wieder beendet (Gerät nicht verfügbar?)."
                : stderr);
        }

        _process = process;
        _stopwatch = Stopwatch.StartNew();

        if (meteringActive)
        {
            _levelFilePath = levelFilePath;
            _levelMonitorCts = new CancellationTokenSource();
            _ = MonitorLevelFileAsync(levelFilePath, _levelMonitorCts.Token);
        }
        else
        {
            _levelFilePath = null;
        }
    }

    /// <summary>Startet ffmpeg mit den gegebenen Argumenten und prüft nach kurzer Anlaufzeit, ob es
    /// sofort wieder beendet wurde (z.B. unbekanntes Gerät, fehlende Rechte, oder - bei einem
    /// Filtergraph-Fehler in den Metering-Args - ein Eskapierungsproblem im Pegel-Dateipfad). Liefert
    /// (null, stderr) im Fehlerfall statt zu werfen, damit StartAsync bei Bedarf einen Fallback
    /// versuchen kann. Bewusst KEIN zusätzlicher Dateigrößen-Check hier: FLAC puffert vor dem ersten
    /// Flush auf die Platte länger als diese Wartezeit (live verifiziert - ein Dateigrößen-Check hätte
    /// selbst funktionierende Aufnahmen fälschlich als fehlgeschlagen gemeldet). Bekannte
    /// Einschränkung: ein unbekannter Pulse/PipeWire-Quellenname lässt ffmpeg beobachtet nicht sofort
    /// fehlschlagen, sondern unbegrenzt hängen, ohne je Daten zu liefern - das fängt dieser Check
    /// (noch) nicht ab, s. ARCHITECTURE.md.</summary>
    private async Task<(Process? Process, string Stderr)> TryLaunchAsync(IReadOnlyList<string> args, CancellationToken ct)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = ExecutablePath,
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

        await Task.Delay(500, ct);
        if (process.HasExited)
        {
            var message = stderrBuilder.ToString().Trim();
            process.Dispose();
            return (null, message);
        }

        return (process, "");
    }

    /// <summary>Liest periodisch die letzten Bytes der Pegel-Metadatendatei (s. StartAsync) und
    /// extrahiert den jeweils neuesten Peak_level je Kanal. Liest immer nur ein festes Fenster vom
    /// Dateiende statt eine Leseposition fortzuschreiben - einfacher und robuster als
    /// Offset-Tracking (kein Risiko von durch StreamReader-Pufferung verfälschten Positionen), auf
    /// Kosten eines minimal höheren, aber konstanten Lese-Overheads pro Tick.</summary>
    private async Task MonitorLevelFileAsync(string levelFilePath, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (File.Exists(levelFilePath))
                {
                    using var stream = new FileStream(levelFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    var start = Math.Max(0, stream.Length - LevelFileTailReadBytes);
                    stream.Seek(start, SeekOrigin.Begin);
                    using var reader = new StreamReader(stream);
                    var tail = await reader.ReadToEndAsync(ct);

                    var peaksByChannel = new SortedDictionary<int, double>();
                    foreach (Match match in ChannelPeakRegex.Matches(tail))
                    {
                        var channel = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
                        peaksByChannel[channel] = ParsePeakDb(match.Groups[2].Value.Trim());
                    }

                    if (peaksByChannel.Count > 0)
                    {
                        _currentLevel = new AudioLevel(peaksByChannel.Values.ToList());
                    }
                }
            }
            catch (IOException)
            {
                // Datei wird evtl. gerade von ffmpeg geschrieben/gesperrt - beim nächsten Tick erneut versuchen.
            }
            catch (OperationCanceledException)
            {
                return;
            }

            try
            {
                await Task.Delay(120, ct);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private static double ParsePeakDb(string raw) => raw switch
    {
        "-inf" => -100.0,
        "inf" => 0.0,
        _ => double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : -100.0,
    };

    /// <summary>Ffmpegs Filtergraph-Syntax kennt zwei ineinander geschachtelte Escape-Ebenen (die
    /// gesamte Filterketten-Beschreibung, und darin einzelne Options-Werte) - live gegen echtes
    /// ffmpeg verifiziert: reservierte Zeichen (u.a. ':' als Options-Trenner, ',' als Filter-Trenner,
    /// Leerzeichen) brauchen dafür tatsächlich EINEN DOPPELTEN Backslash, ein einfacher reicht nicht
    /// und ein einfaches Anführungszeichen-Quoting funktioniert für Pfade mit ':' ebenfalls nicht
    /// zuverlässig. Deckt das trotzdem nicht jeden denkbaren Pfad ab (s. StartAsync-Fallback).</summary>
    private static string EscapeFilterGraphValue(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            if (c is '\\' or '\'' or ':' or ',' or '[' or ']' or ';' or '=' or ' ')
            {
                builder.Append("\\\\");
            }

            builder.Append(c);
        }

        return builder.ToString();
    }

    private const int LevelFileTailReadBytes = 4096;

    private static readonly Regex ChannelPeakRegex = new(@"lavfi\.astats\.(\d+)\.Peak_level=(-?[\d.]+|-inf|inf)", RegexOptions.Compiled);

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

            _levelMonitorCts?.Cancel();
            _levelMonitorCts?.Dispose();
            _levelMonitorCts = null;
            _currentLevel = null;

            if (_levelFilePath is not null)
            {
                try
                {
                    File.Delete(_levelFilePath);
                }
                catch (IOException)
                {
                    // Aufräumen ist bloße Kosmetik (Temp-Verzeichnis) - ein Fehlschlag hier darf das
                    // Beenden der eigentlichen Aufnahme nicht stören.
                }

                _levelFilePath = null;
            }
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
