#if WINDOWS
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;
using Cratebase.App.Desktop.Services;
using Cratebase.App.Desktop.ViewModels;
using Cratebase.Core.Abstractions;
using Cratebase.Core.Models;
using Cratebase.Data;
using Cratebase.Data.Repositories;
using Windows.Media.Control;

internal static class MediaChecks
{
    public static void Run()
    {
        AppBuilder.Configure<Cratebase.App.Desktop.App>().UseHeadless(new()).SetupWithoutStarting();
        SetCurrentProcessExplicitAppUserModelID("Cratebase.MediaTests");
        var hwnd=CreateWindowExW(0,"STATIC","Cratebase media test",0,0,0,100,100,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero);
        if(hwnd==IntPtr.Zero) throw new Exception("Test HWND creation failed");
        try
        {
            var directory=Path.Combine(Path.GetTempPath(),"Cratebase-media-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
            var factory=new SqliteConnectionFactory(Path.Combine(directory,"test.db"));DatabaseInitializer.EnsureCreated(factory);
            var roots=new LibraryRootRepository(factory);var root=Pump(roots.AddOrGetAsync(directory,"Test"));
            var queue=new[]{new Track {Id=1,LibraryRootId=root.Id,RelativePath="one.wav",Title="Media test one",Artist="Cratebase"},new Track {Id=2,LibraryRootId=root.Id,RelativePath="two.wav",Title="Media test two",Artist="Cratebase"}};
            foreach(var track in queue)File.WriteAllBytes(Path.Combine(directory,track.RelativePath),[0]);
            var audio=new Audio();using var player=new WorkspacePlayer(audio,roots,null!,null!,new Stripes());
            var tagCalls=new List<string>();
            using var media=new WindowsMediaSession(hwnd,player,field=> {tagCalls.Add(field);return Task.CompletedTask;});
            Pump(player.PlayAsync(queue[0],queue)); Dispatcher.UIThread.RunJobs();
            var manager=Pump(GlobalSystemMediaTransportControlsSessionManager.RequestAsync().AsTask());
            GlobalSystemMediaTransportControlsSession? session=null;
            Until(()=> { session=manager.GetSessions().FirstOrDefault(s=>s.SourceAppUserModelId=="Cratebase.MediaTests");return session!=null; });
            Check(session!=null,"Windows discovers Cratebase as a media session");
            Check(Pump(session!.TryGetMediaPropertiesAsync().AsTask()).Title=="Media test one","Windows displays the active track metadata");
            Pump(session.TryPauseAsync().AsTask());Until(()=>audio.State==PlaybackState.Paused);
            Check(audio.State==PlaybackState.Paused,"Windows Pause reaches the existing player");
            Pump(session.TryPlayAsync().AsTask());Until(()=>audio.State==PlaybackState.Playing);
            Check(audio.State==PlaybackState.Playing,"Windows Play resumes the existing player");
            Pump(session.TrySkipNextAsync().AsTask());Until(()=>player.CurrentTrack?.Id==2);
            Check(player.CurrentTrack?.Id==2,"Windows Next advances the original playback queue");
            Pump(session.TrySkipPreviousAsync().AsTask());Until(()=>player.CurrentTrack?.Id==1);
            Check(player.CurrentTrack?.Id==1,"Windows Previous returns to the previous track");
            media.SetTagMode(true);
            Pump(session.TrySkipPreviousAsync().AsTask());Until(()=>tagCalls.Count==1);
            Pump(session.TryPauseAsync().AsTask());Until(()=>tagCalls.Count==2);
            Pump(session.TrySkipNextAsync().AsTask());Until(()=>tagCalls.Count==3);
            Check(tagCalls.SequenceEqual(new[]{"club","extended","intro"}),"Tag mode maps Previous / Play-Pause / Next to Club / Extended / Intro");
            Check(player.CurrentTrack?.Id==1 && audio.State==PlaybackState.Playing,"Tag mode preserves playback and the current track at queue boundaries");
            player.PauseCommand.Execute(null);Dispatcher.UIThread.RunJobs();
            Pump(session.TryPlayAsync().AsTask());Until(()=>tagCalls.Count==4);
            Check(tagCalls[3]=="extended" && audio.State==PlaybackState.Paused,"Both Windows Play and Pause requests toggle Extended in tag mode");
            media.SetTagMode(false);
            Pump(session.TrySkipNextAsync().AsTask());Until(()=>player.CurrentTrack?.Id==2);
            Check(tagCalls.Count==4,"Switching back restores transport actions without changing tags");
        }
        finally { DestroyWindow(hwnd); }
    }
    private static T Pump<T>(Task<T> task) { Until(()=>task.IsCompleted);return task.GetAwaiter().GetResult(); }
    private static void Pump(Task task) { Until(()=>task.IsCompleted);task.GetAwaiter().GetResult(); }
    private static void Until(Func<bool> condition)
    {
        var timeout=DateTime.UtcNow.AddSeconds(10);
        while(!condition()) { if(DateTime.UtcNow>timeout)throw new TimeoutException("Windows media integration timed out");Dispatcher.UIThread.RunJobs();Thread.Sleep(15); }
        Dispatcher.UIThread.RunJobs();
    }
    private static void Check(bool condition,string message) { if(!condition)throw new Exception(message);Console.WriteLine("PASS: "+message); }
    private sealed class Stripes : ITrackWaveformSource
    {public Task<WaveformData?> TryLoadAsync(Track track,CancellationToken ct=default)=>Task.FromResult<WaveformData?>(new() {Columns=[new(.1f,.2f,.3f)]});}
    private sealed class Audio : IAudioPlayer
    {
        public PlaybackState State {get;private set;}
        public TimeSpan Position=>TimeSpan.Zero;public TimeSpan Duration=>TimeSpan.FromSeconds(180);public float Volume {get;set;}
        public event EventHandler? PlaybackEnded { add {} remove {} }
        public event EventHandler? PositionChanged;
        public void Load(string path) {}
        public void Play(){State=PlaybackState.Playing;PositionChanged?.Invoke(this,EventArgs.Empty);}
        public void Pause(){State=PlaybackState.Paused;PositionChanged?.Invoke(this,EventArgs.Empty);}
        public void Stop(){State=PlaybackState.Stopped;PositionChanged?.Invoke(this,EventArgs.Empty);}
        public void Seek(TimeSpan position) {}public void Dispose() {}
    }
    [DllImport("shell32.dll",CharSet=CharSet.Unicode)]private static extern int SetCurrentProcessExplicitAppUserModelID(string id);
    [DllImport("user32.dll",CharSet=CharSet.Unicode,SetLastError=true)]private static extern IntPtr CreateWindowExW(int exStyle,string className,string title,int style,int x,int y,int width,int height,IntPtr parent,IntPtr menu,IntPtr instance,IntPtr param);
    [DllImport("user32.dll")]private static extern bool DestroyWindow(IntPtr hwnd);
}
#endif
