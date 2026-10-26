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
        var window=new WorkspaceWindow(tracks,roots,playlists,new LibraryQuery(factory),actions,sync,state,new JsonFavoriteProfileStore(Path.Combine(directory,"favorites.json")),new JsonQuickAccessStore(Path.Combine(directory,"quick.json")),new LibraryScanner(tracks,roots,new TagLibTagReader()),new TagLibTagReader(),player,vm,new FfmpegAudioRecorder(),new ClubMediaExporter(roots,new LibraryQuery(factory),sync));
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
        chooser.SelectedIndex=2;
        var planner=window.GetLogicalDescendants().OfType<SetPlanningView>().Single();
        for(var i=0;i<100&&planner.Busy;i++){Avalonia.Threading.Dispatcher.UIThread.RunJobs();Thread.Sleep(10);}
        window.Measure(new Size(1400,820));window.Arrange(new Rect(0,0,1400,820));
        Check(planner.IsVisible&&!view.IsVisible,"Third workspace opens set planning without losing recorder state");
        planner.Measure(new Size(1372,550));planner.Arrange(new Rect(0,0,1372,550));
        var planTable=planner.GetLogicalDescendants().OfType<DataGrid>().Single(g=>g.Name=="SetPlanTracks");
        Check(planTable.Bounds.Height>50,"Set planning keeps its track table usable at desktop size");
        Check(planner.GetLogicalDescendants().OfType<Cratebase.App.Desktop.Controls.WaveformView>().Count()==2,"Planning has independent A/B waveforms");
        var music=Path.Combine(directory,"indexed-music");var genreFolder=Path.Combine(music,"Techno");Directory.CreateDirectory(genreFolder);
        var fixture=Path.Combine(genreFolder,"fixture.wav");
        using(var writer=new BinaryWriter(File.Create(fixture)))
        {writer.Write("RIFF"u8);writer.Write(16036);writer.Write("WAVEfmt "u8);writer.Write(16);writer.Write((short)1);writer.Write((short)1);writer.Write(8000);writer.Write(16000);writer.Write((short)2);writer.Write((short)16);writer.Write("data"u8);writer.Write(16000);writer.Write(new byte[16000]);}
        var index=typeof(WorkspaceWindow).GetMethod("IndexPlanningFolderAsync",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!;
        void IndexFolder()
        {var task=(Task)index.Invoke(window,[music,CancellationToken.None])!;var end=DateTime.UtcNow.AddSeconds(10);while(!task.IsCompleted){if(DateTime.UtcNow>end)throw new TimeoutException();Avalonia.Threading.Dispatcher.UIThread.RunJobs();Thread.Sleep(5);}task.GetAwaiter().GetResult();}
        IndexFolder();var indexed=new LibraryQuery(factory).QueryAsync(new(new(LibraryTargetKind.Directory,"Fixture",Path:music))).GetAwaiter().GetResult();
        Check(indexed.Count==1&&indexed[0].DurationMs>0&&indexed[0].RelativePath.Contains("Techno/"),"Planner indexing recursively persists real audio metadata and folder structure in SQLite");
        var originalBytes=File.ReadAllBytes(fixture);IndexFolder();
        Check(new LibraryQuery(factory).QueryAsync(new(new(LibraryTargetKind.Directory,"Fixture",Path:music))).GetAwaiter().GetResult().Single().Id==indexed[0].Id&&File.ReadAllBytes(fixture).SequenceEqual(originalBytes),"Repeating planner indexing reuses track identities and leaves original audio unchanged");
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
