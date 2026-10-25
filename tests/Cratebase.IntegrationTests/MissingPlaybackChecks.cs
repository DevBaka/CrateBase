using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;
using Cratebase.App.Desktop.ViewModels;
using Cratebase.Core.Abstractions;
using Cratebase.Core.Models;
using Cratebase.Data;
using Cratebase.Data.Repositories;

internal static class MissingPlaybackChecks
{
    public static void Run()
    {
        AppBuilder.Configure<Cratebase.App.Desktop.App>().UseHeadless(new()).SetupWithoutStarting();
        var directory=Path.Combine(Path.GetTempPath(),"Cratebase-playback-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        var factory=new SqliteConnectionFactory(Path.Combine(directory,"library.db"));DatabaseInitializer.EnsureCreated(factory);
        var tracks=new TrackRepository(factory);var roots=new LibraryRootRepository(factory);var query=new LibraryQuery(factory);var lists=new PlaylistRepository(factory);
        var root=Pump(roots.AddOrGetAsync(directory,"Test music"));
        var queue=Enumerable.Range(0,6).Select(i=>new Track {LibraryRootId=root.Id,RelativePath=$"{i}.wav",Title=$"Track {i+1}",Comment="Keep this metadata"}).ToArray();
        foreach(var track in queue)track.Id=Pump(tracks.UpsertAsync(track));
        foreach(var i in new[]{0,1,2,5})File.WriteAllBytes(Path.Combine(directory,queue[i].RelativePath),[0]);
        Pump(query.SetFlagAsync([queue[3].Id,queue[4].Id],"club",true));Pump(query.SetFlagAsync([queue[3].Id],"extended",true));Pump(query.SetFlagAsync([queue[3].Id],"intro",true));
        var playlist=Pump(lists.CreateAsync("Original order",PlaylistNodeType.Playlist,null));Pump(lists.AddTracksAsync(playlist.Id,queue.Select(t=>t.Id).ToArray()));
        var audio=new Audio();using var player=new WorkspacePlayer(audio,roots,null!,null!,new Stripes(),tracks);var notices=new List<string>();player.ReportError=notices.Add;
        Pump(player.PlayAsync(queue[0],queue));
        audio.End();Until(()=>player.CurrentTrack?.Id==queue[1].Id);
        audio.End();Until(()=>player.CurrentTrack?.Id==queue[2].Id);
        audio.End();Until(()=>player.CurrentTrack?.Id==queue[5].Id);
        Check(audio.State==PlaybackState.Playing && audio.Loads.Count==4,"Natural playback skips the missing fourth and fifth songs and continues with the sixth");
        var missing=new TrackQuery(new(LibraryTargetKind.UnreachableFiles,"Datei nicht erreichbar"));
        Check(Pump(query.QueryAsync(missing)).Count==2 && Pump(query.TotalsAsync(missing)).Count==2,"Dedicated missing-file view and totals include every skipped file");
        Check(Pump(query.QueryAsync(new(new(LibraryTargetKind.Collection,"All"),"Datei nicht erreichbar",SearchField:"availability"))).Count==2,"The unreachable-file tag is searchable metadata");
        var marked=Pump(new TrackRepository(factory).GetByIdAsync(queue[3].Id))!;
        Check(marked.MissingSinceUtc!=null&&marked.ClubReady&&marked.Extended&&marked.Intro&&marked.Comment=="Keep this metadata","Missing mark persists across repository restart without overwriting tags or edit flags");
        Check(Pump(tracks.SearchAsync(null,[playlist.Id],true,100,0)).Select(t=>t.Id).SequenceEqual(queue.Select(t=>t.Id)),"Skipping preserves collection entries and playlist membership/order");
        Pump(player.PreviousCommand.ExecuteAsync(null));
        Check(player.CurrentTrack?.Id==queue[2].Id,"Previous skips unreachable entries backwards without looping forwards");
        Pump(player.NextCommand.ExecuteAsync(null));
        Check(player.CurrentTrack?.Id==queue[5].Id,"Next advances past consecutive unreachable entries");
        var stamp=marked.MissingSinceUtc;
        audio.Stop();var loadCount=audio.Loads.Count;
        Pump(player.PlayAsync(queue[3],[queue[3],queue[4]]));
        Check(!player.CanNext && audio.Loads.Count==loadCount && audio.State==PlaybackState.Stopped,"An entirely missing queue terminates without recursive retry or fictitious playback");
        Check(Pump(tracks.GetByIdAsync(queue[3].Id))!.MissingSinceUtc==stamp,"Repeated failed attempts preserve the original missing timestamp");
        File.WriteAllBytes(Path.Combine(directory,queue[3].RelativePath),[0]);Pump(player.PlayAsync(queue[3],queue));
        Check(player.CurrentTrack?.Id==queue[3].Id && Pump(tracks.GetByIdAsync(queue[3].Id))!.MissingSinceUtc==null && Pump(query.TotalsAsync(missing)).Count==1,"A restored file is retried, plays, and automatically clears its missing mark");
        Pump(player.PlayAsync(queue[4],queue));
        Check(player.CurrentTrack?.Id==queue[5].Id,"Manually opening a missing file also advances to the next playable track");
        if(OperatingSystem.IsWindows())
        {
            using(var locked=new FileStream(Path.Combine(directory,queue[0].RelativePath),FileMode.Open,FileAccess.ReadWrite,FileShare.None))
            {
                Pump(player.PlayAsync(queue[0],queue));
                Check(player.CurrentTrack?.Id==queue[1].Id && Pump(tracks.GetByIdAsync(queue[0].Id))!.MissingSinceUtc!=null,"Existing but exclusively locked files are marked and skipped too");
            }
            Pump(player.PlayAsync(queue[0],queue));
            Check(Pump(tracks.GetByIdAsync(queue[0].Id))!.MissingSinceUtc==null,"Releasing a locked file restores its availability on next playback");
        }
        Check(notices.Any(n=>n.Contains("übersprungen")),"Skipped tracks produce a clear status notification");
    }
    private static T Pump<T>(Task<T> task){Until(()=>task.IsCompleted);return task.GetAwaiter().GetResult();}
    private static void Pump(Task task){Until(()=>task.IsCompleted);task.GetAwaiter().GetResult();}
    private static void Until(Func<bool> condition)
    {var timeout=DateTime.UtcNow.AddSeconds(8);while(!condition()){if(DateTime.UtcNow>timeout)throw new TimeoutException("Missing playback check timed out");Dispatcher.UIThread.RunJobs();Thread.Sleep(10);}Dispatcher.UIThread.RunJobs();}
    private static void Check(bool value,string text){if(!value)throw new Exception(text);Console.WriteLine("PASS: "+text);}
    private sealed class Stripes:ITrackWaveformSource
    {public Task<WaveformData?> TryLoadAsync(Track track,CancellationToken ct=default)=>Task.FromResult<WaveformData?>(new(){Columns=[new(.1f,.2f,.3f)]});}
    private sealed class Audio:IAudioPlayer
    {
        public List<string> Loads {get;}=new();public PlaybackState State {get;private set;}
        public TimeSpan Position=>TimeSpan.Zero;public TimeSpan Duration=>TimeSpan.FromSeconds(180);public float Volume {get;set;}
        public event EventHandler? PlaybackEnded;public event EventHandler? PositionChanged {add{}remove{}}
        public void End(){State=PlaybackState.Stopped;PlaybackEnded?.Invoke(this,EventArgs.Empty);}
        public void Load(string path)=>Loads.Add(path);public void Play()=>State=PlaybackState.Playing;public void Pause()=>State=PlaybackState.Paused;public void Stop()=>State=PlaybackState.Stopped;
        public void Seek(TimeSpan position){}public void Dispose(){}
    }
}
