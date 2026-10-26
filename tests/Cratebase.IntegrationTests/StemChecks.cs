using System.Diagnostics;
using Cratebase.Analysis;
using Cratebase.Core.Models;
internal static class StemChecks
{
    public static async Task RunAsync(bool real)
    {
        var data=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Cratebase");
        var ffmpeg=Path.Combine(data,"tools","ffmpeg.exe");var directory=Path.Combine(Path.GetTempPath(),"Cratebase-stems-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        var source=Path.Combine(directory,"fixture.wav");var start=new ProcessStartInfo(ffmpeg){UseShellExecute=false,CreateNoWindow=true};
        foreach(var arg in new[]{"-v","error","-f","lavfi","-i","sine=frequency=440:duration=8","-ac","2",source})start.ArgumentList.Add(arg);
        using(var process=Process.Start(start)!){await process.WaitForExitAsync();if(process.ExitCode!=0)throw new Exception("Audio fixture failed");}
        var track=new Track {Id=1,Title="Test",Artist="Fixture",DurationMs=8000};var output=Path.Combine(directory,"fixture.stem.mp4");
        await new TraktorStemWriter(ffmpeg).WriteAsync(source,[source,source,source,source],track,output,CancellationToken.None);
        TraktorStemWriter.Validate(output);Console.WriteLine("PASS: Encoded master plus four stereo AAC tracks and valid NI stem manifest");
        try {await new TraktorStemWriter(ffmpeg).WriteAsync(source,[source,source,source,source],track,output,CancellationToken.None);throw new Exception("Overwrite accepted");}catch(IOException){Console.WriteLine("PASS: Existing stem files are never overwritten");}
        if(real)
        {
            var generator=new ManagedStemGenerator(Path.Combine(data,"stem-runtime"),ffmpeg);
            using var timeout=new CancellationTokenSource(TimeSpan.FromMinutes(15));
            var progress=new Progress<string>(s=>Console.WriteLine(s));await generator.InstallAsync(progress,timeout.Token);
            var separated=Path.Combine(directory,"real-separation.stem.mp4");await generator.GenerateAsync(source,track,separated,progress,timeout.Token);TraktorStemWriter.Validate(separated);
            Console.WriteLine("PASS: Actual private CPU Demucs inference produces a five-stream stem container");
        }
        Console.WriteLine("Stem fixtures: "+directory);
    }
}
