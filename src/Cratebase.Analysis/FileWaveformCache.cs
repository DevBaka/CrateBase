using System.Security.Cryptography;
using System.Text;
using Cratebase.Core.Abstractions;
using Cratebase.Core.Models;

namespace Cratebase.Analysis;

/// <summary>Waveform-Cache als eigene Binärdatei pro Track (s. IWaveformCache-Doku), nicht als DB-Blob.</summary>
public sealed class FileWaveformCache : IWaveformCache
{
    private const int Magic = 0x43574631; // "CWF1"
    private readonly string _cacheDir;

    public FileWaveformCache(string cacheDir)
    {
        _cacheDir = cacheDir;
        Directory.CreateDirectory(cacheDir);
    }

    public async Task<WaveformData?> TryLoadAsync(Track track, CancellationToken ct = default)
    {
        var path = GetCachePath(track);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            await using var stream = File.OpenRead(path);
            using var reader = new BinaryReader(stream);
            if (reader.ReadInt32() != Magic)
            {
                return null;
            }

            var count = reader.ReadInt32();
            var columns = new WaveformColumn[count];
            for (var i = 0; i < count; i++)
            {
                columns[i] = new WaveformColumn(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
            }

            return new WaveformData { Columns = columns };
        }
        catch (Exception)
        {
            // Kaputte/unvollständige Cache-Datei (z.B. Absturz mitten im Schreiben) - einfach neu generieren lassen.
            return null;
        }
    }

    public async Task SaveAsync(Track track, WaveformData data, CancellationToken ct = default)
    {
        var path = GetCachePath(track);
        await using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream);
        writer.Write(Magic);
        writer.Write(data.Columns.Count);
        foreach (var column in data.Columns)
        {
            writer.Write(column.Low);
            writer.Write(column.Mid);
            writer.Write(column.High);
        }
    }

    /// <summary>Cache-Key aus Track-Id + Dateigröße + mtime (s. Interface-Doku zu den Grenzen davon).</summary>
    private string GetCachePath(Track track)
    {
        var key = $"{track.Id}:{track.FileSizeBytes}:{track.FileModifiedAtUtc.Ticks}";
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..24].ToLowerInvariant();
        return Path.Combine(_cacheDir, hash + ".cwf");
    }
}
