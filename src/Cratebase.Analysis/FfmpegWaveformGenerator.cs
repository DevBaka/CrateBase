using System.Diagnostics;
using Cratebase.Core.Abstractions;
using Cratebase.Core.Models;

namespace Cratebase.Analysis;

/// <summary>
/// Dekodiert per externem ffmpeg-Prozess zu rohem Mono-PCM und berechnet daraus eine
/// 3-Band-Waveform (Bass/Mitten/Höhen als einfache IIR-Tief-/Hoch-/Bandpassfilter, keine FFT -
/// bewusst leichtgewichtig statt eines echten Spektrogramms, s. WaveformData-Doku).
///
/// Bekannte Voraussetzung: ffmpeg muss im PATH installiert sein. Fehlt es, oder scheitert die
/// Dekodierung (kaputte/exotische Datei), liefert GenerateAsync null statt zu werfen - ein
/// fehlendes ffmpeg darf nicht die ganze App blockieren, nur die Waveform-Anzeige bleibt leer.
/// </summary>
public sealed class FfmpegWaveformGenerator : IWaveformGenerator
{
    private const int SampleRateHz = 11025;
    private const int ColumnCount = 600;
    private const double LowCutoffHz = 200;
    private const double HighCutoffHz = 4000;

    public async Task<WaveformData?> GenerateAsync(string absoluteFilePath, CancellationToken ct = default)
    {
        byte[] pcmBytes;
        try
        {
            pcmBytes = await DecodeToPcmAsync(absoluteFilePath, ct);
        }
        catch (Exception)
        {
            // Deckt sowohl "ffmpeg nicht gefunden" (Win32Exception) als auch Dekodierfehler ab -
            // beides führt zum selben, harmlosen Ergebnis: keine Waveform statt Absturz.
            return null;
        }

        if (pcmBytes.Length < 8)
        {
            return null;
        }

        var samples = BytesToFloats(pcmBytes);
        var low = LowPass(samples, LowCutoffHz);
        var mid = HighPass(LowPass(samples, HighCutoffHz), LowCutoffHz);
        var high = HighPass(samples, HighCutoffHz);

        return new WaveformData { Columns = BuildColumns(low, mid, high) };
    }

    private static async Task<byte[]> DecodeToPcmAsync(string absoluteFilePath, CancellationToken ct)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "ffmpeg",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("-v");
        startInfo.ArgumentList.Add("error");
        startInfo.ArgumentList.Add("-i");
        startInfo.ArgumentList.Add(absoluteFilePath);
        startInfo.ArgumentList.Add("-f");
        startInfo.ArgumentList.Add("f32le");
        startInfo.ArgumentList.Add("-ac");
        startInfo.ArgumentList.Add("1");
        startInfo.ArgumentList.Add("-ar");
        startInfo.ArgumentList.Add(SampleRateHz.ToString());
        startInfo.ArgumentList.Add("-");

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("ffmpeg konnte nicht gestartet werden.");

        var stdoutTask = ReadAllBytesAsync(process.StandardOutput.BaseStream, ct);
        var stderrTask = process.StandardError.ReadToEndAsync(ct); // muss mitgelesen werden, sonst blockiert ffmpeg bei viel stderr-Output

        await Task.WhenAll(stdoutTask, process.WaitForExitAsync(ct), stderrTask);

        if (process.ExitCode != 0)
        {
            return [];
        }

        return await stdoutTask;
    }

    private static async Task<byte[]> ReadAllBytesAsync(Stream stream, CancellationToken ct)
    {
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms, ct);
        return ms.ToArray();
    }

    private static float[] BytesToFloats(byte[] bytes)
    {
        var count = bytes.Length / 4;
        var samples = new float[count];
        Buffer.BlockCopy(bytes, 0, samples, 0, count * 4);
        return samples;
    }

    /// <summary>Einfacher Einpol-Tiefpass (RC-Glied), sample-für-sample.</summary>
    private static float[] LowPass(float[] x, double cutoffHz)
    {
        var y = new float[x.Length];
        var dt = 1.0 / SampleRateHz;
        var rc = 1.0 / (2 * Math.PI * cutoffHz);
        var alpha = (float)(dt / (rc + dt));
        float prev = 0;
        for (var i = 0; i < x.Length; i++)
        {
            prev += alpha * (x[i] - prev);
            y[i] = prev;
        }

        return y;
    }

    /// <summary>Einfacher Einpol-Hochpass (RC-Glied), sample-für-sample.</summary>
    private static float[] HighPass(float[] x, double cutoffHz)
    {
        var y = new float[x.Length];
        var dt = 1.0 / SampleRateHz;
        var rc = 1.0 / (2 * Math.PI * cutoffHz);
        var alpha = (float)(rc / (rc + dt));
        float prevY = 0;
        var prevX = x.Length > 0 ? x[0] : 0f;
        for (var i = 0; i < x.Length; i++)
        {
            var cur = alpha * (prevY + x[i] - prevX);
            prevY = cur;
            prevX = x[i];
            y[i] = cur;
        }

        return y;
    }

    private static WaveformColumn[] BuildColumns(float[] low, float[] mid, float[] high)
    {
        var lows = new float[ColumnCount];
        var mids = new float[ColumnCount];
        var highs = new float[ColumnCount];
        var windowSize = Math.Max(1, low.Length / ColumnCount);
        var globalMax = 1e-6f;

        for (var column = 0; column < ColumnCount; column++)
        {
            var start = column * windowSize;
            var end = Math.Min(low.Length, start + windowSize);
            float lowPeak = 0, midPeak = 0, highPeak = 0;
            for (var i = start; i < end; i++)
            {
                lowPeak = Math.Max(lowPeak, Math.Abs(low[i]));
                midPeak = Math.Max(midPeak, Math.Abs(mid[i]));
                highPeak = Math.Max(highPeak, Math.Abs(high[i]));
            }

            lows[column] = lowPeak;
            mids[column] = midPeak;
            highs[column] = highPeak;
            globalMax = Math.Max(globalMax, Math.Max(lowPeak, Math.Max(midPeak, highPeak)));
        }

        var columns = new WaveformColumn[ColumnCount];
        for (var column = 0; column < ColumnCount; column++)
        {
            columns[column] = new WaveformColumn(lows[column] / globalMax, mids[column] / globalMax, highs[column] / globalMax);
        }

        return columns;
    }
}
