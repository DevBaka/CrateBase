using System.Security.Cryptography;
using System.Text.Json;
using System.Xml.Linq;
using Cratebase.Core.Models;
using Cratebase.Data;
using Cratebase.Data.Repositories;
using Cratebase.Import;

internal static class ClubMediaExportChecks
{
    public static async Task RunAsync()
    {
        var directory=Path.Combine(Path.GetTempPath(),"Cratebase-package-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        var factory=new SqliteConnectionFactory(Path.Combine(directory,"library.db"));DatabaseInitializer.EnsureCreated(factory);
        var tracks=new TrackRepository(factory);var roots=new LibraryRootRepository(factory);var lists=new PlaylistRepository(factory);var query=new LibraryQuery(factory);var state=new WorkspaceStateStore(factory);
        var sync=new CollectionSync(tracks,roots,lists,state,new WorkspaceCheckpoint(factory));var exporter=new ClubMediaExporter(roots,query,sync);
        var paths=new[]{"Beatport/Hard Techno/2020/a.flac","Techno/Hard Techno/2024/b.flac","House/DeepHouse/c.flac","Unknown/missing.flac"}.Select(p=>Path.Combine(directory,"Source",p.Replace('/',Path.DirectorySeparatorChar))).ToArray();
        var genres=new[]{"Techno (Peak Time / Driving)","Hard Techno","",""};
        for(var i=0;i<3;i++){Directory.CreateDirectory(Path.GetDirectoryName(paths[i])!);await File.WriteAllBytesAsync(paths[i],Enumerable.Repeat((byte)(i+1),262144).ToArray());}
        TraktorPath Location(string full){var root=Path.GetPathRoot(full)!;return new(root.TrimEnd('\\','/'),Path.GetDirectoryName(full)![root.Length..].Replace('\\','/').Split('/'),Path.GetFileName(full));}
        var sourceNml=Path.Combine(directory,"source.nml");
        var xml=new XDocument(new XElement("NML",new XAttribute("VERSION",19),new XElement("COLLECTION",new XAttribute("ENTRIES",4),paths.Select((p,i)=>
        {var loc=Location(p);return new XElement("ENTRY",new XAttribute("TITLE",i<2?"Same":"Track "+i),new XAttribute("ARTIST","Artist"),new XElement("LOCATION",new XAttribute("VOLUME",loc.Volume),new XAttribute("VOLUMEID","original-volume"),new XAttribute("DIR",loc.ToLocationDir()),new XAttribute("FILE",loc.FileName)),new XElement("INFO",new XAttribute("GENRE",genres[i])),new XElement("CUE_V2",new XAttribute("NAME","Keep cue"),new XAttribute("TYPE",3),new XAttribute("START",1234)));})),new XElement("PLAYLISTS",new XElement("NODE",new XAttribute("TYPE","FOLDER"),new XAttribute("NAME","$ROOT"),new XElement("SUBNODES",new XAttribute("COUNT",0))))));xml.Save(sourceNml);
        await sync.ApplyAsync(await sync.PreviewAsync(sourceNml));
        var imported=await query.QueryAsync(new(new(LibraryTargetKind.Collection,"All")));await query.SetClubReadyAsync(imported.Select(t=>t.Id).ToArray(),true);
        await query.SetFlagAsync([imported[0].Id],"extended",true);
        var clubRoot=await lists.EnsureClubRootAsync();var group=await lists.CreateAsync("Club groups",PlaylistNodeType.Folder,clubRoot);var playlist=await lists.CreateAsync("Playable set",PlaylistNodeType.Playlist,group.Id);await lists.AddTracksAsync(playlist.Id,imported.Select(t=>t.Id).ToArray());
        var plan=await exporter.PlanAsync();
        Check(plan.Items.Count==4&&plan.Missing==1,"Media export plans all club tracks and explicitly identifies missing sources");
        Check(plan.Groups.Where(g=>g.Genre.Contains("Techno")).All(g=>g.Folder=="Techno/Hard Techno"),"Beatport/Hard Techno and Techno/Hard Techno merge into the same canonical export folder");
        Check(plan.Groups.Any(g=>g.Folder=="House/Deep House"&&g.Warning!=null),"A missing genre tag can use a known source genre folder with a review warning");
        var target=Path.Combine(directory,"Export");
        try{await exporter.ExportAsync(plan,target);throw new Exception("Missing source accepted silently");}catch(IOException){Console.WriteLine("PASS: Missing files require explicit skip consent");}
        var collision=Path.Combine(target,"Techno","Hard Techno","Artist - Same.flac");Directory.CreateDirectory(Path.GetDirectoryName(collision)!);await File.WriteAllTextAsync(collision,"Do not overwrite");
        var before=await state.ReadAsync("traktor-sync-v1");var hashes=paths.Take(3).Select(p=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p)))).ToArray();
        var result=await exporter.ExportAsync(plan,target,true);
        Check(result.Copied==3&&result.Skipped==1&&Path.GetDirectoryName(result.NmlPath)==target,"Full club export copies audio and writes the NML at the selected root");
        Check(await File.ReadAllTextAsync(collision)=="Do not overwrite"&&Directory.GetFiles(Path.GetDirectoryName(collision)!,"*.flac").Length==3,"Name collisions keep existing files and give each different track a unique filename");
        var exported=CollectionSync.Read(result.NmlPath);
        Check(exported.Entities.Values.Count(e=>e.Kind=="track")==3&&exported.Entities.Values.Any(e=>e.Kind=="playlist"&&e.Fields["name"]=="Playable set"),"NML retains the club playlist hierarchy and excludes skipped files");
        foreach(var pair in exported.Entities.Where(p=>p.Value.Kind=="track"))
        {
            var loc=TraktorPath.FromCombinedKey(pair.Key[6..])!;
            var path=Path.Combine(OperatingSystem.IsWindows()?loc.Volume+"\\":"/",Path.Combine(loc.DirSegments.Append(loc.FileName).ToArray()));
            Check(File.Exists(path)&&Path.GetFullPath(path).StartsWith(target,StringComparison.OrdinalIgnoreCase),"Every NML collection path points at a complete exported audio file");
        }
        Check(exported.Entities.Values.Where(e=>e.Kind=="playlist").All(e=>JsonSerializer.Deserialize<string[]>(e.Fields["members"]??"[]")!.All(exported.Entities.ContainsKey)),"All playlist PRIMARYKEY references match the rewritten destination locations");
        var outputXml=XDocument.Load(result.NmlPath);
        Check(outputXml.Descendants("CUE_V2").Count()==3&&!outputXml.Descendants("LOCATION").Any(e=>e.Attribute("VOLUMEID")!=null),"Copied-path export preserves cues and removes stale source-volume identities");
        Check(paths.Take(3).Select(p=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p)))).SequenceEqual(hashes)&&await state.ReadAsync("traktor-sync-v1")==before,"Source audio and the normal Traktor sync baseline are unchanged");
        var repeated=await exporter.ExportAsync(plan,target,true);
        Check(repeated.Reused==3&&repeated.Copied==0,"Repeated export verifies and reuses complete audio copies without duplicates");
        var oldNml=await File.ReadAllBytesAsync(result.NmlPath);var cancelTarget=Path.Combine(directory,"Cancel");using var cancel=new CancellationTokenSource();
        var cancelProgress=new InlineProgress(p=> {if(p.Message.StartsWith("Kopieren:"))cancel.Cancel();});
        try{await exporter.ExportAsync(plan,cancelTarget,true,cancelProgress,cancel.Token);throw new Exception("Cancellation ignored");}catch(OperationCanceledException){Console.WriteLine("PASS: Copy cancellation is observed");}
        Check(!Directory.GetFiles(cancelTarget,"*.partial",SearchOption.AllDirectories).Any()&&!File.Exists(Path.Combine(cancelTarget,ClubMediaExporter.NmlName)),"Cancellation leaves no partial audio or incomplete NML");
        var resumed=await exporter.ExportAsync(plan,cancelTarget,true);
        Check(resumed.Copied+resumed.Reused==3,"Export can complete after a canceled attempt");
        var badGroup=plan.Groups[0];var folder=badGroup.Folder;badGroup.Folder="../Escape";
        try{await exporter.ExportAsync(plan,target,true);throw new Exception("Path escape accepted");}catch(ArgumentException){Console.WriteLine("PASS: Genre folders cannot escape the selected destination");}finally{badGroup.Folder=folder;}
        Check((await File.ReadAllBytesAsync(result.NmlPath)).SequenceEqual(oldNml),"Failed validation preserves the previous completed NML");
        var huge=new ClubExportPlan([plan.Items.First(i=>!i.Missing) with {Bytes=long.MaxValue/2}],plan.Groups);
        try{await exporter.ExportAsync(huge,Path.Combine(directory,"NoSpace"));throw new Exception("Insufficient space accepted");}catch(IOException ex){Check(ex.Message.Contains("Speicher"),"Insufficient destination space is rejected before copying");}
        using(var stop=new CancellationTokenSource())
        {
            var changeGroup=plan.Groups.Single(g=>g.Key==plan.Items.First(i=>!i.Missing).GroupKey);
            var originalFolder=changeGroup.Folder;changeGroup.Folder="Alternate/Genre";
            try{await exporter.ExportAsync(plan,target,true,new InlineProgress(p=> {if(p.Message.StartsWith("Kopieren:"))stop.Cancel();}),stop.Token);throw new Exception("Cancellation ignored");}
            catch(OperationCanceledException){Check((await File.ReadAllBytesAsync(result.NmlPath)).SequenceEqual(oldNml),"Canceling an update preserves the previous usable NML");}
            finally{changeGroup.Folder=originalFolder;}
        }
        var manifest=JsonSerializer.Deserialize<ClubExportManifest>(await File.ReadAllTextAsync(Path.Combine(target,ClubMediaExporter.ManifestName)))!;
        var changed=Path.Combine(target,manifest.Files[0].RelativePath);await File.WriteAllTextAsync(changed,"Changed by user");
        var repaired=await exporter.ExportAsync(plan,target,true);
        Check(repaired.Copied==1&&repaired.Reused==2&&await File.ReadAllTextAsync(changed)=="Changed by user","A changed previous copy is preserved while a fresh uniquely named copy is exported");
        var blocked=Path.Combine(directory,"Blocked");Directory.CreateDirectory(blocked);await File.WriteAllTextAsync(Path.Combine(blocked,ClubMediaExporter.NmlName),"Unmanaged NML");
        try{await exporter.ExportAsync(plan,blocked,true);throw new Exception("Unmanaged NML overwritten");}catch(IOException){Console.WriteLine("PASS: Export refuses to overwrite an unmanaged NML");}
        Check(ClubGenreLayout.SafeName("CON")=="_CON"&&ClubGenreLayout.SafeName("A:B?C")=="A_B_C","Export names are safe for Windows folders and file browsing");
        Console.WriteLine("Club media export fixtures: "+directory);
    }
    private static void Check(bool value,string text){if(!value)throw new Exception("FAIL: "+text);Console.WriteLine("PASS: "+text);}
    private sealed class InlineProgress(Action<ClubExportProgress> action):IProgress<ClubExportProgress>
    {public void Report(ClubExportProgress value)=>action(value);}
}
