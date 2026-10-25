using System.Security.Cryptography;

namespace Cratebase.Server;

public sealed class ServerSettings
{
    public bool Enabled { get; set; }
    public int Port { get; set; } = 5874;
    public bool AllowLan { get; set; }
    public string AccessKey { get; set; } = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    public string FfmpegPath { get; set; } = DefaultFfmpeg();
    public int CacheMegabytes { get; set; } = 2048;
    public string CertificatePath { get; set; } = "";
    // Supplied on start (or via CRATEBASE_TLS_PASSWORD); never serialized into workspace settings.
    [System.Text.Json.Serialization.JsonIgnore]
    public string CertificatePassword { get; set; } = "";
    private static string DefaultFfmpeg()
    {
        if(Environment.GetEnvironmentVariable("CRATEBASE_FFMPEG_PATH") is {Length:>0} configured)return configured;
        var filename=OperatingSystem.IsWindows()?"ffmpeg.exe":"ffmpeg";
        var bundled=Path.Combine(AppContext.BaseDirectory,filename);
        if(File.Exists(bundled))return bundled;
        var local=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Cratebase","tools",filename);
        return File.Exists(local)?local:"ffmpeg";
    }
}

public sealed record FlagChange(long[] Ids, string Field, bool Value);
public sealed record PlaylistChange(string Name, long? ParentId = null, bool Folder = false);
public sealed record AddTracks(long[] Ids);
public sealed record MetadataChange(Dictionary<string, string?> Values);
public sealed record RemoteChange(long? TrackId = null);
public sealed record FolderEntry(string Name, long RootId, string RelativePath);
