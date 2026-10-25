using System.Security.Cryptography;
using System.Text;
using Cratebase.Core.Abstractions;
using Cratebase.Core.Models;

namespace Cratebase.Analysis;

/// <summary>Waveform-Cache als eigene Binärdatei pro Track (s. IWaveformCache-Doku), nicht als DB-Blob.</summary>
public sealed class FileWaveformCache : IWaveformCache
{
    private const int Magic = 0x43574632; // CWF2: validated RGB peaks, new generation algorithm
    private readonly string _cacheDir;

    public FileWaveformCache(string cacheDir)
    {
        _cacheDir = cacheDir;
        Directory.CreateDirectory(cacheDir);
    }

    public async Task<WaveformData?> TryLoadAsync(Track track, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
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
            if(count<=0||count>8192||stream.Length!=8L+count*12L) return null;
            var columns = new WaveformColumn[count];
            for (var i = 0; i < count; i++)
            {
                columns[i] = new WaveformColumn(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                if(!Valid(columns[i].Low)||!Valid(columns[i].Mid)||!Valid(columns[i].High)) return null;
            }

            return new WaveformData { Columns = columns };
        }
        catch (IOException)
        {
            // Kaputte/unvollständige Cache-Datei (z.B. Absturz mitten im Schreiben) - einfach neu generieren lassen.
            return null;
        }
        catch(UnauthorizedAccessException) { return null; }
    }

    public async Task SaveAsync(Track track, WaveformData data, CancellationToken ct = default)
    {
        var path = GetCachePath(track);
        if(data.Origin!=WaveformOrigin.GeneratedRgb||data.Columns.Count is <=0 or >8192) throw new ArgumentException("Nur generierte RGB-Peaks gehören in den Cratebase-Cache.");
        var temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try
        {
            await using(var stream=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None))
            using(var writer=new BinaryWriter(stream))
            {
                writer.Write(Magic); writer.Write(data.Columns.Count);
                foreach(var column in data.Columns)
                {
                    ct.ThrowIfCancellationRequested();
                    if(!Valid(column.Low)||!Valid(column.Mid)||!Valid(column.High)) throw new ArgumentException("Ungültige RGB-Peaks.");
                    writer.Write(column.Low); writer.Write(column.Mid); writer.Write(column.High);
                }
            }
            ct.ThrowIfCancellationRequested(); File.Move(temporary,path,true);
        }
        finally { if(File.Exists(temporary)) File.Delete(temporary); }
    }

    /// <summary>Cache-Key aus Track-Id + Dateigröße + mtime (s. Interface-Doku zu den Grenzen davon).</summary>
    private string GetCachePath(Track track)
    {
        var key = $"rgb2:{track.Id}:{track.LibraryRootId}:{track.RelativePath}:{track.FileSizeBytes}:{track.FileModifiedAtUtc.Ticks}";
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..24].ToLowerInvariant();
        return Path.Combine(_cacheDir, hash + ".cwf");
    }
    private static bool Valid(float value)=>float.IsFinite(value)&&value>=0&&value<=1;
}
