using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace Cratebase.Server;

/// <summary>Only compressed derivatives, under Cratebase's own cache. No source writes or original fallback.</summary>
public sealed class StreamCache(string directory)
{
    private readonly SemaphoreSlim _encoder = new(1, 1);
    public static readonly int[] Bitrates = [96, 160, 256, 320];

    public async Task<string> PrepareAsync(string source, int bitrate, ServerSettings settings, CancellationToken ct)
    {
        if (!Bitrates.Contains(bitrate)) throw new ArgumentException("Qualität muss 96, 160, 256 oder 320 kbit/s sein.");
        var info = new FileInfo(source);
        if (!info.Exists) throw new FileNotFoundException("Datei nicht erreichbar.");
        // A single encoder also makes cleanup/publishing deterministic. Range readers share files for deletion.
        await _encoder.WaitAsync(ct);
        try
        {
            Directory.CreateDirectory(directory);
            var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{info.FullName}|{info.Length}|{info.LastWriteTimeUtc.Ticks}|{bitrate}|mp3-v1")));
            var output = Path.Combine(directory, key + ".mp3");
            if (File.Exists(output)) { File.SetLastWriteTimeUtc(output, DateTime.UtcNow); return output; }
            var limit = settings.CacheMegabytes * 1024L * 1024;
            foreach (var old in new DirectoryInfo(directory).EnumerateFiles("*.part")) { try { old.Delete(); } catch (IOException) { } }
            var part = output + ".part";
            var start = new ProcessStartInfo(settings.FfmpegPath) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
            foreach (var arg in new[] { "-nostdin", "-v", "error", "-y", "-i", source, "-map", "0:a:0", "-vn", "-sn", "-dn", "-map_metadata", "-1", "-ac", "2", "-ar", "44100", "-c:a", "libmp3lame", "-b:a", bitrate + "k", "-fs", (limit+65536).ToString(System.Globalization.CultureInfo.InvariantCulture), "-f", "mp3", part }) start.ArgumentList.Add(arg);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromMinutes(10));
            using var process = Process.Start(start) ?? throw new IOException("FFmpeg konnte nicht gestartet werden.");
            using var cancel = timeout.Token.Register(() => { try { if (!process.HasExited) process.Kill(true); } catch (InvalidOperationException) { } });
            var error = process.StandardError.ReadToEndAsync();
            var stdout = process.StandardOutput.ReadToEndAsync();
            try
            {
                await process.WaitForExitAsync(timeout.Token); await stdout;
                var diagnostic = await error;
                if (process.ExitCode != 0) throw new IOException("Streaming-Encoder fehlgeschlagen. FFmpeg mit libmp3lame erforderlich. " + diagnostic[..Math.Min(diagnostic.Length, 300)]);
                if (new FileInfo(part).Length > limit) throw new IOException("Dieser Track überschreitet den Streaming-Cache. Cache in den Server-Einstellungen vergrößern.");
                var files = new DirectoryInfo(directory).EnumerateFiles("*.mp3").OrderBy(f => f.LastWriteTimeUtc).ToList();
                var size = files.Sum(f => f.Length) + new FileInfo(part).Length;
                foreach (var file in files) { if (size <= limit) break; try { var bytes = file.Length; file.Delete(); size -= bytes; } catch (IOException) { } }
                if (size > limit) throw new IOException("Streaming-Cache ist belegt. Später erneut versuchen.");
                File.Move(part, output); return output;
            }
            finally
            {
                if (!process.HasExited) { process.Kill(true); await process.WaitForExitAsync(CancellationToken.None); }
                if (File.Exists(part)) File.Delete(part);
            }
        }
        finally { _encoder.Release(); }
    }
}
