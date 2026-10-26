using System.Diagnostics;
using System.Text.Json;
using Cratebase.Analysis;
using Cratebase.Core.Models;
using Cratebase.Data;
using Cratebase.Data.Repositories;

internal static class PlanningChecks
{
    public static async Task RunAsync(bool realAi=false,bool cpuOnly=false)
    {
        var directory=Path.Combine(Path.GetTempPath(),"Cratebase-planning-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        var factory=new SqliteConnectionFactory(Path.Combine(directory,"test.db"));DatabaseInitializer.EnsureCreated(factory);
        var tracks=new TrackRepository(factory);var roots=new LibraryRootRepository(factory);var playlists=new PlaylistRepository(factory);var state=new WorkspaceStateStore(factory);var query=new LibraryQuery(factory);
        var root=await roots.AddOrGetAsync(directory,"Fixture");
        Track T(string title,string genre,double bpm,string key,bool club=false)=>new() {LibraryRootId=root.Id,RelativePath=title+".wav",Title=title,Artist="Artist "+title,GenreRaw=genre,Bpm=bpm,MusicalKey=key,DurationMs=180000,ClubReady=club,FileSizeBytes=10000};
        var one=T("One","Hard Techno",145,"8A",true);var two=T("Two","Techno",148,"9A");var three=T("Three","House",125,"Am",true);var four=T("Four","Hardstyle",150,"10B");
        foreach(var track in new[]{one,two,three,four})track.Id=await tracks.UpsertAsync(track);
        await query.SetClubReadyAsync([one.Id,three.Id],true);
        var smart=new SmartPlaylist(Guid.NewGuid().ToString(),"Club Techno",new(LibraryTargetKind.Collection,"Collection"),[new("genre",MetadataOperator.Contains,"techno"),new("club",MetadataOperator.Equals,"ja")]);
        async Task Store(SmartPlaylist value)=>await state.WriteAsync(SmartPlaylist.StateKey,JsonSerializer.Serialize(new[]{value}));
        await Store(smart);Check((await query.QueryAsync(new(smart.Target))).Select(t=>t.Id).SequenceEqual(new[]{one.Id}),"Smart AND rules use persisted definition");
        await query.SetClubReadyAsync([two.Id],true);Check((await query.TotalsAsync(new(smart.Target))).Count==2,"Smart membership updates after a flag change without snapshot rebuild");
        Check((await query.QueryAsync(new(smart.Target,Filters:[new("bpm",MetadataOperator.GreaterOrEqual,"146")]))).Single().Id==two.Id,"Pane filters remain ANDed outside smart rules");
        var any=smart with {MatchAny=true,Rules=[new("genre",MetadataOperator.Contains,"House"),new("genre",MetadataOperator.Contains,"Techno")],Source=new(LibraryTargetKind.ClubCollection,"Club")};await Store(any);
        Check((await query.TotalsAsync(new(any.Target))).Count==3,"OR smart rules respect the source scope");
        Check((await query.QueryAsync(new(any.Target,Limit:1,Offset:1,Sort:"bpm"))).Single().Id==one.Id,"Smart pagination and typed sorting operate in SQL");
        var playlist=await playlists.CreateAsync("Scope",PlaylistNodeType.Playlist,null);await playlists.AddTracksAsync(playlist.Id,[two.Id]);await Store(any with {Source=new(LibraryTargetKind.Playlist,"Scope",playlist.Id)});
        Check((await query.QueryAsync(new(any.Target))).Single().Id==two.Id,"Playlist source limits smart results");
        await Store(smart with {Rules=[new("title",MetadataOperator.Contains,"%' OR 1=1 --")]});Check((await query.TotalsAsync(new(smart.Target))).Count==0,"Rule values are parameterized, not SQL");
        await Store(smart with {Source=smart.Target});await ThrowsAsync(()=>query.QueryAsync(new(smart.Target)),"Nested definitions fail explicitly rather than leaking the collection");
        Check(ClubSetPlanner.Camelot("Am")== (8,'A')&&ClubSetPlanner.Camelot("1m")== (8,'A')&&ClubSetPlanner.Camelot("G# minor")== (1,'A'),"Camelot, note names and Open Key normalize correctly");
        Check(ClubSetPlanner.Assess(one,two,new()).Allowed&&!ClubSetPlanner.Assess(two,four,new()).Allowed,"Default planner separates techno and hardstyle families");
        Check(ClubSetPlanner.Assess(two,four,new(AllowGenreChanges:true)).Allowed,"Open format permits explicit genre changes");
        Check(!ClubSetPlanner.Assess(one,three,new(AllowGenreChanges:true)).Allowed,"BPM limit remains hard in open format");
        two.Trash=true;var sequence=ClubSetPlanner.Generate(new[]{one,two,three,four},new());Check(sequence.All(t=>!t.Trash)&&sequence.Select(t=>t.Id).Distinct().Count()==sequence.Count,"Planner excludes Trash and duplicate tracks");two.Trash=false;
        var large=Enumerable.Range(1,100000).Select(i=>new Track {Id=i,Title="Other",GenreRaw="Pop",DurationMs=180000,Bpm=125}).ToList();
        large[^1].GenreRaw="Hardstyle";large[^1].Artist="Wasted Penguinz";
        large[^2].Trash=true;large[^2].GenreRaw="Hardstyle";
        large[^3].MissingSinceUtc=DateTime.UtcNow;large[^3].GenreRaw="Hardstyle";
        var selected=SetCandidateSelection.ForAi(large,"Hardstyle Wasted Penguinz");
        Check(selected.Count==250&&selected[0].Id==100000,"Large source automatically selects thematic matches beyond the first 5,000 tracks with at most five AI preselection batches");
        Check(selected.All(t=>!t.Trash&&t.MissingSinceUtc==null)&&selected.Select(t=>t.Id).Distinct().Count()==selected.Count,"Automatic shortlist excludes unavailable tracks, Trash and duplicates");
        Check(ClubSetPlanner.Generate(large.Take(6000).ToArray(),new(Minutes:5)).Count==2,"Rule-based planning accepts more than 5,000 candidates");
        using(var canceledSelection=new CancellationTokenSource())
        {canceledSelection.Cancel();await ThrowsAsync(()=>Task.FromResult(SetCandidateSelection.ForAi(large,"Hardstyle",ct:canceledSelection.Token)),"Large candidate selection can be canceled");}
        var folders=new[]{new Track{Id=1,RelativePath="Pop/One.mp3"},new Track{Id=2,RelativePath="Techno/Hard Techno/Two.mp3"},new Track{Id=3,RelativePath="Hardtekk/Three.mp3"},new Track{Id=4,RelativePath="Misc/Four.mp3",GenreRaw="Techno"}};
        Check(SetCandidateSelection.FocusGenreFolders(folders,"Techno Set ohne Pop").Select(t=>t.Id).SequenceEqual(new long[]{2,4}),"Genre focus selects matching folder paths and metadata while ignoring unrelated folders");
        Check(SetCandidateSelection.FocusGenreFolders(folders,"Hardtekk").Single().Id==3,"Hardtekk can be targeted through folder structure without genre tags");
        Check(SetCandidateSelection.FocusGenreFolders(folders,"Hardstyle").Count==folders.Length,"Missing genre folders fall back to the entire source");
        var fragments=new[]{"{\"ids\":[1",",2],\"explanation\":\"Test\"}"};
        var events=string.Join("\n\n",fragments.Select(part=>"data: "+JsonSerializer.Serialize(new {choices=new[]{new {delta=new {content=part}}}})))+"\n\ndata: {\"choices\":[{\"delta\":{},\"finish_reason\":\"stop\"}]}\n\ndata: [DONE]\n\n";
        await using(var stream=new MemoryStream(System.Text.Encoding.UTF8.GetBytes(events)))
        {var updates=0;var answer=await LocalPlaylistAi.ReadCompletionAsync(stream,_=>updates++,CancellationToken.None);Check(updates==2&&LocalPlaylistAi.ParseSuggestion(answer,new HashSet<long>{1,2}).Ids.Count==2,"Streamed local AI answer reports incremental progress and assembles valid IDs");}
        await using(var stream=new MemoryStream(System.Text.Encoding.UTF8.GetBytes("data: {\"choices\":[{\"delta\":{},\"finish_reason\":\"length\"}]}\n")))
            await ThrowsAsync(()=>LocalPlaylistAi.ReadCompletionAsync(stream,null,CancellationToken.None),"Token-limited AI output is reported explicitly instead of parsing incomplete JSON");
        await using(var stream=new MemoryStream(System.Text.Encoding.UTF8.GetBytes("data: {\"choices\":[{\"delta\":{\"content\":\"partial\"}}]}\n")))
            await ThrowsAsync(()=>LocalPlaylistAi.ReadCompletionAsync(stream,null,CancellationToken.None),"Interrupted AI stream does not become a completed suggestion");
        Check(ClubSetPlanner.Generate(new[]{one,two,three},new()).Count==2,"Planner can select connected sequence from several starts");
        var result=LocalPlaylistAi.ParseSuggestion("{\"ids\":[1,2],\"explanation\":\"Test\"}",new HashSet<long>{1,2});Check(result.Ids.Count==2,"Structured AI response accepts only known IDs");
        await ThrowsAsync(()=>Task.FromResult(LocalPlaylistAi.ParseSuggestion("{\"ids\":[999]}",new HashSet<long>{1})),"Hallucinated AI ID is rejected");
        await ThrowsAsync(()=>Task.FromResult(LocalPlaylistAi.ParseSuggestion("{\"ids\":[1,1]}",new HashSet<long>{1})),"Duplicate AI IDs are rejected");
        var marker=SetPlanEntry.For(one);marker.PlayMs=160000;marker.BassTradeMs=175000;marker.FadeOutMs=180000;marker.Validate(one);
        var plan=new SetPlan {Entries=[marker],References=[new("https://example.org/set","Artist – Title")]};await state.WriteAsync(SetPlan.StateKey,JsonSerializer.Serialize(new[]{plan}));
        var restored=JsonSerializer.Deserialize<List<SetPlan>>(await state.ReadAsync(SetPlan.StateKey)??"[]")!.Single();Check(restored.Entries.Single().PlayMs==160000&&restored.References.Count==1,"Plan order, markers and references survive storage");
        var ffmpeg=Environment.GetEnvironmentVariable("CRATEBASE_TEST_FFMPEG")??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Cratebase","tools","ffmpeg.exe");
        if(File.Exists(ffmpeg))
        {
            var sourceA=Path.Combine(directory,"a.wav");var sourceB=Path.Combine(directory,"b.wav");
            await Exec(ffmpeg,["-v","error","-f","lavfi","-i","sine=frequency=90:duration=8","-ac","2",sourceA]);await Exec(ffmpeg,["-v","error","-f","lavfi","-i","sine=frequency=440:duration=8","-ac","2",sourceB]);
            var hash=System.Security.Cryptography.SHA256.HashData(await File.ReadAllBytesAsync(sourceA));
            var a=T("A","Techno",128,"8A");var b=T("B","Techno",130,"9A");a.DurationMs=b.DurationMs=8000;
            var ma=new SetPlanEntry {PlayMs=1000,BassTradeMs=3000,FadeOutMs=5000};var mb=new SetPlanEntry {PlayMs=0,BassTradeMs=3000,FadeOutMs=8000};var output=Path.Combine(directory,"preview.wav");
            await new TransitionRenderer(ffmpeg).RenderAsync(sourceA,sourceB,a,b,ma,mb,output,CancellationToken.None);
            var sourceAfter=await File.ReadAllBytesAsync(sourceA);
            Check(new FileInfo(output).Length>500000&&hash.SequenceEqual(System.Security.Cryptography.SHA256.HashData(sourceAfter)),"Actual FFmpeg transition renders stereo audio without modifying sources");
            using var cancel=new CancellationTokenSource();cancel.Cancel();await ThrowsAsync(()=>new TransitionRenderer(ffmpeg).RenderAsync(sourceA,sourceB,a,b,ma,mb,Path.Combine(directory,"canceled.wav"),cancel.Token),"Canceled transition does not commit output");
            Check(!Directory.GetFiles(directory,"*.partial.wav").Any(),"Canceled transition cleans temporary audio");
        }
        if(realAi)
        {
            using var ai=new LocalPlaylistAi(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Cratebase","ai"));
            ai.CpuOnly=cpuOnly;
            using var timeout=new CancellationTokenSource(TimeSpan.FromMinutes(8));
            var suggestion=await ai.SuggestAsync(new[]{one,two,three,four},"Ein kurzes Techno-Set, 145 bis 148 BPM. Kein House und kein Hardstyle.",[],timeout.Token);
            Check(suggestion.Ids.Count>0&&suggestion.Ids.All(id=>id==one.Id||id==two.Id),$"Actual local Qwen {(cpuOnly?"CPU":"GPU")} inference selects fixture techno tracks");Console.WriteLine(suggestion.Explanation);
            var batch=Enumerable.Range(1,50).Select(i=>new Track {Id=1000+i,Artist="Fixture artist "+i,Title="Fixture track "+i,GenreRaw=i<=20?"Techno":"Pop",Bpm=i<=20?145:100,DurationMs=180000,MusicalKey="8A",Comment=new string('x',100)}).ToArray();
            var watch=Stopwatch.StartNew();var progressCount=0;
            var batchSuggestion=await ai.SuggestAsync(batch,"Kurzes Techno-Set mit genau drei Tracks bei 145 BPM, kein Pop. Return exactly three IDs and a short explanation.",[],timeout.Token,new Progress<string>(message=>{Interlocked.Increment(ref progressCount);Console.WriteLine(message);}));
            Check(batchSuggestion.Ids.Count>0&&batchSuggestion.Ids.All(id=>id>1000&&id<=1020)&&progressCount>0,"Real 50-candidate inference streams progress and selects only eligible fixture genre");
            Console.WriteLine($"Real 50-candidate batch completed in {watch.Elapsed.TotalSeconds:0.0}s ({(cpuOnly?"CPU":"GPU")}).");
        }
        Console.WriteLine("Planning checks complete; fixtures: "+directory);
    }
    private static async Task Exec(string exe,string[] arguments){var info=new ProcessStartInfo(exe){UseShellExecute=false,CreateNoWindow=true};foreach(var arg in arguments)info.ArgumentList.Add(arg);using var process=Process.Start(info)!;await process.WaitForExitAsync();if(process.ExitCode!=0)throw new Exception("FFmpeg fixture failed");}
    private static void Check(bool condition,string text){if(!condition)throw new Exception(text);Console.WriteLine("PASS: "+text);}
    private static async Task ThrowsAsync(Func<Task> work,string text){try {await work();}catch {Console.WriteLine("PASS: "+text);return;}throw new Exception(text);}
}
