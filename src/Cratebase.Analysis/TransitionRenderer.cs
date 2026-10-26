using System.Diagnostics;
using System.Globalization;
using Cratebase.Core.Models;

namespace Cratebase.Analysis;

/// <summary>Offline audition with constant-tempo stretch and a 180 Hz bass trade. Never edits source audio.</summary>
public sealed class TransitionRenderer(string ffmpeg)
{
    public async Task RenderAsync(string sourceA,string sourceB,Track a,Track b,SetPlanEntry markersA,SetPlanEntry markersB,string destination,CancellationToken ct)
    {
        markersA.Validate(a);markersB.Validate(b);
        var duration=(markersA.FadeOutMs-markersA.PlayMs)/1000.0;
        if(duration is <1 or >120)throw new ArgumentException("Für die Übergangsvorschau Play A höchstens 120 Sekunden vor Fade-out A setzen.");
        var tempo=a.Bpm is >0&&b.Bpm is >0?a.Bpm.Value/b.Bpm.Value:1;
        if(tempo is <0.8 or >1.25)throw new ArgumentException("Für die Vorschau darf die Tempoanpassung höchstens −20 / +25 % betragen.");
        if(markersB.PlayMs+duration*tempo*1000>b.DurationMs)throw new ArgumentException("Track B ist ab dem Play-Marker zu kurz für diesen Übergang.");
        var trade=Math.Clamp((markersA.BassTradeMs-markersA.PlayMs)/1000.0,0,duration-.06);
        string N(double n)=>n.ToString("0.######",CultureInfo.InvariantCulture);
        var graph=$"[0:a:0]atrim=start={N(markersA.PlayMs/1000.0)}:duration={N(duration)},asetpts=PTS-STARTPTS,aresample=44100,asplit=2[al][ah];"+
            $"[1:a:0]atrim=start={N(markersB.PlayMs/1000.0)}:duration={N(duration*tempo)},asetpts=PTS-STARTPTS,atempo={N(tempo)},aresample=44100,asplit=2[bl][bh];"+
            $"[al]lowpass=f=180,afade=t=out:st={N(trade)}:d=0.06[alo];[ah]highpass=f=180,afade=t=out:st=0:d={N(duration)}[ahi];"+
            $"[bl]lowpass=f=180,afade=t=in:st={N(trade)}:d=0.06[blo];[bh]highpass=f=180,afade=t=in:st=0:d={N(duration)}[bhi];"+
            "[alo][ahi][blo][bhi]amix=inputs=4:normalize=0,alimiter=limit=0.95:level=false[out]";
        var temporary=destination+"."+Guid.NewGuid().ToString("N")+".partial.wav";
        try
        {
            await RunAsync(ffmpeg,["-nostdin","-v","error","-n","-i",sourceA,"-i",sourceB,"-filter_complex",graph,"-map","[out]","-t",N(duration),"-ac","2","-ar","44100","-c:a","pcm_s16le",temporary],ct);
            ct.ThrowIfCancellationRequested();File.Move(temporary,destination,true);
        }
        finally {if(File.Exists(temporary))File.Delete(temporary);}
    }
    internal static async Task RunAsync(string executable,IEnumerable<string> arguments,CancellationToken ct)
    {
        var start=new ProcessStartInfo(executable){UseShellExecute=false,CreateNoWindow=true,RedirectStandardError=true,RedirectStandardOutput=true};
        foreach(var arg in arguments)start.ArgumentList.Add(arg);
        using var process=Process.Start(start)??throw new IOException("Analyseprozess konnte nicht gestartet werden.");
        using var kill=ct.Register(()=>{try {if(!process.HasExited)process.Kill(true);}catch(InvalidOperationException){}catch(System.ComponentModel.Win32Exception){}});
        var errors=process.StandardError.ReadToEndAsync();var output=process.StandardOutput.ReadToEndAsync();
        try {await process.WaitForExitAsync(ct);await output;var message=await errors;ct.ThrowIfCancellationRequested();if(process.ExitCode!=0)throw new IOException(message.Length>1500?message[^1500..]:message);}
        finally {if(!process.HasExited){process.Kill(true);await process.WaitForExitAsync(CancellationToken.None);}}
    }
}
