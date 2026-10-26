using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;

namespace Cratebase.Analysis;

/// <summary>App-owned Vulkan image-to-image runtime. No Python, external server, or image upload.</summary>
public sealed class LocalArtworkImageAi(string directory)
{
    private const string Release="master-1015-e41fbcf";
    private const string RuntimeName="sd-master-e41fbcf-bin-win-vulkan-x64.zip";
    private const string RuntimeHash="259ceeb62f1069190d24c85ea1c4549d2d61deb0ef712510dbe98d79ce93a38b";
    private const string ModelRevision="9cfd069101959ca3828bf9c04a4419870832b74f";
    private const string ModelHash="e9476a13728cd75d8279f6ec8bad753a66a1957ca375a1464dc63b37db6e3916";
    private string RuntimeFolder=>Path.Combine(directory,Release);
    private string? Executable=>Directory.Exists(RuntimeFolder)?Directory.EnumerateFiles(RuntimeFolder,"sd-cli.exe",SearchOption.AllDirectories).FirstOrDefault():null;
    private string Model=>Path.Combine(directory,"v1-5-pruned-emaonly-fp16.safetensors");
    public bool Installed=>Executable!=null&&File.Exists(Model)&&new FileInfo(Model).Length==2132696762&&File.Exists(Path.Combine(directory,"ready-v1"));
    private static readonly SemaphoreSlim Gate=new(1,1);
    public async Task InstallAsync(IProgress<string>? progress,CancellationToken ct)
    {
        if(!OperatingSystem.IsWindows()||System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture!=System.Runtime.InteropServices.Architecture.X64)throw new PlatformNotSupportedException("Die integrierte Bild-KI unterstützt zunächst Windows x64 mit Vulkan-Grafiktreiber.");
        await Gate.WaitAsync(ct);
        try
        {
            Directory.CreateDirectory(directory);
            if(Executable==null)
            {
                var zip=Path.Combine(directory,RuntimeName);await DownloadAsync($"https://github.com/leejet/stable-diffusion.cpp/releases/download/{Release}/{RuntimeName}",zip,RuntimeHash,29744345,progress,ct);
                var staging=Path.Combine(directory,"extract-"+Guid.NewGuid().ToString("N"));
                try{ZipFile.ExtractToDirectory(zip,staging);if(!Directory.EnumerateFiles(staging,"sd-cli.exe",SearchOption.AllDirectories).Any())throw new IOException("Bild-KI-Laufzeit ist unvollständig.");Directory.Move(staging,RuntimeFolder);}
                finally{PrivateRuntimeFiles.DeleteChildDirectory(directory,staging);}
            }
            await DownloadAsync($"https://huggingface.co/Comfy-Org/stable-diffusion-v1-5-archive/resolve/{ModelRevision}/v1-5-pruned-emaonly-fp16.safetensors",Model,ModelHash,2132696762,progress,ct);
            using var http=new HttpClient();
            await File.WriteAllTextAsync(Path.Combine(directory,"stable-diffusion.cpp-LICENSE.txt"),await http.GetStringAsync("https://raw.githubusercontent.com/leejet/stable-diffusion.cpp/e41fbcf/LICENSE",ct),ct);
            await File.WriteAllTextAsync(Path.Combine(directory,"SD15-LICENSE.txt"),await http.GetStringAsync("https://raw.githubusercontent.com/CompVis/stable-diffusion/main/LICENSE",ct),ct);
            await File.WriteAllTextAsync(Path.Combine(directory,"ready-v1"),$"{Release}; SD1.5 fp16 {ModelHash}; CreativeML Open RAIL-M",ct);
            progress?.Report("Bild-KI installiert: SD 1.5 FP16 + Vulkan, SHA-256 geprüft.");
        }
        finally{Gate.Release();}
    }
    private static async Task DownloadAsync(string url,string path,string hash,long size,IProgress<string>? progress,CancellationToken ct)
    {
        if(File.Exists(path)&&new FileInfo(path).Length==size){await using var stream=File.OpenRead(path);if(Convert.ToHexString(await SHA256.HashDataAsync(stream,ct)).Equals(hash,StringComparison.OrdinalIgnoreCase))return;}
        var temporary=path+".partial";
        try
        {
            using var http=new HttpClient{Timeout=TimeSpan.FromMinutes(45)};using var response=await http.GetAsync(url,HttpCompletionOption.ResponseHeadersRead,ct);response.EnsureSuccessStatusCode();
            await using(var input=await response.Content.ReadAsStreamAsync(ct))
            await using(var output=new FileStream(temporary,FileMode.Create,FileAccess.Write,FileShare.None,131072,true))
            {
                var buffer=new byte[131072];long total=0;var watch=Stopwatch.StartNew();
                while(true){var count=await input.ReadAsync(buffer,ct);if(count==0)break;total+=count;if(total>size)throw new IOException("Bild-KI-Download ist größer als erwartet.");await output.WriteAsync(buffer.AsMemory(0,count),ct);if(watch.ElapsedMilliseconds>500){progress?.Report($"Bild-KI: {Path.GetFileName(path)} · {total/1000000:N0}/{size/1000000:N0} MB");watch.Restart();}}
                if(total!=size)throw new IOException("Bild-KI-Download ist unvollständig.");
            }
            await using(var check=File.OpenRead(temporary))if(!Convert.ToHexString(await SHA256.HashDataAsync(check,ct)).Equals(hash,StringComparison.OrdinalIgnoreCase))throw new IOException("Bild-KI: SHA-256 stimmt nicht.");
            File.Move(temporary,path,true);
        }
        finally{if(File.Exists(temporary))File.Delete(temporary);}
    }
    public async Task<string> GenerateAsync(string source,string prompt,double strength,long seed,IProgress<string>? progress,CancellationToken ct,bool cpu=false)
    {
        if(!Installed)throw new IOException("Bild-KI zuerst im Thumbnail-Editor installieren (etwa 2,2 GB).");
        if(!File.Exists(source))throw new FileNotFoundException("Ausgangsbild nicht erreichbar.",source);
        if(string.IsNullOrWhiteSpace(prompt))throw new ArgumentException("Bitte Bildstil oder gewünschte Änderung beschreiben (Englisch empfohlen).");
        if(!double.IsFinite(strength)||strength is <.05 or >.85)throw new ArgumentException("Bild-KI-Stärke muss zwischen 0,05 und 0,85 liegen.");
        await Gate.WaitAsync(ct);var work=Path.Combine(directory,"jobs",Guid.NewGuid().ToString("N"));
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(ct);timeout.CancelAfter(TimeSpan.FromMinutes(10));
        try
        {
            Directory.CreateDirectory(work);
            var output=Path.Combine(work,"result.png");var start=new ProcessStartInfo(Executable!){UseShellExecute=false,CreateNoWindow=true,RedirectStandardError=true,RedirectStandardOutput=true,WorkingDirectory=Path.GetDirectoryName(Executable!)!};
            foreach(var arg in new[]{"-m",Model,"--init-img",source,"-p",prompt[..Math.Min(1500,prompt.Length)]+", high quality photography, no text, no watermark","--negative-prompt","text, letters, watermark, distorted face, deformed hands, blurry","-W","768","-H","432","--steps","24","--cfg-scale","6","--strength",strength.ToString("0.###",CultureInfo.InvariantCulture),"--seed",seed.ToString(CultureInfo.InvariantCulture),"--sampling-method","euler_a","--backend",cpu?"cpu":"vulkan0","--offload-to-cpu","--vae-tiling","-t","8","-o",output})start.ArgumentList.Add(arg);
            using var process=Process.Start(start)??throw new IOException("Bild-KI konnte nicht starten.");
            using var kill=timeout.Token.Register(()=>{try{if(!process.HasExited)process.Kill(true);}catch(InvalidOperationException){}catch(System.ComponentModel.Win32Exception){}});
            var elapsed=Stopwatch.StartNew();using var heartbeat=new Timer(_=>progress?.Report($"Lokale Bild-KI berechnet Variante · {elapsed.Elapsed:mm\\:ss}"),null,0,2000);
            var stdout=process.StandardOutput.ReadToEndAsync();var stderr=process.StandardError.ReadToEndAsync();
            try{await process.WaitForExitAsync(timeout.Token);var text=(await stdout)+"\n"+(await stderr);await File.WriteAllTextAsync(Path.Combine(work,"runtime.log"),text,ct);if(process.ExitCode!=0||!File.Exists(output))throw new IOException("Bild-KI fehlgeschlagen. Vulkan-Treiber prüfen oder CPU-Modus nutzen. "+text[^Math.Min(900,text.Length)..]);}
            finally{if(!process.HasExited){process.Kill(true);await process.WaitForExitAsync(CancellationToken.None);}}
            return output;
        }
        catch(OperationCanceledException) when(!ct.IsCancellationRequested){throw new IOException("Bild-KI nach zehn Minuten beendet. Mit weniger Stärke oder freier GPU erneut versuchen.");}
        finally{Gate.Release();}
    }
}
