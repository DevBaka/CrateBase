using System.Security.Cryptography;
using System.Text.Json;
using Cratebase.Core.Abstractions;
using Cratebase.Core.Models;

namespace Cratebase.Import;

public sealed class ClubExportGroup
{
    public string Key {get;init;}="";
    public string Genre {get;init;}="";
    public string Folder {get;set;}="";
    public string? Warning {get;init;}
    public int Count {get;set;}
}
public sealed record ClubExportItem(Track Track,string Source,string GroupKey,long Bytes,DateTime ModifiedUtc,bool Missing);
public sealed record ClubExportPlan(IReadOnlyList<ClubExportItem> Items,IReadOnlyList<ClubExportGroup> Groups)
{
    public long Bytes=>Items.Sum(i=>i.Bytes);
    public int Missing=>Items.Count(i=>i.Missing);
}
public sealed record ClubExportProgress(int Completed,int Total,long Bytes,long TotalBytes,string Message);
public sealed record ClubMediaExportResult(string NmlPath,int Copied,int Reused,int Skipped,long Bytes,IReadOnlyList<string> Warnings);
public sealed class ClubExportFile
{
    public long TrackId {get;set;}
    public string Source {get;set;}="";
    public long Bytes {get;set;}
    public DateTime ModifiedUtc {get;set;}
    public string RelativePath {get;set;}="";
    public string Sha256 {get;set;}="";
}
public sealed class ClubExportManifest
{
    public int Version {get;set;}=1;
    public List<ClubExportFile> Files {get;set;}=new();
    public List<string> RetainedFiles {get;set;}=new();
}

public sealed class ClubMediaExporter(ILibraryRootRepository roots,ILibraryQuery query,CollectionSync sync)
{
    public const string NmlName="Cratebase-Club-Collection.nml";
    public const string ManifestName=".cratebase-club-export.json";
    public const string ReportName="Cratebase-Exportbericht.json";
    private static readonly JsonSerializerOptions Json=new(){WriteIndented=true};

    public async Task<ClubExportPlan> PlanAsync(IReadOnlyDictionary<string,string>? folders=null,CancellationToken ct=default)
    {
        var rootMap=(await roots.GetAllAsync(ct)).ToDictionary(r=>r.Id);
        var items=new List<ClubExportItem>();var groups=new Dictionary<string,ClubExportGroup>();
        for(var offset=0;;offset+=1000)
        {
            var page=await query.QueryAsync(new(new(LibraryTargetKind.ClubCollection,"Club Collection"),Limit:1000,Offset:offset),ct);
            foreach(var track in page)
            {
                ct.ThrowIfCancellationRequested();
                var source=rootMap.TryGetValue(track.LibraryRootId,out var root)?Path.GetFullPath(Path.Combine(root.Path,track.RelativePath)):"";
                var suggestion=ClubGenreLayout.Suggest(track.GenreRaw,source);
                var key=(track.GenreRaw??"").Trim().ToLowerInvariant()+"|"+suggestion.Folder.ToLowerInvariant();
                if(!groups.TryGetValue(key,out var group))
                {
                    group=new(){Key=key,Genre=string.IsNullOrWhiteSpace(track.GenreRaw)?"Ohne Genre-Tag":track.GenreRaw.Trim(),Folder=folders?.GetValueOrDefault(key)??suggestion.Folder,Warning=suggestion.Warning};groups.Add(key,group);
                }
                group.Count++;
                var info=source.Length==0?null:new FileInfo(source);var exists=info?.Exists==true;
                items.Add(new(track,source,key,exists?info!.Length:0,exists?info!.LastWriteTimeUtc:DateTime.MinValue,!exists));
            }
            if(page.Count<1000)break;
        }
        return new(items,groups.Values.OrderBy(g=>g.Folder).ThenBy(g=>g.Genre).ToArray());
    }

