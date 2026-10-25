using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;
using Cratebase.App.Desktop.Services;
using Cratebase.App.Desktop.ViewModels;
using Cratebase.Core.Abstractions;
using Cratebase.Core.Models;
using Cratebase.Data;
using Cratebase.Data.Repositories;

internal static class RemotePlaybackChecks
{
    public static void Run()
    {
        AppBuilder.Configure<Cratebase.App.Desktop.App>().UseHeadless(new()).SetupWithoutStarting();
        var folder=Path.Combine(Path.GetTempPath(),"Cratebase-remote-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
        var factory=new SqliteConnectionFactory(Path.Combine(folder,"test.db"));DatabaseInitializer.EnsureCreated(factory);
        var roots=new LibraryRootRepository(factory);var tracks=new TrackRepository(factory);var root=Pump(roots.AddOrGetAsync(folder,"Test"));
        var queue=Enumerable.Range(0,3).Select(i=>new Track{LibraryRootId=root.Id,RelativePath=i+".wav",Title="Track "+i,DurationMs=180000}).ToArray();
        foreach(var track in queue){File.WriteAllBytes(Path.Combine(folder,track.RelativePath),[0]);track.Id=Pump(tracks.UpsertAsync(track));}
        var audio=new Audio();using var player=new WorkspacePlayer(audio,roots,null!,null!,new Stripes(),tracks);var bridge=new DesktopPlaybackBridge(player);
        Pump(bridge.LoadAsync(queue,1,45000,true,default));var state=Pump(bridge.ReadAsync(default));
        Check(state.TrackId==queue[1].Id&&state.Playing&&state.Queue.SequenceEqual(queue.Select(t=>t.Id))&&state.Index==1,"Desktop bridge loads the selected queue and exposes real player state");
        Check(audio.Position.TotalMilliseconds==45000,"Playback transfer resumes at the supplied position");
        Pump(bridge.CommandAsync(new("pause"),default));Check(!Pump(bridge.ReadAsync(default)).Playing,"Remote pause controls the desktop engine");
        Pump(bridge.CommandAsync(new("play"),default));Pump(bridge.CommandAsync(new("seek",90000),default));Pump(bridge.CommandAsync(new("volume",Volume:42),default));
        Check(audio.State==PlaybackState.Playing&&audio.Position.TotalSeconds==90&&Math.Abs(audio.Volume-.42)<.001,"Remote play, seeking and volume reach the existing audio engine");
        Pump(bridge.CommandAsync(new("next"),default));Until(()=>player.CurrentTrack?.Id==queue[2].Id);
        Pump(bridge.CommandAsync(new("previous"),default));Until(()=>player.CurrentTrack?.Id==queue[1].Id);
        Check(true,"Remote previous/next use the desktop queue");
        try{Pump(bridge.CommandAsync(new("pause",ExpectedTrackId:queue[0].Id),default));throw new Exception("Stale transfer paused another track");}catch(ArgumentException){Check(audio.State==PlaybackState.Playing,"A stale device handoff cannot pause a newly selected desktop track");}
        player.PauseCommand.Execute(null);Check(!Pump(bridge.ReadAsync(default)).Playing,"Changes made directly on desktop are visible to the remote client");
    }
    private static T Pump<T>(Task<T> task){Until(()=>task.IsCompleted);return task.GetAwaiter().GetResult();}
    private static void Pump(Task task){Until(()=>task.IsCompleted);task.GetAwaiter().GetResult();}
    private static void Until(Func<bool> condition){var end=DateTime.UtcNow.AddSeconds(8);while(!condition()){if(DateTime.UtcNow>end)throw new TimeoutException();Dispatcher.UIThread.RunJobs();Thread.Sleep(10);}Dispatcher.UIThread.RunJobs();}
    private static void Check(bool value,string text){if(!value)throw new Exception(text);Console.WriteLine("PASS: "+text);}
    private sealed class Stripes:ITrackWaveformSource{public Task<WaveformData?> TryLoadAsync(Track track,CancellationToken ct=default)=>Task.FromResult<WaveformData?>(new(){Columns=[new(.1f,.2f,.3f)]});}
    private sealed class Audio:IAudioPlayer
    {
        public PlaybackState State{get;private set;}public TimeSpan Position{get;private set;}public TimeSpan Duration=>TimeSpan.FromSeconds(180);public float Volume{get;set;}
        public event EventHandler? PlaybackEnded{add{}remove{}}public event EventHandler? PositionChanged;
        public void Load(string path){Position=TimeSpan.Zero;}public void Play(){State=PlaybackState.Playing;PositionChanged?.Invoke(this,EventArgs.Empty);}public void Pause(){State=PlaybackState.Paused;PositionChanged?.Invoke(this,EventArgs.Empty);}public void Stop()=>State=PlaybackState.Stopped;
        public void Seek(TimeSpan value){Position=value;PositionChanged?.Invoke(this,EventArgs.Empty);}public void Dispose(){}
    }
}
