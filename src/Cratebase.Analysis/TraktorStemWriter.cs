using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using Cratebase.Core.Models;

namespace Cratebase.Analysis;

/// <summary>Classic NI Stem container: master + drums/bass/other/vocals AAC stereo, version 1 manifest.</summary>
public sealed class TraktorStemWriter(string ffmpeg)
{
    public async Task WriteAsync(string master,IReadOnlyList<string> stems,Track track,string destination,CancellationToken ct)
    {
        if(stems.Count!=4)throw new ArgumentException("Vier getrennte Stems erforderlich: Drums, Bass, Other, Vocals.");
        if(!destination.EndsWith(".stem.mp4",StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Zieldatei muss auf .stem.mp4 enden.");
        if(File.Exists(destination))throw new IOException("Stem-Zieldatei existiert bereits; sie wird nicht überschrieben.");
        var temporary=destination+"."+Guid.NewGuid().ToString("N")+".partial.mp4";
        try
        {
            var args=new List<string>{"-nostdin","-v","error","-n"};
            foreach(var input in new[]{master}.Concat(stems)){args.Add("-i");args.Add(input);}
            var names=new[]{"Master","Drums","Bass","Other","Vocals"};
            for(var i=0;i<5;i++)args.AddRange(["-map",$"{i}:a:0",$"-metadata:s:a:{i}","handler_name="+names[i],$"-disposition:a:{i}",i==0?"default":"0"]);
            args.AddRange(["-c:a","aac","-b:a","256k","-ar","44100","-ac","2","-brand","M4A ","-skip_iods","1","-metadata","title="+track.DisplayTitle,"-metadata","artist="+track.DisplayArtist,"-metadata","genre="+(track.GenreRaw??""),"-f","mp4",temporary]);
            await TransitionRenderer.RunAsync(ffmpeg,args,ct);
            await AddManifestAsync(temporary,ct);Validate(temporary);ct.ThrowIfCancellationRequested();File.Move(temporary,destination,false);
        }
        finally {if(File.Exists(temporary))File.Delete(temporary);}
    }
    private static byte[] Box(string type,byte[] data)
    {var box=new byte[data.Length+8];BinaryPrimitives.WriteUInt32BigEndian(box,(uint)box.Length);Encoding.ASCII.GetBytes(type).CopyTo(box,4);data.CopyTo(box,8);return box;}
    private sealed record Atom(long Position,long Size,string Type,int Header);
    private static IEnumerable<Atom> Atoms(Stream stream,long start,long end)
    {
        var header=new byte[16];
        while(start<end)
        {
            stream.Position=start;stream.ReadExactly(header.AsSpan(0,8));long size=BinaryPrimitives.ReadUInt32BigEndian(header);var count=8;
            if(size==1){stream.ReadExactly(header.AsSpan(8,8));size=checked((long)BinaryPrimitives.ReadUInt64BigEndian(header.AsSpan(8)));count=16;}
            if(size==0)size=end-start;
            if(size<count||size>end-start)throw new IOException("Ungültige MP4-Box.");
            yield return new(start,size,Encoding.ASCII.GetString(header,4,4),count);start+=size;
        }
    }
    private static async Task AddManifestAsync(string path,CancellationToken ct)
    {
        await using var stream=new FileStream(path,FileMode.Open,FileAccess.ReadWrite,FileShare.None,65536,true);
        var root=Atoms(stream,0,stream.Length).ToArray();var moov=root.Single(a=>a.Type=="moov");
        // Growing a final moov keeps every media chunk offset valid, even for large source audio.
        if(moov.Position+moov.Size!=stream.Length||moov.Header!=8||moov.Size>16_000_000)throw new IOException("Unerwartetes MP4-Layout; keine unsichere Box-Verschiebung vorgenommen.");
        var children=Atoms(stream,moov.Position+8,moov.Position+moov.Size).ToArray();var tracks=children.Where(a=>a.Type=="trak").ToArray();
        if(tracks.Length!=5)throw new IOException("Stem-Datei benötigt genau fünf Audiotracks.");
        for(var i=0;i<tracks.Length;i++)
        {
            var tkhd=Atoms(stream,tracks[i].Position+tracks[i].Header,tracks[i].Position+tracks[i].Size).Single(a=>a.Type=="tkhd");
            stream.Position=tkhd.Position+tkhd.Header;var version=stream.ReadByte();
            // Only the master is enabled in ordinary MP4 players; Traktor addresses the four stem streams.
            var flags=new byte[]{0,0,(byte)(i==0?3:2)};await stream.WriteAsync(flags,ct);
            stream.Position=tkhd.Position+(version==1?54:42);await stream.WriteAsync(new byte[2],ct); // no alternate track group
        }
        var manifest=JsonSerializer.SerializeToUtf8Bytes(new {version=1,stems=new[]{new {name="Drums",color="#FF604A"},new {name="Bass",color="#FFD54A"},new {name="Other",color="#33DDAA"},new {name="Vocals",color="#AA77FF"}},mastering_dsp=new {compressor=new {enabled=false,ratio=3,threshold=0,attack=.003,release=.3,input_gain=0,output_gain=0,hp_cutoff=300,dry_wet=50},limiter=new {enabled=false,threshold=0,ceiling=0,release=.05}}});
        var stem=Box("stem",manifest);var udta=children.FirstOrDefault(a=>a.Type=="udta");
        if(udta!=null)
        {
            if(udta.Position+udta.Size!=moov.Position+moov.Size||udta.Header!=8)throw new IOException("Unerwartete MP4-Metadatenposition.");
            stream.Position=stream.Length;await stream.WriteAsync(stem,ct);stream.Position=udta.Position;var size=new byte[4];BinaryPrimitives.WriteUInt32BigEndian(size,checked((uint)(udta.Size+stem.Length)));await stream.WriteAsync(size,ct);
        }
        else {stem=Box("udta",stem);stream.Position=stream.Length;await stream.WriteAsync(stem,ct);}
        stream.Position=moov.Position;var moovSize=new byte[4];BinaryPrimitives.WriteUInt32BigEndian(moovSize,checked((uint)(moov.Size+stem.Length)));await stream.WriteAsync(moovSize,ct);
    }
    public static void Validate(string path)
    {
        using var stream=File.OpenRead(path);var root=Atoms(stream,0,stream.Length).ToArray();var moov=root.Single(a=>a.Type=="moov");
        var children=Atoms(stream,moov.Position+moov.Header,moov.Position+moov.Size).ToArray();
        if(children.Count(a=>a.Type=="trak")!=5)throw new IOException("Fünf Stem-Audiotracks erwartet.");
        var udta=children.Single(a=>a.Type=="udta");var atom=Atoms(stream,udta.Position+udta.Header,udta.Position+udta.Size).Single(a=>a.Type=="stem");
        if(atom.Size>100000)throw new IOException("Stem-Metadaten zu groß.");var data=new byte[checked((int)(atom.Size-atom.Header))];stream.Position=atom.Position+atom.Header;stream.ReadExactly(data);
        using var json=JsonDocument.Parse(data);if(json.RootElement.GetProperty("stems").GetArrayLength()!=4||json.RootElement.GetProperty("version").GetInt32()!=1)throw new IOException("Ungültiges Stem-Manifest.");
    }
}
