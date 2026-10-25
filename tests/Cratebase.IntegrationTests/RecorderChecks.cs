using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using Cratebase.App.Desktop.ViewModels;
using Cratebase.App.Desktop.Views;
using Cratebase.Audio;
using Cratebase.Core.Abstractions;
using Cratebase.Core.Models;
using Cratebase.Data;
using Cratebase.Data.Repositories;
using Cratebase.Import;
using Cratebase.Scanning;
using Cratebase.Tagging;

internal static class RecorderChecks
{
    public static void Run()
    {
        AppBuilder.Configure<Cratebase.App.Desktop.App>().UseHeadless(new()).SetupWithoutStarting();
        var engine=new Recorder();
        var vm=new MainViewModel(null!,null!,null!,null!,null!,null!,null!,null!,null!,null!,null!,null!,engine,null!,false);
        var directory=Path.Combine(Path.GetTempPath(),"Cratebase-recorder-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var factory=new SqliteConnectionFactory(Path.Combine(directory,"test.db")); DatabaseInitializer.EnsureCreated(factory);
        var tracks=new TrackRepository(factory); var roots=new LibraryRootRepository(factory); var playlists=new PlaylistRepository(factory);
        var state=new WorkspaceStateStore(factory);
        var player=new WorkspacePlayer(new Audio(),roots,null!,null!,new Stripes(),tracks);
        var actions=new TargetActions(tracks,roots,playlists,new TagLibTagReader(),new LibraryQuery(factory));
        var sync=new CollectionSync(tracks,roots,playlists,state,new WorkspaceCheckpoint(factory));
        var window=new WorkspaceWindow(tracks,roots,playlists,new LibraryQuery(factory),actions,sync,state,new JsonFavoriteProfileStore(Path.Combine(directory,"favorites.json")),new JsonQuickAccessStore(Path.Combine(directory,"quick.json")),null!,null!,player,vm,new FfmpegAudioRecorder(),new ClubMediaExporter(roots,new LibraryQuery(factory),sync));
        var chooser=window.GetLogicalDescendants().OfType<ComboBox>().Single(c=>c.ItemsSource is string[] modes && modes.Contains("Set Recorder"));
        var view=window.GetLogicalDescendants().OfType<SetRecorderView>().Single();
        Check(!view.IsVisible,"Library is the default workspace");
        chooser.SelectedIndex=1;
        Check(view.IsVisible && vm.RecordingDevices.Count==1,"Workspace selector opens the recorder and loads inputs");
        vm.StartRecordingCommand.ExecuteAsync(Path.Combine(directory,"set.flac")).GetAwaiter().GetResult();
        Check(vm.IsRecording && engine.IsRecording,"Selected input starts a recording");
        vm.NewMarkerArtist="Test Artist"; vm.NewMarkerTitle="Test Title"; vm.MarkTransitionCommand.Execute(null);
        Check(vm.TracklistMarkers.Count==1 && vm.TracklistPreview.Contains("Test Artist - Test Title"),"Manual transition appears in tracklist preview");
        chooser.SelectedIndex=0;
        Check(!view.IsVisible && engine.IsRecording,"Switching to library preserves active recording");
        chooser.SelectedIndex=1;
        Check(view.DataContext==vm && vm.TracklistMarkers.Count==1 && engine.Starts==1,"Returning to recorder retains session and markers");
        vm.TracklistMarkers[0].Title="Edited"; vm.RefreshTracklistPreviewCommand.Execute(null);
        Check(vm.TracklistPreview.Contains("Edited"),"Edited markers refresh export text");
        vm.StopRecordingCommand.ExecuteAsync(null).GetAwaiter().GetResult();
        Check(!vm.IsRecording && engine.Stops==1 && vm.RecordingElapsedDisplay=="01:05","Stop finalizes recording and displays duration");
        vm.RemoveMarkerCommand.Execute(vm.TracklistMarkers[0]);
        Check(vm.TracklistPreview=="","Removing a marker updates preview");
        window.Measure(new Size(1400,820)); window.Arrange(new Rect(0,0,1400,820));
        Check(view.Bounds.Width>0,"Recorder XAML, resources and layout load successfully");
        chooser.SelectedIndex=0;
        var clubRoot=playlists.EnsureClubRootAsync().GetAwaiter().GetResult();
        playlists.CreateAsync("Club set",PlaylistNodeType.Playlist,clubRoot).GetAwaiter().GetResult();
        var reload=(Task)typeof(WorkspaceWindow).GetMethod("ReloadBrowsers",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!.Invoke(window,null)!;
        reload.GetAwaiter().GetResult();
        window.Show();
        TreeViewItem? clubItem=null;
        for(var i=0;i<100&&clubItem==null;i++)
        {
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();Thread.Sleep(10);
            clubItem=window.GetVisualDescendants().OfType<TreeViewItem>().FirstOrDefault(t=>t.DataContext is BrowserEntry e && e.Target.Kind==LibraryTargetKind.ClubCollection);
        }
        Check(clubItem?.IsExpanded==true && clubItem.Items.Count==2,"Club Collection expands to show Unsorted and its own playlist");
        WorkspaceShortcutUiChecks.Run(window,player,tracks,roots,playlists,state,directory);
        vm.StartRecordingCommand.ExecuteAsync(Path.Combine(directory,"second.flac")).GetAwaiter().GetResult();
        window.Close();
        for(var i=0;i<100&&window.IsVisible;i++) {Avalonia.Threading.Dispatcher.UIThread.RunJobs();Thread.Sleep(10);}
        Check(!window.IsVisible && !engine.IsRecording && engine.Stops==2,"Closing the workspace finalizes an active recorder session");
    }
    private static void Check(bool condition,string text)
    { if(!condition) throw new Exception(text); Console.WriteLine("PASS: "+text); }
    private sealed class Recorder : IAudioRecorder
    {
        public bool IsRecording { get; private set; }
        public int Starts,Stops;
        public AudioLevel? CurrentLevel=>new(new double[] {-12,-10});
        public Task<IReadOnlyList<AudioCaptureDevice>> GetAvailableDevicesAsync(CancellationToken ct=default)=>Task.FromResult<IReadOnlyList<AudioCaptureDevice>>([new("test","Test input")]);
        public Task StartAsync(string deviceId,RecordingFormat format,string outputPath,CancellationToken ct=default)
        { IsRecording=true; Starts++; return Task.CompletedTask; }
        public Task<TimeSpan> StopAsync(CancellationToken ct=default)
        { IsRecording=false; Stops++; return Task.FromResult(TimeSpan.FromSeconds(65)); }
    }
    private sealed class Audio : IAudioPlayer
    {
        public PlaybackState State=>PlaybackState.Stopped;
        public TimeSpan Position=>TimeSpan.Zero;
        public TimeSpan Duration=>TimeSpan.Zero;
        public float Volume { get; set; }
        public event EventHandler? PlaybackEnded { add {} remove {} }
        public event EventHandler? PositionChanged { add {} remove {} }
        public void Load(string path) {} public void Play() {} public void Pause() {} public void Stop() {} public void Seek(TimeSpan position) {} public void Dispose() {}
    }
    private sealed class Stripes : ITrackWaveformSource
    {public Task<WaveformData?> TryLoadAsync(Track track,CancellationToken ct=default)=>Task.FromResult<WaveformData?>(new() {Columns=[new(.1f,.2f,.3f)]});}
}
