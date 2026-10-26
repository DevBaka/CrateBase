using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Cratebase.Core.Models;

namespace Cratebase.Analysis;

public sealed class HistoryAudioAnalyzer(string directory,string ffmpeg)
{
    private static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web){WriteIndented=true};
    private string OwnPython=>Path.Combine(directory,"environment","Scripts","python.exe");
    private string? Python=>new[]{Environment.GetEnvironmentVariable("CRATEBASE_HISTORY_PYTHON"),File.Exists(Path.Combine(directory,"ready-v1"))?OwnPython:null,Path.Combine(Path.GetDirectoryName(directory)!,"stem-runtime","environment","Scripts","python.exe")}.FirstOrDefault(p=>!string.IsNullOrWhiteSpace(p)&&File.Exists(p));
    public bool Installed=>Python!=null;
    public async Task InstallAsync(IProgress<string>? progress,CancellationToken ct)
    {
        if(Installed)return;
        if(!OperatingSystem.IsWindows())throw new PlatformNotSupportedException("Die automatische Analyse-Laufzeit unterstützt zunächst Windows.");
        Directory.CreateDirectory(directory);
        var uv=Path.Combine(directory,"uv","uv.exe");
        if(!File.Exists(uv))
        {
            progress?.Report("Kleine private Analyse-Laufzeit herunterladen …");
            var archive=Path.Combine(directory,Guid.NewGuid()+".zip");
            try
            {
                using var http=new HttpClient{Timeout=TimeSpan.FromMinutes(3)};
                await using(var input=await http.GetStreamAsync("https://github.com/astral-sh/uv/releases/download/0.13.5/uv-x86_64-pc-windows-msvc.zip",ct))
                await using(var output=File.Create(archive))await input.CopyToAsync(output,ct);
                await using(var check=File.OpenRead(archive))if(!Convert.ToHexString(await SHA256.HashDataAsync(check,ct)).Equals("a38b8d1902b7cfbc76b423ba1bb456f623f42064cdc72628b6075b89afd1019a",StringComparison.OrdinalIgnoreCase))throw new IOException("Analyse-Laufzeit: SHA-256 stimmt nicht.");
                ZipFile.ExtractToDirectory(archive,Path.Combine(directory,"uv"),true);
            }
            finally{if(File.Exists(archive))File.Delete(archive);}
        }
        if(!File.Exists(OwnPython))await RunAsync(uv,["--no-config","venv","--python","3.11","--managed-python",Path.Combine(directory,"environment")],progress,ct);
        await RunAsync(uv,["--no-config","pip","install","--python",OwnPython,"numpy==2.3.5"],progress,ct);
        await File.WriteAllTextAsync(Path.Combine(directory,"ready-v1"),"numpy 2.3.5",ct);
    }
    public async Task<HistoryAudioReport> AnalyzeAsync(string recording,IReadOnlyList<HistoryAudioTrack> tracks,IProgress<string>? progress,CancellationToken ct)
    {
        if(!File.Exists(recording))throw new FileNotFoundException("Aufnahme nicht erreichbar.",recording);
        if(tracks.Count==0)throw new ArgumentException("Die History enthält keine Kandidaten.");
        if(!Installed)await InstallAsync(progress,ct);
        var job=Path.Combine(directory,"jobs",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(job);
        var resource=typeof(HistoryAudioAnalyzer).Assembly.GetManifestResourceNames().Single(n=>n.EndsWith("history_match.py",StringComparison.Ordinal));
        await using(var source=typeof(HistoryAudioAnalyzer).Assembly.GetManifestResourceStream(resource)!)
        await using(var output=File.Create(Path.Combine(job,"history_match.py")))await source.CopyToAsync(output,ct);
        var reportPath=Path.Combine(job,"report.json");var manifest=Path.Combine(job,"manifest.json");
        await File.WriteAllTextAsync(manifest,JsonSerializer.Serialize(new{recording,ffmpeg,cache=Path.Combine(directory,"fingerprints"),output=reportPath,tracks},Json),ct);
        await RunAsync(Python!,["-u",Path.Combine(job,"history_match.py"),manifest],progress,ct);
        var report=JsonSerializer.Deserialize<HistoryAudioReport>(await File.ReadAllTextAsync(reportPath,ct),Json)??throw new IOException("Analysebericht ist leer.");
        report.ApplyConservativeThresholds();report.RefreshEvidence(tracks);
        return report;
    }
    public Task PreviewAsync(string recording,double start,string destination,CancellationToken ct)=>TransitionRenderer.RunAsync(ffmpeg,["-nostdin","-v","error","-y","-ss",Math.Max(0,start-3).ToString(System.Globalization.CultureInfo.InvariantCulture),"-i",recording,"-t","25","-map","0:a:0","-vn","-ac","2","-ar","44100",destination],ct);
    private async Task RunAsync(string executable,IEnumerable<string> arguments,IProgress<string>? progress,CancellationToken ct)
    {
        var start=new ProcessStartInfo(executable){UseShellExecute=false,CreateNoWindow=true,RedirectStandardError=true,RedirectStandardOutput=true,StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8};
        foreach(var arg in arguments)start.ArgumentList.Add(arg);
        start.Environment["UV_PYTHON_INSTALL_DIR"]=Path.Combine(directory,"python");start.Environment["UV_PYTHON_INSTALL_REGISTRY"]="false";
        start.Environment["UV_CACHE_DIR"]=Path.Combine(directory,"package-cache");start.Environment["PYTHONUTF8"]="1";
        start.Environment["OPENBLAS_NUM_THREADS"]="1";start.Environment["OMP_NUM_THREADS"]="1";
        using var process=Process.Start(start)??throw new IOException("Audio-Abgleich konnte nicht starten.");
        try{process.PriorityClass=ProcessPriorityClass.BelowNormal;}catch(System.ComponentModel.Win32Exception){}catch(InvalidOperationException){}
        using var kill=ct.Register(()=>{try{if(!process.HasExited)process.Kill(true);}catch(InvalidOperationException){}catch(System.ComponentModel.Win32Exception){}});
        var errors=new Queue<string>();
        async Task ReadAsync(StreamReader reader,bool error)
        {
            while(await reader.ReadLineAsync(ct) is {} line)
            {
                if(error){if(errors.Count>=15)errors.Dequeue();errors.Enqueue(line);}
                try{using var json=JsonDocument.Parse(line);if(json.RootElement.TryGetProperty("progress",out var message))progress?.Report(message.GetString()??"");}
                catch(JsonException){if(error)progress?.Report(line[..Math.Min(350,line.Length)]);}
            }
        }
        try
        {
            await Task.WhenAll(ReadAsync(process.StandardOutput,false),ReadAsync(process.StandardError,true),process.WaitForExitAsync(ct));
            ct.ThrowIfCancellationRequested();if(process.ExitCode!=0)throw new IOException("Audio-Abgleich fehlgeschlagen: "+string.Join("\n",errors));
        }
        finally{if(!process.HasExited){process.Kill(true);await process.WaitForExitAsync(CancellationToken.None);}}
    }
}
