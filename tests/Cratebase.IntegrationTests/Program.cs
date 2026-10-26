using System.Diagnostics;
using System.Text.Json;
using System.Xml.Linq;
using Cratebase.Core.Models;
using Cratebase.Core.Abstractions;
using Cratebase.Data;
using Cratebase.Data.Repositories;
using Cratebase.Import;
using Cratebase.Scanning;
using Cratebase.Tagging;

if(args.Contains("--install-local-ai"))
{
    using var ai=new Cratebase.Analysis.LocalPlaylistAi(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Cratebase","ai"));
    await ai.InstallAsync(new Progress<string>(Console.WriteLine),CancellationToken.None);return;
}
if(args.Contains("--install-artwork-ai"))
{
    var ai=new Cratebase.Analysis.LocalArtworkImageAi(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Cratebase","artwork-ai"));
    await ai.InstallAsync(new Progress<string>(Console.WriteLine),CancellationToken.None);return;
}
if(args.Length is 2 or 3&&args[0]=="--artwork-image-checks")
{
    var ai=new Cratebase.Analysis.LocalArtworkImageAi(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Cratebase","artwork-ai"));
    if(args.Contains("--cancel"))
    {
        using var artworkCancel=new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        try{await ai.GenerateAsync(args[1],"cinematic nightclub photograph",.25,42,null,artworkCancel.Token);throw new Exception("Expected image-AI cancellation.");}
        catch(OperationCanceledException){Console.WriteLine("PASS: Active local image-AI job cancels and releases its owned process.");return;}
    }
    byte[] before,after;await using(var artworkSource=File.OpenRead(args[1]))before=await System.Security.Cryptography.SHA256.HashDataAsync(artworkSource);
    var imageOutput=await ai.GenerateAsync(args[1],"cinematic nightclub photography, violet and cyan neon lighting, atmospheric professional DJ promotional photo, realistic",.25,42,new Progress<string>(Console.WriteLine),CancellationToken.None);
    await using(var artworkSource=File.OpenRead(args[1]))after=await System.Security.Cryptography.SHA256.HashDataAsync(artworkSource);
    if(!before.SequenceEqual(after)||!File.Exists(imageOutput)||new FileInfo(imageOutput).Length<10000)throw new Exception("Image AI test failed.");
    Console.WriteLine("PASS: Actual local Vulkan image-to-image generation preserves the original. Output: "+imageOutput);return;
}

if(args.Length==3&&args[0]=="--real-stream-check")
{
    var path=Cratebase.Server.LibraryServer.ContainedPath(args[1],args[2]);
    var cache=new Cratebase.Server.StreamCache(Path.Combine(Path.GetTempPath(),"Cratebase-stream-check-"+Guid.NewGuid().ToString("N")));
    var streamOutput=await cache.PrepareAsync(path,160,new Cratebase.Server.ServerSettings(),CancellationToken.None);
    Console.WriteLine("PASS real library path and MP3 stream preparation: "+new FileInfo(streamOutput).Length+" bytes at 160 kbit/s; source unchanged.");return;
}

if(args.Length==2&&args[0]=="--audit-library") {LibraryAudit.Run(args[1]);return;}
if(args.Length==3&&args[0] is "--preview-club-media" or "--export-club-media") {await ClubExportCli.RunAsync(args[1],args[2],args[0]=="--export-club-media");return;}

if(args.Contains("--recorder-checks")) { RecorderChecks.Run(); return; }
if(args.Contains("--history-checks")){await HistoryAnalysisChecks.RunAsync();return;}
if(args.Contains("--history-ui-checks")){HistoryUiChecks.Run(args.Length==3&&args[1]=="--report"?args[2]:null);return;}
if(args.Contains("--history-player-checks")){HistoryPlayerChecks.Run();return;}
if(args.Contains("--history-video-checks")){HistoryPlayerChecks.Run(true);return;}
if(args.Contains("--artwork-checks")){var videoIndex=Array.IndexOf(args,"--video");ArtworkChecks.Run(args.Contains("--real-ai"),videoIndex>=0?args[videoIndex+1]:null,args.Contains("--real-image-ai"));return;}
if(args.Length==3&&args[0]=="--history-report"){await HistoryAnalysisChecks.ExportReportAsync(args[1],args[2]);return;}
if(args.Length==4&&args[0]=="--history-analyze")
{
    var data=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Cratebase");
    var analyzer=new Cratebase.Analysis.HistoryAudioAnalyzer(Path.Combine(data,"history-analysis"),Path.Combine(data,"tools","ffmpeg.exe"));
    var report=await analyzer.AnalyzeAsync(args[2],TraktorHistoryReader.Read(args[1]),new Progress<string>(Console.WriteLine),CancellationToken.None);
    await File.WriteAllTextAsync(args[3],JsonSerializer.Serialize(report,new JsonSerializerOptions(JsonSerializerDefaults.Web){WriteIndented=true}));return;
}
if(args.Contains("--keyboard-checks")) { KeyboardChecks.Run(); return; }
if(args.Contains("--playback-checks")) { MissingPlaybackChecks.Run(); return; }
if(args.Contains("--club-export-checks")) {await ClubMediaExportChecks.RunAsync();return;}
if(args.Contains("--server-checks")) {await ServerChecks.RunAsync();return;}
if(args.Contains("--remote-playback-checks")) {RemotePlaybackChecks.Run();return;}
if(args.Contains("--planning-checks")) {await PlanningChecks.RunAsync(args.Contains("--real-ai"),args.Contains("--ai-cpu"));return;}
if(args.Contains("--planning-ui-checks")) {PlanningUiChecks.Run();return;}
if(args.Contains("--planning-native-playback-checks")) {PlanningUiChecks.RunNativePlayback();return;}
if(args.Contains("--stem-checks")) {await StemChecks.RunAsync(args.Contains("--real-stems"));return;}
if(args.Length==2&&args[0]=="--inspiration-check")
{
    var inspirationSource=new Cratebase.Analysis.SetInspiration(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Cratebase","planning","research"));
    foreach(var reference in await inspirationSource.FindAsync(args[1],CancellationToken.None))Console.WriteLine(reference.Url+" · "+reference.Tracklist.Split('\n')[0]+" · "+reference.Tracklist.Length+" chars");return;
}
if(args.Contains("--mobile-device-fixture")) {await ServerChecks.ServeDeviceFixtureAsync();return;}
#if WINDOWS
if(args.Contains("--media-checks")) { MediaChecks.Run(); return; }
#endif

if(args.Length==2&&args[0]=="--real-waveforms")
{
    await WaveformChecks.RealStripesAsync(args[1]); return;
}

if(args.Length==2&&args[0]=="--inspect-db")
{
    using var inspect=new Microsoft.Data.Sqlite.SqliteConnection(new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder { DataSource=args[1],Mode=Microsoft.Data.Sqlite.SqliteOpenMode.ReadOnly }.ToString()); inspect.Open();
    foreach(var statement in new[] { "SELECT COUNT(*) FROM track", "SELECT COUNT(*) FROM playlist_node WHERE node_type='playlist'", "SELECT COUNT(*) FROM playlist_track", "SELECT COUNT(*) FROM playlist_node p WHERE p.node_type='playlist' AND NOT EXISTS(SELECT 1 FROM playlist_track pt WHERE pt.playlist_node_id=p.id)", "SELECT COUNT(*) FROM track WHERE missing_since IS NOT NULL" })
    { using var command=inspect.CreateCommand(); command.CommandText=statement; Console.WriteLine(statement+" = "+command.ExecuteScalar()); }
    using var baselineCommand=inspect.CreateCommand(); baselineCommand.CommandText="SELECT json FROM workspace_state WHERE key='traktor-sync-v1'";
    var json=baselineCommand.ExecuteScalar() as string;
    if(json!=null)
    {
        var known=JsonSerializer.Deserialize<SyncState>(json)!;
        Console.WriteLine($"Sync baseline: {known.Entities.Count} entities; {known.Entities.Values.Count(e=>e.LocalId==0)} unresolved IDs.");
    }
    return;
}

var directory=Path.Combine(Path.GetTempPath(),"Cratebase-tests-"+Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(directory);
var factory=new SqliteConnectionFactory(Path.Combine(directory,"library.db"));
DatabaseInitializer.EnsureCreated(factory); DatabaseInitializer.EnsureCreated(factory);
var tracks=new TrackRepository(factory); var roots=new LibraryRootRepository(factory); var playlists=new PlaylistRepository(factory);
var query=new LibraryQuery(factory); var state=new WorkspaceStateStore(factory); var sync=new CollectionSync(tracks,roots,playlists,state,new WorkspaceCheckpoint(factory));
if(args.Length==2&&args[0]=="--real-nml")
{
    var actual=await sync.PreviewAsync(args[1]);
    Console.WriteLine($"Actual NML: {actual.Remote.Entities.Values.Count(e=>e.Kind=="track")} distinct tracks, {actual.Remote.Entities.Values.Count(e=>e.Kind=="playlist")} playlists.");
    await sync.ApplyAsync(actual,progress:new Progress<TransferProgress>(p=> { if(p.Completed%5000==0) Console.WriteLine($"Imported {p.Completed}/{p.Total}"); }));
    var imported=await query.TotalsAsync(new(new(LibraryTargetKind.TraktorCollection,"Traktor")));
    var actualNodes=await playlists.GetAllNodesAsync();
    var linked=0; var expectedLinks=0;
    foreach(var entity in actual.Remote.Entities.Values.Where(e=>e.Kind=="playlist"))
    {
        var keys=JsonSerializer.Deserialize<string[]>(entity.Fields["members"]??"[]")!;
        var expected=keys.Distinct().Count(actual.Remote.Entities.ContainsKey);
        var actualTracks=await tracks.SearchAsync(null,[entity.LocalId],true,1000000,0);
        if(actualTracks.Count!=expected) throw new Exception($"Playlist membership mismatch: expected {expected}, got {actualTracks.Count}");
        linked+=actualTracks.Count; expectedLinks+=expected;
        var paneRows=await query.QueryAsync(new(new(LibraryTargetKind.Playlist,"Test",entity.LocalId)));
        if(paneRows.Count!=Math.Min(expected,500)) throw new Exception("Workspace playlist query lost tracks");
    }
    Console.WriteLine($"PASS actual collection: {imported.Count} tracks, {actualNodes.Count} nodes, {linked}/{expectedLinks} playlist memberships; workspace queries verified.");
    var repeated=await sync.PreviewAsync(args[1]);
    if(repeated.Changes.Count!=0) throw new Exception($"Actual unchanged reimport has {repeated.Changes.Count} changes");
    Console.WriteLine("PASS actual unchanged reimport. Isolated database: "+factory.DatabasePath);
    return;
}
var source=Path.Combine(directory,"source"); Directory.CreateDirectory(source);
var wav=Path.Combine(source,"test.wav");
using(var writer=new BinaryWriter(File.Create(wav)))
{
    writer.Write("RIFF"u8); writer.Write(36+44100*2); writer.Write("WAVEfmt "u8); writer.Write(16); writer.Write((short)1); writer.Write((short)1); writer.Write(44100); writer.Write(88200); writer.Write((short)2); writer.Write((short)16); writer.Write("data"u8); writer.Write(88200); writer.Write(new byte[88200]);
}
var location=Path.GetFullPath(wav); var volume=Path.GetPathRoot(location)!.TrimEnd('\\','/');
var segments=Path.GetDirectoryName(location)![(Path.GetPathRoot(location)?.Length??0)..].Replace('\\','/').Split('/',StringSplitOptions.RemoveEmptyEntries);
var key=new TraktorPath(volume,segments,Path.GetFileName(wav));
var missing=key with { FileName="missing.mp3" };
var input=Path.Combine(directory,"collection.nml");
XDocument Document(string title="First",string playlistName="Warmup") => new(new XElement("NML",new XAttribute("VERSION",19),
    new XElement("COLLECTION",new XAttribute("ENTRIES",2),Entry(key,title),Entry(missing,"Offline")),
    new XElement("PLAYLISTS",new XElement("NODE",new XAttribute("TYPE","FOLDER"),new XAttribute("NAME","$ROOT"),
    new XElement("SUBNODES",new XAttribute("COUNT",1),new XElement("NODE",new XAttribute("TYPE","PLAYLIST"),new XAttribute("NAME",playlistName),
    new XElement("PLAYLIST",new XAttribute("TYPE","LIST"),new XAttribute("UUID","stable-playlist"),new XAttribute("ENTRIES",2),Reference(key),Reference(missing))))))));
XElement Entry(TraktorPath path,string title) => new("ENTRY",new XAttribute("TITLE",title),new XAttribute("ARTIST","Test Artist"),
    new XElement("LOCATION",new XAttribute("VOLUME",path.Volume),new XAttribute("DIR",path.ToLocationDir()),new XAttribute("FILE",path.FileName)),
    new XElement("INFO",new XAttribute("PLAYTIME",1),new XAttribute("BITRATE","320000.000")),new XElement("TEMPO",new XAttribute("BPM","128.000000")),
    new XElement("CUE_V2",new XAttribute("NAME","Load"),new XAttribute("TYPE",3),new XAttribute("START","1200"),new XAttribute("LEN",0),new XAttribute("HOTCUE",0)),
    new XElement("LOUDNESS",new XAttribute("PEAK_DB","-0.1")));
XElement Reference(TraktorPath path) => new("ENTRY",new XElement("PRIMARYKEY",new XAttribute("TYPE","TRACK"),new XAttribute("KEY",path.ToCombinedKey())));
var passed=0;
void Assert(bool condition,string label) { if(!condition) throw new Exception("FAIL: "+label); Console.WriteLine("PASS: "+label); passed++; }
Document().Save(input);
var preview=await sync.PreviewAsync(input); await sync.ApplyAsync(preview);
var all=await tracks.SearchAsync(null,null,true,100,0);
Assert(all.Count==2,"NML import without scanner includes missing tracks");
var present=all.Single(t=>t.Title=="First");
Assert(all.Single(t=>t.Title=="Offline").MissingSinceUtc!=null,"Missing files marked, never deleted");
Assert((await playlists.GetAllNodesAsync()).Count==1,"Playlist hierarchy imported");
Assert((await sync.PreviewAsync(input)).Changes.Count==0,"Unchanged reimport is idempotent and BPM is normalized");
Assert(present.BitrateKbps==320 && present.DurationMs==1000,"NML technical metadata imports bitrate in kbps and duration");
using(var connection=factory.CreateConnection())
{
    using var command=connection.CreateCommand();command.CommandText="UPDATE track SET bitrate_kbps=NULL,duration_ms=0";command.ExecuteNonQuery();
}
await sync.RefreshTechnicalMetadataAsync();
present=(await tracks.GetByIdAsync(present.Id))!;
Assert(present.BitrateKbps==320 && present.DurationMs==1000,"Existing imports recover missing technical metadata without reselecting NML");
await query.SetClubReadyAsync([present.Id],true);
Assert((await query.TotalsAsync(new(new(LibraryTargetKind.ClubCollection,"Club")))).Count==1,"Club filter persists");
var root=(await roots.GetAllAsync()).Single();
var scanner=new LibraryScanner(tracks,roots,new TagLibTagReader());
await tracks.UpsertAsync(present);
Assert((await tracks.GetByIdAsync(present.Id))!.ClubReady,"Rescan/upsert preserves club flag");
present=(await tracks.GetByIdAsync(present.Id))!; present.Title="Local title"; await tracks.UpdateAsync(present);
Document("Remote title","Renamed").Save(input);
preview=await sync.PreviewAsync(input);
Assert(preview.Changes.Any(c=>c.Field=="title"&&c.Conflict),"Three-way field conflict detected");
foreach(var change in preview.Changes.Where(c=>c.Conflict)) change.UseRemote=false;
await sync.ApplyAsync(preview);
Assert((await tracks.GetByIdAsync(present.Id))!.Title=="Local title","Choosing local preserves local edit");
Assert((await playlists.GetAllNodesAsync()).Single().Name=="Renamed","UUID playlist rename does not duplicate playlist");
Assert((await sync.PreviewAsync(input)).Changes.Count==0,"Rejected remote conflict does not reappear unchanged");
var output=Path.Combine(directory,"export.nml"); await sync.ExportAsync(output);
var exported=XDocument.Load(output);
Assert(exported.Descendants("CUE_V2").Count()==2 && exported.Descendants("LOUDNESS").Count()==2,"Cues and unknown ENTRY metadata survive export");
Assert(new TraktorNmlReader().ReadCollection(output).Count()==2,"Export includes missing tracks and can be read by NML reader");
var baseline=await state.ReadAsync("traktor-sync-v1");
Assert(baseline!=await state.ReadAsync("traktor-pending-export"),"Export alone does not advance baseline");
present=(await tracks.GetByIdAsync(present.Id))!; present.Comment="Edited after export"; await tracks.UpdateAsync(present);
await sync.ConfirmExportAsync();
preview=await sync.PreviewAsync(output);
Assert(preview.Changes.Count==0,"Confirmed export reimport preserves newer local edits");
await sync.ApplyAsync(preview);
Assert((await tracks.GetByIdAsync(present.Id))!.Comment=="Edited after export","Post-export edit retained after apply");
var target=Path.Combine(directory,"target"); Directory.CreateDirectory(target);
var actions=new TargetActions(tracks,roots,playlists,new TagLibTagReader(),query);
await actions.AddAsync(new(LibraryTargetKind.Directory,"Target",Path:target),[present],CopyCollision.Skip,null,CancellationToken.None);
Assert(File.Exists(Path.Combine(target,"test.wav")),"Folder target copies real audio file");
var copyRoot=(await roots.GetAllAsync()).OrderByDescending(r=>r.Path.Length).First(r=>Path.Combine(target,"test.wav").StartsWith(r.Path,StringComparison.OrdinalIgnoreCase));
var copied=await tracks.FindByPathAsync(copyRoot.Id,Path.GetRelativePath(copyRoot.Path,Path.Combine(target,"test.wav")).Replace('\\','/'));
Assert(copied?.ClubReady==true,"Copied file retains club mark");
await actions.AddAsync(new(LibraryTargetKind.Directory,"Target",Path:target),[present],CopyCollision.Skip,null,CancellationToken.None);
Assert(Directory.GetFiles(target,"*.wav").Length==1,"Skip collision never overwrites");
await actions.AddAsync(new(LibraryTargetKind.Directory,"Target",Path:target),[present],CopyCollision.KeepBoth,null,CancellationToken.None);
Assert(Directory.GetFiles(target,"*.wav").Length==2,"Keep both creates unique name");
using(var cancel=new CancellationTokenSource())
{
    cancel.Cancel();
    try { await actions.AddAsync(new(LibraryTargetKind.Directory,"Target",Path:target),[present],CopyCollision.KeepBoth,null,cancel.Token); } catch(OperationCanceledException) { }
    Assert(Directory.GetFiles(target,"*.partial").Length==0 && Directory.GetFiles(target,"*.wav").Length==2,"Cancellation leaves no partial or extra file");
}
var playlist=(await playlists.GetAllNodesAsync()).Single();
await actions.AddAsync(new(LibraryTargetKind.Playlist,playlist.Name,playlist.Id),[present,present],CopyCollision.Skip,null,CancellationToken.None);
Assert((await tracks.SearchAsync(null,[playlist.Id],true,100,0)).Count==2,"Playlist add deduplicates references");
var favoritesPath=Path.Combine(directory,"favorite_profiles.json");
await File.WriteAllTextAsync(favoritesPath,"{\"Profiles\":[{\"Name\":\"Old\",\"Slots\":{\"0\":1}}],\"SlotCount\":12}");
var favorites=await new JsonFavoriteProfileStore(favoritesPath).LoadAsync();
Assert(favorites.Profiles.Single().Slots[0]==1 && favorites.Profiles.Single().Targets.Count==0,"Legacy favorite profiles remain readable");
var restartedSync=new CollectionSync(tracks,roots,playlists,state,new WorkspaceCheckpoint(factory));
var rememberedSource=await sync.GetSourcePathAsync();
Assert(await restartedSync.GetSourcePathAsync()==rememberedSource,"NML source persists across service/app restarts");
var invalid=Path.Combine(directory,"invalid.nml"); await File.WriteAllTextAsync(invalid,"<other />");
try { await restartedSync.PreviewAsync(invalid); throw new Exception("Invalid source accepted"); } catch(InvalidDataException) { }
Assert(await restartedSync.GetSourcePathAsync()==rememberedSource,"Invalid NML does not replace the saved source");
var fullPending=await state.ReadAsync("traktor-pending-export"); var fullBaseline=await state.ReadAsync("traktor-sync-v1");
var clubOutput=Path.Combine(directory,"club-only.nml");
await query.SetClubReadyAsync([copied!.Id],false);
var allSmall=await tracks.SearchAsync(null,null,true,100,0);
await query.SetClubReadyAsync(allSmall.Where(t=>t.Id!=present.Id).Select(t=>t.Id).ToArray(),false);
await restartedSync.ExportClubAsync(clubOutput);
var clubRead=CollectionSync.Read(clubOutput);
Assert(clubRead.Entities.Values.Count(e=>e.Kind=="track")==1,"Club NML exports only marked tracks");
Assert(clubRead.Entities.Values.Any(e=>e.Kind=="playlist"&&e.Fields.GetValueOrDefault("name")=="Club Collection"),"Club export includes a dedicated Club Collection playlist");
Assert(clubRead.Entities.Values.Where(e=>e.Kind=="playlist").All(e=>JsonSerializer.Deserialize<string[]>(e.Fields["members"]??"[]")!.All(clubRead.Entities.ContainsKey)),"Club playlist references never include non-club tracks");
Assert(XDocument.Load(clubOutput).Descendants("CUE_V2").Count()==1,"Club export preserves selected track cues");
Assert(await state.ReadAsync("traktor-pending-export")==fullPending&&await state.ReadAsync("traktor-sync-v1")==fullBaseline,"Club export leaves full sync baseline and pending export untouched");
Assert((await query.TotalsAsync(new(new(LibraryTargetKind.TraktorCollection,"Traktor")))).Count==2,"Traktor Collection displays imported tracks separately from copied local files");
// Reproduce the old importer: it made all playlists but linked no songs without a prior scan.
var legacyFactory=new SqliteConnectionFactory(Path.Combine(directory,"legacy.db")); DatabaseInitializer.EnsureCreated(legacyFactory);
var legacyTracks=new TrackRepository(legacyFactory); var legacyRoots=new LibraryRootRepository(legacyFactory); var legacyPlaylists=new PlaylistRepository(legacyFactory);
await new TraktorCollectionImporter(legacyTracks,legacyRoots,legacyPlaylists,new CuePointRepository(legacyFactory)).ImportAsync(input,null);
var legacyPlaylist=(await legacyPlaylists.GetAllNodesAsync()).Single(n=>n.NodeType==PlaylistNodeType.Playlist);
Assert((await legacyTracks.SearchAsync(null,[legacyPlaylist.Id],true,100,0)).Count==0,"Reproduced legacy playlists without songs");
var legacyState=new WorkspaceStateStore(legacyFactory);
var legacySync=new CollectionSync(legacyTracks,legacyRoots,legacyPlaylists,legacyState,new WorkspaceCheckpoint(legacyFactory));
var repair=await legacySync.PreviewAsync(input);
Assert(repair.Changes.All(c=>!c.Conflict),"Empty legacy playlist contents can be repaired without false conflicts");
await legacySync.ApplyAsync(repair);
Assert((await legacyPlaylists.GetAllNodesAsync()).Count(n=>n.NodeType==PlaylistNodeType.Playlist)==1&&(await legacyPlaylists.GetAllNodesAsync()).Single(n=>n.NodeType==PlaylistNodeType.Playlist).Id==legacyPlaylist.Id,"Repair preserves playlist IDs and does not create duplicates");
Assert((await legacyTracks.SearchAsync(null,[legacyPlaylist.Id],true,100,0)).Count==2,"Repair restores every local playlist track including missing files");
Assert((await legacySync.PreviewAsync(input)).Changes.Count==0,"Repaired legacy playlists remain stable on subsequent sync");
// Seed 100k rows efficiently in one transaction for realistic SQL paging checks.
using(var connection=factory.CreateConnection())
using(var transaction=connection.BeginTransaction())
{
    var command=connection.CreateCommand(); command.Transaction=transaction;
    command.CommandText="INSERT INTO track(library_root_id,relative_path,file_size,file_mtime,format,title,artist,date_added,date_modified) VALUES(@root,@path,1000,'2026-01-01','mp3',@title,'Performance','2026-01-01','2026-01-01')";
    command.Parameters.AddWithValue("@root",root.Id); var pathParameter=command.Parameters.AddWithValue("@path",""); var titleParameter=command.Parameters.AddWithValue("@title",""); command.Prepare();
    for(var i=0;i<100_000;i++) { pathParameter.Value=$"perf/{i}.mp3"; titleParameter.Value=$"Track {i:D6}"; command.ExecuteNonQuery(); }
    transaction.Commit();
}
var watch=Stopwatch.StartNew();
var page=await query.QueryAsync(new(new(LibraryTargetKind.Collection,"All"),"Track 099","title",Limit:500));
var totals=await query.TotalsAsync(new(new(LibraryTargetKind.Collection,"All"),"Track 099")); watch.Stop();
Assert(page.Count==500 && totals.Count==1000,"100k library search and pagination are correct");
Assert(watch.ElapsedMilliseconds<3000,$"100k search plus totals completes in {watch.ElapsedMilliseconds}ms (<3s)");
await LibraryWorkflowChecks.RunAsync();
await WaveformChecks.RunAsync();
Console.WriteLine($"{passed} integration checks passed. Test artifacts: {directory}");
if(args.Contains("--demo"))
{
    // A separate small database for UI inspection, never the production library.
    Console.WriteLine("Demo fixture is available via generated NML: "+input);
}
