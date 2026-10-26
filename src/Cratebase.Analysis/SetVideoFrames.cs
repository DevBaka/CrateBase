using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Cratebase.Analysis;

public sealed record SetVideoFrame(string Path,double Seconds,double Score,string Source,bool AiGenerated=false,string? OriginalPath=null);

/// <summary>Bounded samples over the whole recording, no full video decode or source changes.</summary>
public sealed class SetVideoFrames(string ffmpeg)
{
    public async Task<string> AdjustImageAsync(string source,string folder,double brightness,double contrast,double saturation,double gamma,CancellationToken ct)
    {
        if(!File.Exists(source))throw new FileNotFoundException("Bild nicht erreichbar.",source);
        if(!double.IsFinite(brightness)||!double.IsFinite(contrast)||!double.IsFinite(saturation)||!double.IsFinite(gamma))throw new ArgumentException("Ungültige Bildkorrektur.");
        Directory.CreateDirectory(folder);var output=Path.Combine(folder,Guid.NewGuid().ToString("N")+".png");
        string N(double value)=>value.ToString("0.###",CultureInfo.InvariantCulture);
        try{await CaptureAsync(["-nostdin","-v","error","-i",source,"-vf",$"eq=brightness={N(Math.Clamp(brightness,-.25,.25))}:contrast={N(Math.Clamp(contrast,.5,1.8))}:saturation={N(Math.Clamp(saturation,0,2.5))}:gamma={N(Math.Clamp(gamma,.6,1.8))}","-frames:v","1","-update","1",output],ct);return output;}
        catch{if(File.Exists(output))File.Delete(output);throw;}
    }
    public async Task<double> DurationAsync(string source,CancellationToken ct)
    {
        if(!File.Exists(source))throw new FileNotFoundException("Video nicht erreichbar.",source);
        var result=await CaptureAsync(["-hide_banner","-i",source],ct,false);
        var match=Regex.Match(result.Error,@"Duration:\s*(\d+):(\d+):(\d+(?:\.\d+)?)");
        if(!match.Success)throw new IOException("Videolänge konnte nicht gelesen werden.");
        return double.Parse(match.Groups[1].Value,CultureInfo.InvariantCulture)*3600+double.Parse(match.Groups[2].Value,CultureInfo.InvariantCulture)*60+double.Parse(match.Groups[3].Value,CultureInfo.InvariantCulture);
    }
    public async Task<IReadOnlyList<SetVideoFrame>> FindAsync(string source,string folder,IProgress<string>? progress,CancellationToken ct)
    {
        var duration=await DurationAsync(source,ct);Directory.CreateDirectory(folder);var frames=new List<SetVideoFrame>();
        for(var i=0;i<9;i++)
        {
            ct.ThrowIfCancellationRequested();progress?.Report($"Videoframe {i+1}/9 · Helligkeit und Detail prüfen …");
            frames.Add(await ExtractAsync(source,duration*(.08+i*.105),folder,ct));
        }
        return frames.OrderByDescending(f=>f.Score).ToArray();
    }
    public async Task<SetVideoFrame> ExtractAsync(string source,double seconds,string folder,CancellationToken ct)
    {
        if(!File.Exists(source))throw new FileNotFoundException("Video nicht erreichbar.",source);
        if(!double.IsFinite(seconds)||seconds<0)throw new ArgumentException("Ungültiger Zeitpunkt.");
        Directory.CreateDirectory(folder);var path=System.IO.Path.Combine(folder,Guid.NewGuid().ToString("N")+".png");
        try
        {
            await CaptureAsync(["-nostdin","-v","error","-ss",seconds.ToString("0.###",CultureInfo.InvariantCulture),"-i",source,"-map","0:v:0","-an","-vf","scale=1280:720:force_original_aspect_ratio=increase,crop=1280:720,setsar=1","-frames:v","1","-update","1",path],ct);
            if(!File.Exists(path)||new FileInfo(path).Length==0)throw new IOException("An dieser Stelle wurde kein Videoframe gefunden.");
            var gray=await CaptureAsync(["-nostdin","-v","error","-i",path,"-vf","scale=160:90,format=gray","-frames:v","1","-f","rawvideo","pipe:1"],ct);
            return new(path,seconds,Score(gray.Bytes),source);
        }
        catch{if(File.Exists(path))File.Delete(path);throw;}
    }
    public static double Score(byte[] pixels)
    {
        if(pixels.Length!=160*90)return 0;
        var mean=pixels.Average(b=>(double)b);var variance=pixels.Average(b=>Math.Pow(b-mean,2));double edges=0;
        for(var y=1;y<89;y++)for(var x=1;x<159;x++){var p=y*160+x;edges+=Math.Abs(4*pixels[p]-pixels[p-1]-pixels[p+1]-pixels[p-160]-pixels[p+160]);}
        var clipped=pixels.Count(b=>b<12||b>243)/(double)pixels.Length;
        return (Math.Sqrt(variance)+edges/(158*88))*(1-clipped)*Math.Max(0,1-Math.Abs(mean-125)/125);
    }
    private async Task<(byte[] Bytes,string Error)> CaptureAsync(string[] arguments,CancellationToken ct,bool requireSuccess=true)
    {
        var start=new ProcessStartInfo(ffmpeg){UseShellExecute=false,CreateNoWindow=true,RedirectStandardError=true,RedirectStandardOutput=true};foreach(var arg in arguments)start.ArgumentList.Add(arg);
        using var process=Process.Start(start)??throw new IOException("FFmpeg konnte nicht starten.");
        using var kill=ct.Register(()=>{try{if(!process.HasExited)process.Kill(true);}catch(InvalidOperationException){}catch(System.ComponentModel.Win32Exception){}});
        using var buffer=new MemoryStream();var output=process.StandardOutput.BaseStream.CopyToAsync(buffer,ct);var error=process.StandardError.ReadToEndAsync(ct);
        try{await Task.WhenAll(output,error,process.WaitForExitAsync(ct));ct.ThrowIfCancellationRequested();var message=await error;if(requireSuccess&&process.ExitCode!=0)throw new IOException(message[..Math.Min(1200,message.Length)]);return(buffer.ToArray(),message);}
        finally{if(!process.HasExited){process.Kill(true);await process.WaitForExitAsync(CancellationToken.None);}}
    }
}
