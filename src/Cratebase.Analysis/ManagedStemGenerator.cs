using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using Cratebase.Core.Models;

namespace Cratebase.Analysis;

/// <summary>App-private CPU Demucs environment. No global Python, PATH or Traktor installation changes.</summary>
public sealed class ManagedStemGenerator(string directory,string ffmpeg)
{
    private string Uv=>Path.Combine(directory,"uv","uv.exe");
    private string Python=>Path.Combine(directory,"environment","Scripts","python.exe");
    public bool Installed=>File.Exists(Python)&&File.Exists(Path.Combine(directory,"ready-v2"));
    public async Task InstallAsync(IProgress<string>? progress,CancellationToken ct)
    {
        if(Installed)return;
        if(!OperatingSystem.IsWindows())throw new PlatformNotSupportedException("Automatische Stem-Laufzeit zunächst für Windows.");
        Directory.CreateDirectory(directory);
        if(!File.Exists(Uv))
        {
            var zip=Path.Combine(directory,"uv.zip");var temporary=zip+".partial";
            try
            {
                using var client=new HttpClient {Timeout=TimeSpan.FromMinutes(3)};
                await using(var input=await client.GetStreamAsync("https://github.com/astral-sh/uv/releases/download/0.13.5/uv-x86_64-pc-windows-msvc.zip",ct))
                await using(var output=File.Create(temporary))await input.CopyToAsync(output,ct);
                await using(var check=File.OpenRead(temporary))if(!Convert.ToHexString(await SHA256.HashDataAsync(check,ct)).Equals("a38b8d1902b7cfbc76b423ba1bb456f623f42064cdc72628b6075b89afd1019a",StringComparison.OrdinalIgnoreCase))throw new IOException("Stem-Laufzeit: SHA-256-Prüfung fehlgeschlagen.");
                File.Move(temporary,zip,true);ZipFile.ExtractToDirectory(zip,Path.Combine(directory,"uv"),true);
            }
            finally {if(File.Exists(temporary))File.Delete(temporary);}
        }
        progress?.Report("Stem-Laufzeit · private Python-Umgebung wird angelegt …");
        if(!File.Exists(Python))await ExecuteAsync(Uv,["--no-config","venv","--python","3.11","--managed-python",Path.Combine(directory,"environment")],progress,ct);
        progress?.Report("Stem-Laufzeit · PyTorch CPU und Demucs werden heruntergeladen …");
        await ExecuteAsync(Uv,["--no-config","pip","install","--python",Python,"torch==2.5.1","torchaudio==2.5.1","--index-url","https://download.pytorch.org/whl/cpu"],progress,ct);
        await ExecuteAsync(Uv,["--no-config","pip","install","--python",Python,"demucs==4.0.1","numpy==1.26.4","soundfile==0.13.1"],progress,ct);
        await ExecuteAsync(Python,["-c","import demucs, torch, torchaudio, soundfile; assert 'soundfile' in torchaudio.list_audio_backends(); print('Stem runtime ready')"],progress,ct);
        await File.WriteAllTextAsync(Path.Combine(directory,"ready-v2"),"demucs 4.0.1; torch/torchaudio 2.5.1 CPU; numpy 1.26.4; soundfile 0.13.1",ct);
    }
    public async Task GenerateAsync(string source,Track track,string destination,IProgress<string>? progress,CancellationToken ct)
    {
        if(!Installed)throw new IOException("Stem-Laufzeit zuerst einrichten.");
        if(File.Exists(destination))throw new IOException("Stem-Datei existiert bereits und bleibt unverändert.");
        var work=Path.Combine(directory,"jobs",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(work);
        try
        {
            progress?.Report("Vier Stems werden auf der CPU berechnet: "+track.DisplayTitle);
            await ExecuteAsync(Python,["-m","demucs","-n","htdemucs","-d","cpu","--shifts","1","-j","0","--clip-mode","clamp","-o",work,source],progress,ct);
            var stems=new List<string>();foreach(var stem in new[]{"drums","bass","other","vocals"})stems.Add(Directory.GetFiles(work,stem+".wav",SearchOption.AllDirectories).Single());
            progress?.Report("Traktor-Stem-Container wird erstellt …");
            await new TraktorStemWriter(ffmpeg).WriteAsync(source,stems,track,destination,ct);
        }
        finally {PrivateRuntimeFiles.DeleteChildDirectory(Path.Combine(directory,"jobs"),work);}
    }
    private async Task ExecuteAsync(string executable,IEnumerable<string> arguments,IProgress<string>? progress,CancellationToken ct)
    {
        var start=new ProcessStartInfo(executable){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,WorkingDirectory=directory};
        foreach(var arg in arguments)start.ArgumentList.Add(arg);
        start.Environment["UV_PYTHON_INSTALL_DIR"]=Path.Combine(directory,"python");start.Environment["UV_PYTHON_INSTALL_REGISTRY"]="false";
        start.Environment["UV_CACHE_DIR"]=Path.Combine(directory,"package-cache");start.Environment["TORCH_HOME"]=Path.Combine(directory,"models");
        start.Environment["OMP_NUM_THREADS"]="8";start.Environment["MKL_NUM_THREADS"]="8";start.Environment["PYTHONUTF8"]="1";
        if(Path.IsPathFullyQualified(ffmpeg))start.Environment["PATH"]=Path.GetDirectoryName(ffmpeg)+Path.PathSeparator+Environment.GetEnvironmentVariable("PATH");
        using var process=Process.Start(start)??throw new IOException("Stem-Prozess konnte nicht starten.");
        try {process.PriorityClass=ProcessPriorityClass.BelowNormal;}catch(System.ComponentModel.Win32Exception){}catch(InvalidOperationException){}
        using var kill=ct.Register(()=>{try {if(!process.HasExited)process.Kill(true);}catch(InvalidOperationException){}});
        var errors=new Queue<string>();
        async Task Read(StreamReader reader,bool error)
        {while(await reader.ReadLineAsync(ct) is {} line){if(line.Length>600)line=line[^600..];if(error){if(errors.Count>=12)errors.Dequeue();errors.Enqueue(line);}progress?.Report(line);}}
        var stdout=Read(process.StandardOutput,false);var stderr=Read(process.StandardError,true);
        try {await Task.WhenAll(process.WaitForExitAsync(ct),stdout,stderr);ct.ThrowIfCancellationRequested();if(process.ExitCode!=0)throw new IOException("Stem-Prozess fehlgeschlagen: "+string.Join("\n",errors));}
        finally {if(!process.HasExited){process.Kill(true);await process.WaitForExitAsync(CancellationToken.None);}}
    }
}
