using System.Buffers.Binary;
using System.ComponentModel;
using System.Diagnostics;
using Cratebase.Core.Abstractions;
using Cratebase.Core.Models;

namespace Cratebase.Analysis;

/// <summary>Streams signed 16-bit PCM from FFmpeg; keeps only three-band peaks, never writes beside the audio.</summary>
public sealed class FfmpegWaveformGenerator(string executable="ffmpeg") : IWaveformGenerator
{
    private const int SampleRate=11025;
    private const int ColumnCount=1200;

    public Task<WaveformData?> GenerateAsync(string absoluteFilePath,CancellationToken ct=default)
        => Task.Run(()=>GenerateCoreAsync(absoluteFilePath,ct),ct);

    private async Task<WaveformData?> GenerateCoreAsync(string path,CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var bundled=Path.Combine(AppContext.BaseDirectory,OperatingSystem.IsWindows()?"ffmpeg.exe":"ffmpeg");
        var program=executable=="ffmpeg"&&File.Exists(bundled)?bundled:executable;
        var start=new ProcessStartInfo { FileName=program,RedirectStandardOutput=true,RedirectStandardError=true,UseShellExecute=false,CreateNoWindow=true };
        foreach(var arg in new[] { "-nostdin","-v","error","-i",path,"-map","0:a:0","-vn","-sn","-dn","-ac","1","-ar",SampleRate.ToString(System.Globalization.CultureInfo.InvariantCulture),"-c:a","pcm_s16le","-f","s16le","pipe:1" }) start.ArgumentList.Add(arg);
        using var process=new Process { StartInfo=start };
        try { if(!process.Start()) throw new IOException("FFmpeg konnte nicht gestartet werden."); }
        catch(Win32Exception ex) { throw new IOException("RGB-Waveform: FFmpeg nicht gefunden. ffmpeg im PATH oder neben Cratebase bereitstellen.",ex); }
        using var cancellation=ct.Register(()=>
        {
            try { if(!process.HasExited) process.Kill(entireProcessTree:true); }
            catch(InvalidOperationException) { } catch(Win32Exception) { }
        });
        var stderr=process.StandardError.ReadToEndAsync();
        var waveform=ReadPeaksAsync(process.StandardOutput.BaseStream,ct);
        try
        {
            await Task.WhenAll(waveform,stderr,process.WaitForExitAsync(ct));
            ct.ThrowIfCancellationRequested();
            if(process.ExitCode!=0) throw new IOException("RGB-Waveform konnte nicht dekodiert werden: "+(await stderr).Trim()[..Math.Min(400,(await stderr).Trim().Length)]);
            return await waveform;
        }
        finally
        {
            // Also clean up if reading/decoding throws for reasons other than cancellation.
            if(!process.HasExited) { process.Kill(entireProcessTree:true); await process.WaitForExitAsync(CancellationToken.None); }
        }
    }

    private static async Task<WaveformData?> ReadPeaksAsync(Stream pcm,CancellationToken ct)
    {
        var peaks=new List<WaveformColumn>();
        var buffer=new byte[32768];
        var carry=-1;
        var group=0;
        float low=0,highLowPass=0,lowPeak=0,midPeak=0,highPeak=0;
        var lowAlpha=(float)(1-Math.Exp(-2*Math.PI*200/SampleRate));
        var highAlpha=(float)(1-Math.Exp(-2*Math.PI*4000/SampleRate));
        void Sample(short value)
        {
            var sample=value/32768f;
            low+=lowAlpha*(sample-low); highLowPass+=highAlpha*(sample-highLowPass);
            lowPeak=Math.Max(lowPeak,Math.Abs(low)); midPeak=Math.Max(midPeak,Math.Abs(highLowPass-low)); highPeak=Math.Max(highPeak,Math.Abs(sample-highLowPass));
            if(++group<256) return;
            peaks.Add(new(lowPeak,midPeak,highPeak)); group=0; lowPeak=midPeak=highPeak=0;
        }
        int read;
        while((read=await pcm.ReadAsync(buffer,ct))>0)
        {
            ct.ThrowIfCancellationRequested();
            var start=0;
            if(carry>=0) { Sample(unchecked((short)(carry|(buffer[0]<<8)))); start=1; carry=-1; }
            for(var i=start;i+1<read;i+=2) Sample(BinaryPrimitives.ReadInt16LittleEndian(buffer.AsSpan(i,2)));
            if((read-start)%2!=0) carry=buffer[read-1];
        }
        if(group>0) peaks.Add(new(lowPeak,midPeak,highPeak));
        if(peaks.Count==0) return null;
        var count=Math.Min(ColumnCount,peaks.Count);
        var columns=new WaveformColumn[count]; var maximum=0.000001f;
        for(var i=0;i<count;i++)
        {
            var from=(int)((long)i*peaks.Count/count); var to=(int)((long)(i+1)*peaks.Count/count);
            float l=0,m=0,h=0;
            for(var j=from;j<to;j++) { l=Math.Max(l,peaks[j].Low); m=Math.Max(m,peaks[j].Mid); h=Math.Max(h,peaks[j].High); }
            columns[i]=new(l,m,h); maximum=Math.Max(maximum,Math.Max(l,Math.Max(m,h)));
        }
        for(var i=0;i<count;i++) columns[i]=new(columns[i].Low/maximum,columns[i].Mid/maximum,columns[i].High/maximum);
        return new() { Columns=columns,Origin=WaveformOrigin.GeneratedRgb };
    }
}
