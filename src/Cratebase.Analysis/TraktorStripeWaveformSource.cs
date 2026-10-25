using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using Cratebase.Core.Abstractions;
using Cratebase.Core.Models;

namespace Cratebase.Analysis;

/// <summary>Read-only decoding of legacy PRTS textures and Traktor 4 spectrum stripe archives.</summary>
public sealed class TraktorStripeWaveformSource(ITraktorAnalysisLookup lookup) : ITrackWaveformSource
{
    private const int MaxFileSize=16*1024*1024;
    public async Task<WaveformData?> TryLoadAsync(Track track,CancellationToken ct=default)
    {
        var reference=await lookup.FindAsync(track,ct);
        if(reference==null || TraktorStripeName.FromAudioId(reference.AudioId) is not { } name) return null;
        foreach(var folder in CandidateFolders(reference.CollectionDirectory))
        foreach(var suffix in new[] { ".stripe","" })
        {
            ct.ThrowIfCancellationRequested();
            var path=Path.Combine(folder,"Stripes",name+suffix);
            try
            {
                var info=new FileInfo(path);
                if(!info.Exists||info.Length<25||info.Length>MaxFileSize) continue;
                // FileShare permits Traktor to update its cache. Incomplete reads simply fall back.
                await using var file=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);
                using var content=new MemoryStream(); await file.CopyToAsync(content,ct);
                var result=Decode(content.ToArray());
                if(result?.HasData==true) return result;
            }
            catch(IOException) { }
            catch(UnauthorizedAccessException) { }
        }
        return null;
    }

    private static IEnumerable<string> CandidateFolders(string collectionDirectory)
    {
        var paths=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for(var dir=new DirectoryInfo(collectionDirectory);dir!=null&&paths.Count<5;dir=dir.Parent) paths.Add(dir.FullName);
        var native=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),"Native Instruments");
        if(Directory.Exists(native))
        {
            string[] installed;
            try { installed=Directory.GetDirectories(native,"Traktor*"); } catch(IOException) { installed=[]; } catch(UnauthorizedAccessException) { installed=[]; }
            foreach(var path in installed.OrderDescending()) paths.Add(path);
        }
        return paths;
    }

    public static WaveformData? Decode(byte[] bytes)
    {
        if(bytes.Length<25||bytes.Length>MaxFileSize) return null;
        if(bytes[0]==6&&bytes.AsSpan(1,4).SequenceEqual("PRTS"u8))
        {
            if(BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(5,4))!=2) return null;
            var width=BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(13,4));
            var height=BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(17,4));
            if(width<=0||height<=0||width>8192||height>1024||(long)width*height*4!=bytes.Length-25) return null;
            var pixels=new byte[width*height*4];
            // The legacy texture is column-major, not a row-major bitmap or PCM array.
            for(var x=0;x<width;x++) for(var y=0;y<height;y++)
                bytes.AsSpan(25+(x*height+y)*4,4).CopyTo(pixels.AsSpan((y*width+x)*4,4));
            return new() { Origin=WaveformOrigin.TraktorStripe,Raster=new(width,height,pixels) };
        }
        if(!bytes.AsSpan(0,Math.Min(bytes.Length,30)).StartsWith("22 serialization::archive "u8)) return null;
        var tokens=Encoding.ASCII.GetString(bytes).Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries);
        // Recognized spectrum archive layout; do not guess unknown Boost layouts.
        if(tokens.Length<22||tokens[7]!="5") return null;
        var cursor=12;
        // Six optional spectrum-analysis values are encoded as presence flag + value.
        // Their presence changes the payload offset, even within the same Traktor version.
        for(var optional=0;optional<6;optional++)
        {
            if(cursor>=tokens.Length) return null;
            var flag=tokens[cursor++];
            if(flag=="0") continue;
            if(flag!="1"||cursor>=tokens.Length||!double.TryParse(tokens[cursor++],NumberStyles.Float,CultureInfo.InvariantCulture,out var value)||!double.IsFinite(value)) return null;
        }
        if(cursor+4>tokens.Length||!int.TryParse(tokens[cursor++],NumberStyles.None,CultureInfo.InvariantCulture,out var count)||count<=0||count>1_000_000) return null;
        if(tokens[cursor++]!="1"||tokens[cursor++]!="0"||tokens[cursor++]!="1"||(long)count*4+cursor!=tokens.Length) return null;
        var points=new WaveformColumn[count];
        Span<float> bands=stackalloc float[4];
        for(var i=0;i<count;i++)
        {
            for(var j=0;j<4;j++)
            {
                if(!ushort.TryParse(tokens[cursor+i*4+j],NumberStyles.None,CultureInfo.InvariantCulture,out var amplitude)) return null;
                bands[j]=Math.Clamp(amplitude/32768f,0,1);
            }
            points[i]=new(bands[0],bands[1],bands[2]);
        }
        return new() { Origin=WaveformOrigin.TraktorStripe,Columns=points };
    }
}
