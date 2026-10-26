using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.Input;
using System.Reflection;
using Avalonia.VisualTree;
using Cratebase.App.Desktop.Views;
using Cratebase.Core.Abstractions;
using Cratebase.Core.Models;
using Cratebase.Data;
using Cratebase.Data.Repositories;

internal static class PlanningUiChecks
{
    public static void RunNativePlayback()
    {
        AppBuilder.Configure<Cratebase.App.Desktop.App>().UseHeadless(new()).SetupWithoutStarting();LibVLCSharp.Shared.Core.Initialize();
        var directory=Path.Combine(Path.GetTempPath(),"Cratebase-native-planning-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        var source=Path.Combine(directory,"quiet-test.wav");var info=new System.Diagnostics.ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Cratebase","tools","ffmpeg.exe")){UseShellExecute=false,CreateNoWindow=true};
        foreach(var arg in new[]{"-v","error","-f","lavfi","-i","sine=frequency=440:duration=8","-af","volume=0.00001",source})info.ArgumentList.Add(arg);
        using(var process=System.Diagnostics.Process.Start(info)!){process.WaitForExit();if(process.ExitCode!=0)throw new Exception("Native audio fixture failed");}
        var factory=new SqliteConnectionFactory(Path.Combine(directory,"test.db"));DatabaseInitializer.EnsureCreated(factory);var roots=new LibraryRootRepository(factory);var tracks=new TrackRepository(factory);var playlists=new PlaylistRepository(factory);
        var root=roots.AddOrGetAsync(directory,"Fixture").GetAwaiter().GetResult();var track=new Track {RelativePath="quiet-test.wav",LibraryRootId=root.Id,Title="Quiet test",DurationMs=8000};track.Id=tracks.UpsertAsync(track).GetAwaiter().GetResult();
        using var view=new SetPlanningView(new WorkspaceStateStore(factory),tracks,roots,playlists,null,new Wave(),new Cache(),new Wave(),(_,_)=>Task.FromResult<IReadOnlyList<Track>>([track]),()=>[track],()=>Task.CompletedTask,()=>{},Console.WriteLine,()=>"ffmpeg");
        var host=new Window {Content=view,Width=1372,Height=650};host.Show();Wait(view.InitializeAsync());Click("Auswahl anhängen");
        var button=view.GetLogicalDescendants().OfType<Button>().Single(b=>b.Name=="SetDeckAPlay");button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Until(()=>!view.Busy);
        var audio=(IAudioPlayer)typeof(SetPlanningView).GetField("_preview",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(view)!;
        Until(()=>audio.Position.TotalMilliseconds>=300);Check(audio.State==PlaybackState.Playing,"Real LibVLC deck playback advances through valid audio");
        view.GetLogicalDescendants().OfType<Cratebase.App.Desktop.Controls.WaveformView>().First().SeekCommand!.Execute(.5d);Until(()=>audio.Position.TotalMilliseconds>=3900);Check(audio.Position.TotalMilliseconds<7000,"Real LibVLC waveform seek jumps to the requested position");
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Until(()=>audio.State==PlaybackState.Paused);Check(audio.State==PlaybackState.Paused,"Real LibVLC deck pauses");
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Until(()=>audio.State==PlaybackState.Playing);Check(audio.State==PlaybackState.Playing,"Real LibVLC deck resumes");
        Click("Vorschau stoppen");Check(typeof(SetPlanningView).GetField("_preview",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(view)==null,"Real LibVLC deck stops and releases its audio");host.Close();
        void Click(string label){view.GetLogicalDescendants().OfType<Button>().Single(b=>b.Content as string==label).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Until(()=>!view.Busy);}
    }
    public static void Run()
    {
        AppBuilder.Configure<Cratebase.App.Desktop.App>().UseHeadless(new(){UseHeadlessDrawing=false}).UseSkia().SetupWithoutStarting();
        var directory=Path.Combine(Path.GetTempPath(),"Cratebase-planning-ui-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        var original=Environment.GetEnvironmentVariable("CRATEBASE_DATA_DIR");Environment.SetEnvironmentVariable("CRATEBASE_DATA_DIR",directory);
        try
        {
            var factory=new SqliteConnectionFactory(Path.Combine(directory,"test.db"));DatabaseInitializer.EnsureCreated(factory);
            var tracks=new TrackRepository(factory);var roots=new LibraryRootRepository(factory);var playlists=new PlaylistRepository(factory);var cues=new CuePointRepository(factory);var state=new WorkspaceStateStore(factory);
            var root=roots.AddOrGetAsync(directory,"Fixture").GetAwaiter().GetResult();
            var selection=new[]{new Track {Title="A",RelativePath="a.wav",LibraryRootId=root.Id,DurationMs=180000,Bpm=128,MusicalKey="8A",GenreRaw="Techno"},new Track {Title="B",RelativePath="b.wav",LibraryRootId=root.Id,DurationMs=180000,Bpm=130,MusicalKey="9A",GenreRaw="Techno"}};
            foreach(var track in selection)track.Id=tracks.UpsertAsync(track).GetAwaiter().GetResult();
            foreach(var track in selection)File.WriteAllBytes(Path.Combine(directory,track.RelativePath),[1,2,3,4]);
            cues.ReplaceForTrackAsync(selection[0].Id,"imported:traktor",[new(){TrackId=selection[0].Id,PositionMs=1000,Label="Original",Source="imported:traktor"}]).GetAwaiter().GetResult();
            var audioPlayers=new List<Audio>();
            using var view=new SetPlanningView(state,tracks,roots,playlists,cues,new Wave(),new Cache(),new Wave(),(_,_)=>Task.FromResult<IReadOnlyList<Track>>(selection),()=>selection,()=>Task.CompletedTask,()=>{},Console.WriteLine,()=>"ffmpeg",browserQuery:new LibraryQuery(factory),audioFactory:()=>{var audio=new Audio();audioPlayers.Add(audio);return audio;});
            var host=new Window {Content=view,Width=1372,Height=550};host.Show();Dispatcher.UIThread.RunJobs();
            Wait(view.InitializeAsync());
            var browser=view.GetLogicalDescendants().OfType<DataGrid>().Single(g=>g.Name=="SetPlanningBrowserTracks");
            Check(browser.ItemsSource!.Cast<Track>().Count()==2,"Embedded file browser loads the collection independently of the Library workspace");
            foreach(var track in browser.ItemsSource!.Cast<Track>())browser.SelectedItems.Add(track);
            var table=view.GetLogicalDescendants().OfType<DataGrid>().Single(g=>g.Name=="SetPlanTracks");
            Drop(selection,[],null);Check(table.ItemsSource!.Cast<object>().Count()==2,"Dropping browser tracks onto the set inserts the multi-selection");
            Click("Auswahl anhängen");Check(table.ItemsSource!.Cast<object>().Count()==2,"Append button does not duplicate tracks already added through drag and drop");
            ClickNamed("SetDeckAPlay");Check(audioPlayers.Last().State==PlaybackState.Playing,"Deck A can audition its actual track through the audio engine");
            ClickNamed("SetDeckAPlay");Check(audioPlayers.Last().State==PlaybackState.Paused,"Deck A pause reaches the audio engine");
            ClickNamed("SetDeckAPlay");Check(audioPlayers.Last().State==PlaybackState.Playing,"Deck A resumes without reloading its track");
            ClickNamed("SetDeckBPlay");Check(audioPlayers.Count==2&&audioPlayers[0].Disposed&&audioPlayers[1].State==PlaybackState.Playing,"Deck B auditions independently and stops the previous deck");
            var wave=view.GetLogicalDescendants().OfType<Cratebase.App.Desktop.Controls.WaveformView>().First();
            wave.SeekCommand!.Execute(.5d);Until(()=>!view.Busy);Check(Math.Abs(audioPlayers.Last().Position.TotalSeconds-90)<.01,"Waveform click seeks audio without changing transition markers");
            host.UpdateLayout();var start=wave.TranslatePoint(new Point(1,wave.Bounds.Height-3),host)!.Value;var end=wave.TranslatePoint(new Point(wave.Bounds.Width*.1,wave.Bounds.Height-3),host)!.Value;
            host.MouseDown(start,MouseButton.Left);host.MouseMove(end);host.MouseUp(end,MouseButton.Left);Until(()=>!view.Busy);
            var mousePlan=System.Text.Json.JsonSerializer.Deserialize<List<SetPlan>>(state.ReadAsync(SetPlan.StateKey).GetAwaiter().GetResult()!)!.Single();
            Check(Math.Abs(mousePlan.Entries[0].PlayMs-18000)<1000,"Dragging the transition-start handle in the waveform persists its position");
            DragMarker("bass",.8);DragMarker("out",.9);
            mousePlan=System.Text.Json.JsonSerializer.Deserialize<List<SetPlan>>(state.ReadAsync(SetPlan.StateKey).GetAwaiter().GetResult()!)!.Single();
            Check(Math.Abs(mousePlan.Entries[0].BassTradeMs-144000)<1000&&Math.Abs(mousePlan.Entries[0].FadeOutMs-162000)<1000,"Mouse dragging also persists bass-trade and fade-out markers");
            Click("Kurzen Basswechsel vorschlagen");Click("Marker übernehmen");
            var json=state.ReadAsync(SetPlan.StateKey).GetAwaiter().GetResult()!;var plans=System.Text.Json.JsonSerializer.Deserialize<List<SetPlan>>(json)!;
            Check(plans.Single().Entries[0].PlayMs>150000,"Short transition markers are saved independently");
            Check(cues.GetForTrackAsync(selection[0].Id).GetAwaiter().GetResult().Single().Label=="Original","Planning leaves imported Traktor cues unchanged");
            var current=(SetPlan)typeof(SetPlanningView).GetField("_plan",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(view)!;var moved=current.Entries[0];var cue=moved.PlayMs;
            Drop([], [moved],view);Check(current.Entries.Last()==moved&&moved.PlayMs==cue,"Internal set drag-and-drop changes order while preserving transition markers");
            Click("↑");
            Click("↓");Click("Als Playlist speichern");
            var node=playlists.GetAllNodesAsync().GetAwaiter().GetResult().Single(n=>n.NodeType==PlaylistNodeType.Playlist);
            var ordered=new LibraryQuery(factory).QueryAsync(new(new(LibraryTargetKind.Playlist,node.Name,node.Id))).GetAwaiter().GetResult();
            Check(ordered.Select(t=>t.Id).SequenceEqual(selection.AsEnumerable().Reverse().Select(t=>t.Id)),"Manual set order survives publishing as a normal playlist");
            Click("Quellen aktualisieren");var tree=view.GetLogicalDescendants().OfType<TreeView>().Single(t=>t.Name=="SetPlanningSources");
            tree.SelectedItem=tree.ItemsSource!.Cast<object>().Single(o=>(o.GetType().GetProperty("Target")!.GetValue(o) as LibraryTarget)?.PlaylistId==node.Id);Until(()=>!view.Busy);
            Check(browser.ItemsSource!.Cast<Track>().Select(t=>t.Id).SequenceEqual(ordered.Select(t=>t.Id)),"Embedded browser opens playlist contents in their saved order");
            var search=view.GetLogicalDescendants().OfType<TextBox>().Single(t=>t.Name=="SetPlanningSearch");search.Text="B";Until(()=>browser.ItemsSource!.Cast<Track>().Count()==1);
            Check(browser.ItemsSource!.Cast<Track>().Single().Title=="B","Embedded browser search filters its source independently");search.Text="";Until(()=>browser.ItemsSource!.Cast<Track>().Count()==2);
            Click("Regelbasiert vorschlagen");
            plans=System.Text.Json.JsonSerializer.Deserialize<List<SetPlan>>(state.ReadAsync(SetPlan.StateKey).GetAwaiter().GetResult()!)!;
            Check(plans.Count==2&&plans.All(p=>p.Entries.Count==2),"Generating another proposal preserves the existing saved set");
            Wait(new LibraryQuery(factory).SetFlagAsync(selection.Select(t=>t.Id).ToArray(),"trash",true));Click("Regelbasiert vorschlagen");
            plans=System.Text.Json.JsonSerializer.Deserialize<List<SetPlan>>(state.ReadAsync(SetPlan.StateKey).GetAwaiter().GetResult()!)!;
            Check(plans.Count==2,"An empty proposal preserves the existing plan and creates no empty replacement");
            var extra=new Track {Title="C",RelativePath="c.wav",LibraryRootId=root.Id,DurationMs=180000,Bpm=129,GenreRaw="Techno"};extra.Id=tracks.UpsertAsync(extra).GetAwaiter().GetResult();
            host.UpdateLayout();var firstRow=table.GetVisualDescendants().OfType<DataGridRow>().First(r=>r.DataContext?.GetType().GetProperty("Number")?.GetValue(r.DataContext) is 1);
            Drop([extra],[],null,firstRow.TranslatePoint(new Point(35,3),table));
            current=(SetPlan)typeof(SetPlanningView).GetField("_plan",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(view)!;
            Check(current.Entries[0].TrackId==extra.Id,"A drop above a set row inserts at that position instead of always appending");
            Check(new LibraryQuery(factory).QueryAsync(new(new(LibraryTargetKind.Playlist,node.Name,node.Id))).GetAwaiter().GetResult().Count==2,"Set edits leave the source playlist unchanged");
            view.Measure(new Size(1372,550));view.Arrange(new Rect(0,0,1372,550));
            Console.WriteLine("Planner table height: "+table.Bounds.Height);
            using(var bitmap=new Avalonia.Media.Imaging.RenderTargetBitmap(new PixelSize(1372,550),new Vector(96,96))){bitmap.Render(view);bitmap.Save(Path.Combine(directory,"planning.png"),new Avalonia.Media.Imaging.PngBitmapEncoderOptions());}
            Console.WriteLine("Planning screenshot: "+Path.Combine(directory,"planning.png"));
            Check(table.Bounds.Height>50,"Planner table retains space with populated cue and summary views");
            var flags=BindingFlags.Instance|BindingFlags.NonPublic;
            var updateCandidates=typeof(SetPlanningView).GetMethod("ShowCandidates",flags)!;
            updateCandidates.Invoke(view,[selection,"Metadaten-Vorauswahl"]);
            var pending=new TaskCompletionSource();
            var background=(Task)typeof(SetPlanningView).GetMethod("Run",flags)!.Invoke(view,[new Func<Task>(()=>pending.Task)])!;
            var candidateButton=view.GetLogicalDescendants().OfType<Button>().Single(b=>b.Name=="SetCandidateList");
            Check(view.Busy&&candidateButton.IsEnabled,"Candidate list remains accessible while the AI job is running");
            candidateButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Dispatcher.UIThread.RunJobs();
            var candidateWindow=(Window)typeof(SetPlanningView).GetField("_candidateWindow",flags)!.GetValue(view)!;
            var candidateTable=candidateWindow.GetLogicalDescendants().OfType<DataGrid>().Single();
            Check(candidateTable.ItemsSource!.Cast<Track>().Count()==2,"Candidate window displays the current preselection while the planner is busy");
            var candidatePlay=candidateWindow.GetLogicalDescendants().OfType<Button>().Single(b=>b.Name=="SetCandidatePlay");
            candidatePlay.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Until(()=>audioPlayers.Last().State==PlaybackState.Playing);
            Check(view.Busy&&audioPlayers.Last().State==PlaybackState.Playing,"Preselection tracks can be auditioned without waiting for AI completion");
            candidatePlay.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Until(()=>audioPlayers.Last().State==PlaybackState.Paused);
            Check(audioPlayers.Last().State==PlaybackState.Paused,"Preselection playback supports pause independently of the background job");
            updateCandidates.Invoke(view,[new[]{selection[1]},"Bisherige KI-Vorauswahl"]);
            Check(candidateTable.ItemsSource!.Cast<Track>().Single().Id==selection[1].Id,"Progressive preselection updates appear in the open list");
            candidateWindow.Close();Check(audioPlayers.Last().Disposed,"Closing the candidate window releases audition playback");
            pending.SetResult();Wait(background);
            Check(current.Entries.Count==3,"Auditioning provisional candidates leaves the saved set unchanged");
            host.Close();
            Console.WriteLine("Planning UI checks passed.");
            void Click(string label){var button=view.GetLogicalDescendants().OfType<Button>().Single(b=>b.Content as string==label);button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Until(()=>!view.Busy);}
            void ClickNamed(string name){var button=view.GetLogicalDescendants().OfType<Button>().Single(b=>b.Name==name);button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Until(()=>!view.Busy);}
            void DragMarker(string id,double progress)
            {
                var marker=wave.Markers!.Single(m=>m.Id==id);var from=wave.TranslatePoint(new Point(Math.Clamp(marker.Progress*wave.Bounds.Width,1,wave.Bounds.Width-1),wave.Bounds.Height-3),host)!.Value;
                var to=wave.TranslatePoint(new Point(progress*wave.Bounds.Width,wave.Bounds.Height-3),host)!.Value;
                host.MouseDown(from,MouseButton.Left);host.MouseMove(to);host.MouseUp(to,MouseButton.Left);Until(()=>!view.Busy);
            }
            void Drop(Track[] payloadTracks,SetPlanEntry[] entries,SetPlanningView? owner,Point? point=null)
            {
                var type=typeof(SetPlanningView).GetNestedType("PlanningDrag",BindingFlags.NonPublic)!;
                var data=Activator.CreateInstance(type,[payloadTracks,entries,owner]);
                var format=typeof(SetPlanningView).GetField("PlanningDragFormat",BindingFlags.NonPublic|BindingFlags.Static)!.GetValue(null);
                var create=typeof(DataTransferItem).GetMethods().Single(m=>m.Name=="Create"&&m.IsGenericMethodDefinition&&m.GetParameters().Length==2&&m.GetParameters()[1].ParameterType.IsGenericParameter).MakeGenericMethod(type);
                var transfer=new DataTransfer();transfer.Add((DataTransferItem)create.Invoke(null,[format,data])!);
                table.RaiseEvent(new DragEventArgs(DragDrop.DropEvent,transfer,table,point??new Point(5,5),KeyModifiers.None));Until(()=>!view.Busy);
            }
        }
        finally {Environment.SetEnvironmentVariable("CRATEBASE_DATA_DIR",original);}
    }
    private static void Wait(Task task){Until(()=>task.IsCompleted);task.GetAwaiter().GetResult();}
    private static void Until(Func<bool> done){var end=DateTime.UtcNow.AddSeconds(10);while(!done()){if(DateTime.UtcNow>end)throw new TimeoutException();Dispatcher.UIThread.RunJobs();Thread.Sleep(5);}Dispatcher.UIThread.RunJobs();}
    private static void Check(bool value,string text){if(!value)throw new Exception(text);Console.WriteLine("PASS: "+text);}
    private sealed class Wave:IWaveformGenerator,ITrackWaveformSource
    {
        public Task<WaveformData?> GenerateAsync(string path,CancellationToken ct=default)=>Data();
        public Task<WaveformData?> TryLoadAsync(Track track,CancellationToken ct=default)=>Data();
        private static Task<WaveformData?> Data()=>Task.FromResult<WaveformData?>(new(){Columns=[new(.3f,.2f,.1f)]});
    }
    private sealed class Cache:IWaveformCache
    {public Task<WaveformData?> TryLoadAsync(Track track,CancellationToken ct=default)=>Task.FromResult<WaveformData?>(null);public Task SaveAsync(Track track,WaveformData data,CancellationToken ct=default)=>Task.CompletedTask;}
    private sealed class Audio:IAudioPlayer
    {
        public PlaybackState State {get;private set;}public TimeSpan Position {get;private set;}public TimeSpan Duration=>TimeSpan.FromSeconds(180);public float Volume {get;set;}public bool Disposed {get;private set;}
        public event EventHandler? PositionChanged;public event EventHandler? PlaybackEnded {add{}remove{}}
        public void Load(string path){Position=TimeSpan.Zero;}public void Play(){State=PlaybackState.Playing;PositionChanged?.Invoke(this,EventArgs.Empty);}public void Pause()=>State=PlaybackState.Paused;
        public void Stop()=>State=PlaybackState.Stopped;public void Seek(TimeSpan position){Position=position;PositionChanged?.Invoke(this,EventArgs.Empty);}public void Dispose()=>Disposed=true;
    }
}
