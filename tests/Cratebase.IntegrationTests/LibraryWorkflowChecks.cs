using System.Text.Json;
using Cratebase.Core.Models;
using Cratebase.Data;
using Cratebase.Data.Repositories;
using Cratebase.Import;

internal static class LibraryWorkflowChecks
{
    public static async Task RunAsync()
    {
        var directory=Path.Combine(Path.GetTempPath(),"Cratebase-workflow-"+Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        var factory=new SqliteConnectionFactory(Path.Combine(directory,"library.db")); DatabaseInitializer.EnsureCreated(factory);
        var roots=new LibraryRootRepository(factory); var tracks=new TrackRepository(factory); var lists=new PlaylistRepository(factory); var query=new LibraryQuery(factory);
        var root=await roots.AddOrGetAsync(directory,"Music");
        var records=new[]{
            new Track {LibraryRootId=root.Id,RelativePath="a.mp3",Title="A",Format="mp3",BitrateKbps=96,Bpm=99,Comment="100% intro",DurationMs=180000,DateAddedUtc=new DateTime(2025,1,1,0,0,0,DateTimeKind.Utc)},
            new Track {LibraryRootId=root.Id,RelativePath="b.mp3",Title="B",Format="mp3",BitrateKbps=320,Bpm=128,Comment="Club",DurationMs=360000,DateAddedUtc=new DateTime(2026,1,1,0,0,0,DateTimeKind.Utc)},
            new Track {LibraryRootId=root.Id,RelativePath="c.flac",Title="C",Format="flac",BitrateKbps=1000,Bpm=140,DurationMs=300000,DateAddedUtc=new DateTime(2026,1,2,0,0,0,DateTimeKind.Utc)}
        };
        foreach(var t in records)t.Id=await tracks.UpsertAsync(t);
        var all=new LibraryTarget(LibraryTargetKind.Collection,"All");
        Check((await query.QueryAsync(new(all,Sort:"bitrate"))).Select(t=>t.BitrateKbps).SequenceEqual(new int?[]{96,320,1000}),"Bitrate sorting is numeric");
        await query.SetFlagAsync([records[1].Id],"extended",true);await query.SetFlagAsync([records[1].Id],"intro",true);
        await query.SetFlagAsync([records[1].Id],"trash",true);
        Check((await query.QueryAsync(new(all,Filters:[new("trash",MetadataOperator.Equals,"ja")]))).Single().Id==records[1].Id,"Trash is a searchable independent boolean filter");
        var filtered=new TrackQuery(all,Sort:"bpm",Filters:[new("format",MetadataOperator.Equals,"MP3"),new("bitrate",MetadataOperator.GreaterOrEqual,"300"),new("extended",MetadataOperator.Equals,"ja")]);
        Check((await query.QueryAsync(filtered)).Single().Id==records[1].Id && (await query.TotalsAsync(filtered)).Count==1,"Combined format, bitrate and edit filters agree with totals");
        Check((await query.QueryAsync(new(all,"100%",SearchField:"comment"))).Single().Id==records[0].Id,"Metadata search escapes literal LIKE wildcards");
        Check((await query.QueryAsync(new(all,Filters:[new("added",MetadataOperator.GreaterOrEqual,"2026-01-01")]))).Count==2,"Date metadata filters work");
        try { await query.QueryAsync(new(all,Sort:"title; DROP TABLE track")); throw new Exception("Unsafe field accepted"); }catch(ArgumentException) {Console.WriteLine("PASS: Unknown sort fields are rejected");}
        await tracks.UpsertAsync(records[1]);
        var retained=await tracks.GetByIdAsync(records[1].Id);
        Check(retained!.Extended && retained.Intro && retained.Trash && !retained.ClubReady,"Independent edit and Trash flags survive a rescan");
        var clubRoot=await lists.EnsureClubRootAsync();
        Check(await lists.EnsureClubRootAsync()==clubRoot,"Club collection root persists without duplicates");
        var group=await lists.CreateAsync("Peak time",PlaylistNodeType.Folder,clubRoot);
        var club=await lists.CreateAsync("Club playlist",PlaylistNodeType.Playlist,group.Id);
        var regular=await lists.CreateAsync("Regular playlist",PlaylistNodeType.Playlist,null);
        await lists.AddTracksAsync(regular.Id,[records[0].Id]);
        await query.SetClubReadyAsync([records[0].Id,records[1].Id],true);
        var unsorted=new TrackQuery(new(LibraryTargetKind.ClubUnsorted,"Nicht einsortiert",clubRoot));
        Check((await query.QueryAsync(unsorted)).Count==2,"Membership in a regular playlist does not hide club tracks from Unsorted");
        await lists.AddTracksAsync(club.Id,[records[1].Id,records[2].Id]);
        Check((await query.QueryAsync(unsorted)).Single().Id==records[0].Id,"Nested club playlists remove tracks from Unsorted");
        Check((await tracks.GetByIdAsync(records[2].Id))!.ClubReady,"Adding to a club playlist sets the club mark");
        await lists.RemoveTrackAsync(club.Id,records[1].Id);
        Check((await query.QueryAsync(unsorted)).Count==2 && (await tracks.GetByIdAsync(records[1].Id))!.ClubReady,"Removing a club membership returns the track to Unsorted and keeps its mark");
        await query.SetClubReadyAsync([records[2].Id],false);
        Check((await query.QueryAsync(new(new(LibraryTargetKind.Playlist,"Club",club.Id)))).Count==0,"Unmarked tracks disappear from club playlist views");
        await lists.MoveAsync(club.Id,clubRoot);
        Check(!(await tracks.GetByIdAsync(records[2].Id))!.ClubReady,"Moving within Club Collection preserves explicit unchecked marks");
        await lists.MoveAsync(regular.Id,group.Id);
        Check((await lists.GetAllNodesAsync()).Single(n=>n.Id==regular.Id).ParentId==group.Id && (await query.QueryAsync(unsorted)).Single().Id==records[1].Id,"Moving a populated playlist into a group preserves references and updates Unsorted");
        try { await lists.MoveAsync(group.Id,regular.Id);throw new Exception("Playlist accepted as parent"); }catch(InvalidOperationException) { Console.WriteLine("PASS: Only groups accept playlists"); }
        var nested=await lists.CreateAsync("Nested",PlaylistNodeType.Folder,group.Id);
        try { await lists.MoveAsync(group.Id,nested.Id);throw new Exception("Cycle accepted"); }catch(InvalidOperationException) { Console.WriteLine("PASS: Group cycles are rejected"); }
        var state=new WorkspaceStateStore(factory); var sync=new CollectionSync(tracks,roots,lists,state,new WorkspaceCheckpoint(factory));
        var full=Path.Combine(directory,"full.nml"); await sync.ExportAsync(full); await sync.ConfirmExportAsync();
        Check(CollectionSync.Read(full).Entities.Values.All(e=>e.Kind=="track"),"Club playlists stay out of the regular Traktor export");
        var baseline=await state.ReadAsync("traktor-sync-v1");
        var output=Path.Combine(directory,"club.nml"); await sync.ExportClubAsync(output);var exported=CollectionSync.Read(output);
        var exportLists=exported.Entities.Values.Where(e=>e.Kind=="playlist").ToArray();
        Check(exportLists.Any(e=>e.Fields["name"]=="Club playlist") && exported.Entities.Values.Any(e=>e.Kind=="folder"&&e.Fields["name"]=="Peak time"),"Club export retains nested groups and empty playlists");
        Check(exportLists.Single(e=>e.Fields["name"]=="Nicht einsortiert").Fields["members"] is {} members && JsonSerializer.Deserialize<string[]>(members)!.Length==1,"Club export includes the current Unsorted playlist");
        Check(exported.Entities.Values.Count(e=>e.Kind=="track")==2 && await state.ReadAsync("traktor-sync-v1")==baseline,"Club export contains only marked tracks and does not advance the full baseline");
        await sync.ExportClubAsync(output);
        Check(CollectionSync.Read(output).Entities.Keys.Order().SequenceEqual(exported.Entities.Keys.Order()),"Repeated club exports keep stable playlist identities");
        var preview=await sync.PreviewAsync(full);await sync.ApplyAsync(preview);
        Check((await lists.GetClubNodeIdsAsync()).Contains(club.Id) && (await lists.GetAllNodesAsync()).Single(n=>n.Id==regular.Id).ParentId==group.Id,"Regular Traktor sync leaves the club hierarchy intact");
    }
    private static void Check(bool condition,string text)
    { if(!condition) throw new Exception("FAIL: "+text);Console.WriteLine("PASS: "+text); }
}