    public async Task<ClubMediaExportResult> ExportAsync(ClubExportPlan plan,string destination,bool skipMissing=false,IProgress<ClubExportProgress>? progress=null,CancellationToken ct=default)
    {
        if(plan.Items.Count==0)throw new InvalidOperationException("Die Club Collection ist leer.");
        if(plan.Missing>0&&!skipMissing)throw new IOException($"{plan.Missing} Quelldateien sind nicht erreichbar. Export abgebrochen; fehlende Dateien können in der Vorschau ausdrücklich übersprungen werden.");
        if(string.IsNullOrWhiteSpace(destination)||!Path.IsPathFullyQualified(destination))throw new ArgumentException("Bitte einen absoluten Exportpfad wählen.");
        var root=Path.GetFullPath(destination).TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar);
        if(root.Length==0||string.Equals(root,Path.GetPathRoot(root)?.TrimEnd('\\','/'),StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Bitte einen eigenen Exportordner auswählen, keine Laufwerkswurzel.");
        var folders=plan.Groups.ToDictionary(g=>g.Key,g=>ClubGenreLayout.ValidateFolder(g.Folder));
        var sourceNml=await sync.GetSourcePathAsync(ct);
        var nml=Path.Combine(root,NmlName);var manifestPath=Path.Combine(root,ManifestName);var reportPath=Path.Combine(root,ReportName);
        if(sourceNml!=null&&string.Equals(Path.GetFullPath(sourceNml),nml,StringComparison.OrdinalIgnoreCase))throw new IOException("Die ursprüngliche Traktor-NML darf nicht überschrieben werden.");
        var manifest=File.Exists(manifestPath)?JsonSerializer.Deserialize<ClubExportManifest>(await File.ReadAllTextAsync(manifestPath,ct),Json)??throw new IOException("Exportmanifest ist ungültig."):new();
        if(manifest.Version!=1)throw new IOException("Unbekannte Version des Exportmanifests.");
        if(!File.Exists(manifestPath)&&(File.Exists(nml)||File.Exists(reportPath)))throw new IOException("Das Ziel enthält bereits gleichnamige Exportdateien ohne Cratebase-Manifest. Bitte einen separaten Ordner wählen.");
        foreach(var entry in manifest.Files)Inside(root,entry.RelativePath);
        var required=plan.Items.Where(i=>!i.Missing&&!manifest.Files.Any(e=>e.TrackId==i.Track.Id&&e.Source==i.Source&&e.Bytes==i.Bytes&&e.ModifiedUtc==i.ModifiedUtc&&File.Exists(Inside(root,e.RelativePath))&&new FileInfo(Inside(root,e.RelativePath)).Length==i.Bytes&&string.Equals(Path.GetDirectoryName(Inside(root,e.RelativePath)),Inside(root,folders[i.GroupKey]),StringComparison.OrdinalIgnoreCase))).Sum(i=>i.Bytes);
        try
        {
            var available=new DriveInfo(Path.GetPathRoot(root)!).AvailableFreeSpace;
            if(required>available)throw new IOException($"Nicht genug freier Speicher: mindestens {required/1_000_000_000.0:N2} GB benötigt, {available/1_000_000_000.0:N2} GB verfügbar.");
        }
        catch(ArgumentException) { }
        catch(NotSupportedException) { }
        CheckDirectories(root,root);Directory.CreateDirectory(root);
        var warnings=new List<string>();var copied=0;var reused=0;var skipped=0;long bytes=0;long lastProgress=0;var published=false;
        var paths=new Dictionary<long,string>();
        var selected=plan.Items.Select(i=>i.Track.Id).ToHashSet();
        var reserved=manifest.Files.Select(e=>Inside(root,e.RelativePath)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        async Task SaveManifest()=>await AtomicJson(manifestPath,manifest,CancellationToken.None);
        try
        {
            for(var index=0;index<plan.Items.Count;index++)
            {
                ct.ThrowIfCancellationRequested();
                var item=plan.Items[index];
                progress?.Report(new(index,plan.Items.Count,bytes,plan.Bytes,"Prüfen: "+item.Track.DisplayTitle));
                if(item.Missing)
                {skipped++;warnings.Add("Nicht erreichbar: "+item.Source);continue;}
                var folder=Inside(root,folders[item.GroupKey]);CheckDirectories(root,folder);Directory.CreateDirectory(folder);
                var previous=manifest.Files.FirstOrDefault(e=>e.TrackId==item.Track.Id&&string.Equals(e.Source,item.Source,StringComparison.OrdinalIgnoreCase));
                if(previous!=null&&previous.Bytes==item.Bytes&&previous.ModifiedUtc==item.ModifiedUtc&&string.Equals(Path.GetDirectoryName(Inside(root,previous.RelativePath)),folder,StringComparison.OrdinalIgnoreCase))
                {
                    var oldPath=Inside(root,previous.RelativePath);
                    if(File.Exists(oldPath)&&new FileInfo(oldPath).Length==item.Bytes&&await Hash(oldPath,ct)==previous.Sha256)
                    {
                        var sourceInfo=new FileInfo(item.Source);
                        if(sourceInfo.Exists&&sourceInfo.Length==item.Bytes&&sourceInfo.LastWriteTimeUtc==item.ModifiedUtc)
                        {paths[item.Track.Id]=oldPath;reused++;bytes+=item.Bytes;progress?.Report(new(index+1,plan.Items.Count,bytes,plan.Bytes,"Bereits vollständig: "+item.Track.DisplayTitle));continue;}
                    }
                }
                FileStream input;
                try {input=new(item.Source,FileMode.Open,FileAccess.Read,FileShare.Read,131072,FileOptions.Asynchronous|FileOptions.SequentialScan);}
                catch(Exception ex) when(skipMissing&&ex is IOException or UnauthorizedAccessException)
                {skipped++;warnings.Add("Nicht lesbar: "+item.Source+" · "+ex.Message);continue;}
                await using(input)
                {
                    var info=new FileInfo(item.Source);
                    if(input.Length!=item.Bytes||info.LastWriteTimeUtc!=item.ModifiedUtc)throw new IOException("Die Quelldatei wurde nach der Vorschau geändert. Bitte den Export erneut vorbereiten: "+item.Source);
                    var ext=item.Source.EndsWith(".stem.mp4",StringComparison.OrdinalIgnoreCase)?".stem.mp4":Path.GetExtension(item.Source);
                    var title=string.IsNullOrWhiteSpace(item.Track.Title)?Path.GetFileName(item.Source)[..^ext.Length]:item.Track.Title;
                    var stem=string.IsNullOrWhiteSpace(item.Track.Artist)?title:item.Track.Artist+" - "+title;
                    var available=Math.Min(140,235-folder.Length-ext.Length-12);
                    if(available<16)throw new IOException("Der Zielpfad ist zu lang. Bitte einen kürzeren Ordnernamen wählen: "+folder);
                    stem=ClubGenreLayout.SafeName(stem!,available);
                    var target=Path.Combine(folder,stem+ext);var suffix=2;
                    while(File.Exists(target)||reserved.Contains(target))target=Path.Combine(folder,$"{stem} ({suffix++}){ext}");
                    var temporary=Path.Combine(folder,".cratebase-"+Guid.NewGuid().ToString("N")+".partial");
                    string digest;
                    try
                    {
                        using var hasher=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                        await using(var output=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None,131072,true))
                        {
                            var buffer=new byte[131072];int read;long current=0;
                            while((read=await input.ReadAsync(buffer,ct))>0)
                            {
                                await output.WriteAsync(buffer.AsMemory(0,read),ct);hasher.AppendData(buffer,0,read);current+=read;
                                if(Environment.TickCount64-lastProgress>=100)
                                {lastProgress=Environment.TickCount64;progress?.Report(new(index,plan.Items.Count,bytes+current,plan.Bytes,"Kopieren: "+item.Track.DisplayTitle));}
                            }
                            await output.FlushAsync(ct);
                            if(current!=item.Bytes)throw new IOException("Quelldatei hat sich während des Kopierens geändert: "+item.Source);
                        }
                        var after=new FileInfo(item.Source);
                        if(after.Length!=item.Bytes||after.LastWriteTimeUtc!=item.ModifiedUtc)throw new IOException("Quelldatei hat sich während des Kopierens geändert: "+item.Source);
                        ct.ThrowIfCancellationRequested();File.Move(temporary,target,false);
                        digest=Convert.ToHexString(hasher.GetHashAndReset());
                    }
                    finally {if(File.Exists(temporary))File.Delete(temporary);}
                    if(previous!=null) {manifest.Files.Remove(previous);manifest.RetainedFiles.Add(previous.RelativePath);}
                    manifest.Files.Add(new(){TrackId=item.Track.Id,Source=item.Source,Bytes=item.Bytes,ModifiedUtc=item.ModifiedUtc,RelativePath=Path.GetRelativePath(root,target).Replace('\\','/'),Sha256=digest});
                    reserved.Add(target);paths[item.Track.Id]=target;copied++;bytes+=item.Bytes;
                    if(copied%25==0)await SaveManifest();
                }
                progress?.Report(new(index+1,plan.Items.Count,bytes,plan.Bytes,item.Track.DisplayTitle));
            }
            if(paths.Count==0)throw new IOException("Keine lesbaren Club-Tracks exportiert; es wurde keine NML erzeugt.");
            ct.ThrowIfCancellationRequested();
            progress?.Report(new(plan.Items.Count,plan.Items.Count,bytes,plan.Bytes,"NML mit Zielpfaden, Playlisten und Cues erstellen …"));
            await sync.ExportClubWithFilesAsync(nml,paths,ct);
            published=true;
            await SaveManifest();
            await AtomicJson(reportPath,new {ErstelltUtc=DateTime.UtcNow,Status=skipped==0?"Vollständig":"Unvollständig: Quelldateien übersprungen",Ziel=root,Kopiert=copied,Wiederverwendet=reused,Uebersprungen=skipped,Bytes=bytes,Warnungen=warnings,GenreZuordnung=plan.Groups,Dateien=manifest.Files.Where(e=>paths.ContainsKey(e.TrackId)).ToArray(),TrackMetadaten=plan.Items.Where(i=>paths.ContainsKey(i.Track.Id)).Select(i=>new {i.Track.Id,i.Track.Title,i.Track.Artist,i.Track.GenreRaw,i.Track.Extended,i.Track.Intro,Club=true,Ziel=Path.GetRelativePath(root,paths[i.Track.Id])}).ToArray(),NichtMehrAusgewaehlt=manifest.Files.Where(e=>!selected.Contains(e.TrackId)).Select(e=>e.RelativePath).Concat(manifest.RetainedFiles).Distinct().ToArray()},CancellationToken.None);
            return new(nml,copied,reused,skipped,bytes,warnings);
        }
        catch(Exception ex)
        {
            try
            {
                await SaveManifest();
                await AtomicJson(Path.Combine(root,"Cratebase-Export-abgebrochen.json"),new {ZeitUtc=DateTime.UtcNow,Fehler=ex is OperationCanceledException?"Abgebrochen":ex.Message,Kopiert=copied,Wiederverwendet=reused,NmlAktualisiert=published},CancellationToken.None);
            }
            catch(IOException) { }
            catch(UnauthorizedAccessException) { }
            throw;
        }
    }
    private static async Task<string> Hash(string path,CancellationToken ct)
    {if(File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint))throw new IOException("Exportdatei darf keine Verknüpfung sein: "+path);await using var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read,131072,true);return Convert.ToHexString(await SHA256.HashDataAsync(stream,ct));}
    private static string Inside(string root,string relative)
    {
        if(Path.IsPathRooted(relative))throw new IOException("Exportpfad muss relativ sein.");
        var full=Path.GetFullPath(Path.Combine(root,relative.Replace('/',Path.DirectorySeparatorChar)));
        if(!full.StartsWith(root+Path.DirectorySeparatorChar,OperatingSystem.IsWindows()?StringComparison.OrdinalIgnoreCase:StringComparison.Ordinal))throw new IOException("Exportpfad liegt außerhalb des gewählten Zielordners.");
        return full;
    }
    private static void CheckDirectories(string root,string folder)
    {
        for(var current=new DirectoryInfo(folder);current!=null;current=current.Parent)
        {
            if(current.Exists&&current.Attributes.HasFlag(FileAttributes.ReparsePoint))throw new IOException("Export über verknüpfte Ordner ist nicht erlaubt: "+current.FullName);
            if(string.Equals(current.FullName,root,StringComparison.OrdinalIgnoreCase))break;
        }
    }
    private static async Task AtomicJson(string path,object value,CancellationToken ct)
    {
        var temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try {await File.WriteAllTextAsync(temp,JsonSerializer.Serialize(value,Json),ct);File.Move(temp,path,true);}
        finally {if(File.Exists(temp))File.Delete(temp);}
    }
}
