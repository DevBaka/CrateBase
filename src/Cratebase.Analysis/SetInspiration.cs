using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Cratebase.Core.Models;

namespace Cratebase.Analysis;

/// <summary>Public descriptions only. No media download, account cookies, or access restriction bypass.</summary>
public sealed class SetInspiration(string directory)
{
    private const string Url="https://github.com/yt-dlp/yt-dlp/releases/download/2026.10.21/yt-dlp.exe";
    private const string Hash="33121d192d706e62f684a06f80918e3f3a8ba52ac3a65213b6951c39c7d9f555";
    private string Executable=>Path.Combine(directory,"yt-dlp-2026.10.21.exe");
    public async Task<IReadOnlyList<SetReference>> FindAsync(string queryOrUrl,CancellationToken ct)
    {
        if(string.IsNullOrWhiteSpace(queryOrUrl)||queryOrUrl.Length>2000)throw new ArgumentException("Bitte ein Set-Thema oder einen Link eingeben.");
        var input="ytsearch3:"+queryOrUrl+" DJ set tracklist";
        if(Uri.TryCreate(queryOrUrl,UriKind.Absolute,out var uri))
        {
            var host=uri.IdnHost.ToLowerInvariant();
            if(uri.Scheme!="https"||!new[]{"youtube.com","www.youtube.com","youtu.be","soundcloud.com","www.soundcloud.com","mixcloud.com","www.mixcloud.com"}.Contains(host))throw new ArgumentException("Automatischer Abruf unterstützt öffentliche HTTPS-Links von YouTube, SoundCloud und Mixcloud.");
            input=uri.AbsoluteUri;
        }
        await InstallAsync(ct);
        var start=new ProcessStartInfo(Executable){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=System.Text.Encoding.UTF8};
        foreach(var arg in new[]{"--ignore-config","--no-plugin-dirs","--skip-download","--no-playlist","--no-warnings","--quiet","--ignore-no-formats-error","--socket-timeout","15","--retries","0","--extractor-retries","0","--print","%(.{id,title,description,webpage_url})j","--",input})start.ArgumentList.Add(arg);
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(ct);timeout.CancelAfter(TimeSpan.FromSeconds(90));
        using var process=Process.Start(start)??throw new IOException("Set-Recherche konnte nicht starten.");
        using var kill=timeout.Token.Register(()=>{try {if(!process.HasExited)process.Kill(true);}catch(InvalidOperationException){}});
        var output=ReadBoundedAsync(process.StandardOutput,2_000_000,timeout.Token);var errors=ReadBoundedAsync(process.StandardError,100_000,timeout.Token);
        try
        {
            await process.WaitForExitAsync(timeout.Token);var json=await output;var message=await errors;ct.ThrowIfCancellationRequested();
            var result=new List<SetReference>();
            foreach(var line in json.Split('\n',StringSplitOptions.RemoveEmptyEntries).Take(3))
            {
                using var doc=JsonDocument.Parse(line);var root=doc.RootElement;
                var link=root.GetProperty("webpage_url").GetString()??"";var title=root.GetProperty("title").GetString()??"";
                var description=root.TryGetProperty("description",out var d)&&d.ValueKind==JsonValueKind.String?d.GetString()??"":"";
                result.Add(new(link,title+"\n"+description[..Math.Min(description.Length,20000)]));
            }
            if(result.Count==0)throw new IOException("Keine öffentliche Set-Beschreibung abrufbar. Trackliste manuell einfügen. "+message[..Math.Min(500,message.Length)]);
            return result;
        }
        finally {if(!process.HasExited){process.Kill(true);await process.WaitForExitAsync(CancellationToken.None);}}
    }
    private static async Task<string> ReadBoundedAsync(StreamReader reader,int limit,CancellationToken ct)
    {
        var result=new System.Text.StringBuilder();var buffer=new char[4096];
        while(true){var count=await reader.ReadAsync(buffer,ct);if(count==0)break;if(result.Length+count>limit)throw new IOException("Set-Beschreibung überschreitet das Größenlimit.");result.Append(buffer,0,count);}
        return result.ToString();
    }
    private async Task InstallAsync(CancellationToken ct)
    {
        if(File.Exists(Executable))return;
        if(!OperatingSystem.IsWindows())throw new PlatformNotSupportedException("Automatische Set-Recherche ist zunächst für Windows verfügbar.");
        Directory.CreateDirectory(directory);var temporary=Executable+".partial";
        try
        {
            using var client=new HttpClient {Timeout=TimeSpan.FromMinutes(3)};
            await using(var stream=await client.GetStreamAsync(Url,ct))
            await using(var output=File.Create(temporary))
            {var buffer=new byte[131072];long total=0;while(true){var count=await stream.ReadAsync(buffer,ct);if(count==0)break;total+=count;if(total>17880111)throw new IOException("Unerwartete Recherche-Laufzeitgröße.");await output.WriteAsync(buffer.AsMemory(0,count),ct);}}
            await using(var check=File.OpenRead(temporary))if(!Convert.ToHexString(await SHA256.HashDataAsync(check,ct)).Equals(Hash,StringComparison.OrdinalIgnoreCase))throw new IOException("Recherche-Laufzeit: SHA-256-Prüfung fehlgeschlagen.");
            File.Move(temporary,Executable,false);
        }
        finally {if(File.Exists(temporary))File.Delete(temporary);}
    }
}
