using System.Collections.ObjectModel;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.Styling;
using Cratebase.App.Desktop.Controls;
using Cratebase.App.Desktop.Services;
using Cratebase.App.Desktop.ViewModels;
using Cratebase.Core.Abstractions;
using Cratebase.Core.Models;
using Cratebase.Import;
using Cratebase.Scanning;

namespace Cratebase.App.Desktop.Views;

public sealed class WorkspaceSettings
{
    public double SidebarTop { get; set; } = 340;
    public double TopHeight { get; set; } = 380;
    public bool DualPane { get; set; }
    public double ClubBudgetGb { get; set; } = 500;
    public List<MidiBinding> Midi { get; set; } = new();
    public List<TrackPaneOptions> Panes { get; set; }=[new(),new()];
    public double UiScalePercent { get; set; }=100;
    public bool TrashColumnInitialized { get; set; }
    public string? RecorderFfmpegPath { get; set; }
    public string ClubExportDirectory {get;set;}=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),"Music","ClubCollection");
    public Dictionary<string,string> ClubExportFolders {get;set;}=new();
    public string StemExportDirectory {get;set;}=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Cratebase","stems");
}

public sealed class BrowserEntry(LibraryTarget target)
{
    public LibraryTarget Target { get; } = target;
    public ObservableCollection<BrowserEntry> Children { get; } = new();
    public bool Loaded { get; set; }
    public bool Expanded { get; set; }
    public string Label => Target.Kind switch { LibraryTargetKind.Directory => "▸  ", LibraryTargetKind.PlaylistFolder => "▸  ",LibraryTargetKind.ClubCollection => "✓  ",_ => "≡  " } + Target.Name;
}

/// <summary>Desktop composition of independent browser, pane, transport and sync states.</summary>
public sealed partial class WorkspaceWindow : Window
{
    private readonly ITrackRepository _tracks;
    private readonly ILibraryRootRepository _roots;
    private readonly IPlaylistRepository _playlists;
    private readonly ILibraryQuery _query;
    private readonly TargetActions _actions;
    private readonly CollectionSync _sync;
    private readonly IWorkspaceStateStore _state;
    private readonly IFavoriteProfileStore _favoriteStore;
    private readonly IQuickAccessStore _quickStore;
    private readonly ILibraryScanner _scanner;
    private readonly ITagReader _tags;
    private readonly WorkspacePlayer _player;
    private readonly MainViewModel _recorder;
    private readonly Cratebase.Audio.FfmpegAudioRecorder _audioRecorder;
    private readonly ClubMediaExporter? _clubExporter;
    private readonly ICuePointRepository? _planningCues;
    private SetPlanningView? _planningView;
    private readonly WindowsMidiInput _midi = new();
    private WindowsMediaSession? _mediaSession;
    private WorkspaceSettings _settings = new();
    private FavoriteProfileSettings _favorites = new();
    private readonly ObservableCollection<BrowserEntry> _playlistEntries = new();
    private readonly ObservableCollection<BrowserEntry> _folderEntries = new();
    private readonly TrackPaneViewModel[] _panes;
    private readonly DataGrid[] _tables = new DataGrid[2];
    private readonly Dictionary<string,DataGridColumn>[] _columns=[new(),new()];
    private readonly ScaleTransform _uiScale=new(1,1);
    private readonly Border[] _paneBorders = new Border[2];
    private readonly Grid _sidebar = new() { RowDefinitions = new("340,5,*") };
    private readonly Grid _lists = new() { RowDefinitions = new("*,0,0") };
    private readonly WrapPanel _slots = new() { Orientation = Orientation.Horizontal };
    private readonly ComboBox _profile = new() { Width = 170 };
    private readonly TextBlock _status = new() { Text = "Bereit", TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock _budget = new();
    private readonly Button _cancel = new() { Content = "Auftrag abbrechen", IsVisible = false };
    private CancellationTokenSource? _job;
    private BrowserEntry? _selectedBrowser;
    private long? _revealPlaylist;
    private int _active;
    private int? _learnSlot;
    private bool _restoring;
    private bool _closing;
    private bool _syncActive;
    private bool _updatingClub;
    private CancellationTokenSource? _availabilityRefresh;
    private static readonly IBrush Accent = Brush.Parse("#33E78C");
    private static readonly IBrush Surface = Brush.Parse("#11151B");
    private static readonly IBrush Muted = Brush.Parse("#8993A3");
    private static readonly DataFormat<DragPayload> DragFormat = DataFormat.CreateInProcessFormat<DragPayload>("application/x-cratebase-workspace");
    private sealed record DragPayload(LibraryTarget? Target, Track[] Tracks);

    public WorkspaceWindow(ITrackRepository tracks,ILibraryRootRepository roots,IPlaylistRepository playlists,ILibraryQuery query,TargetActions actions,CollectionSync sync,IWorkspaceStateStore state,IFavoriteProfileStore favoriteStore,IQuickAccessStore quickStore,ILibraryScanner scanner,ITagReader tags,WorkspacePlayer player,MainViewModel recorder,Cratebase.Audio.FfmpegAudioRecorder audioRecorder,ClubMediaExporter? clubExporter=null,Cratebase.Server.LibraryServer? server=null,ICuePointRepository? planningCues=null)
    {
        _tracks=tracks; _roots=roots; _playlists=playlists; _query=query; _actions=actions; _sync=sync; _state=state; _favoriteStore=favoriteStore; _quickStore=quickStore; _scanner=scanner; _tags=tags; _player=player;
        _panes=[new(query),new(query)];
        _recorder=recorder; _audioRecorder=audioRecorder;
        _clubExporter=clubExporter;
        _planningCues=planningCues;
        _server=server;
        if(_server!=null)_server.Changed+=OnRemoteLibraryChanged;
        WireKeyboard();
        _recorder.PropertyChanged+=(_,e)=> { if(e.PropertyName==nameof(MainViewModel.StatusMessage)) Status(_recorder.StatusMessage); };
        foreach(var pane in _panes) pane.ReportError=Status;
        _player.ReportError=Status;
        _player.PropertyChanged+=(_,e)=>{if(e.PropertyName==nameof(WorkspacePlayer.PlaybackStatus)&&_player.PlaybackStatus==PlaybackState.Playing){_planningView?.StopPreview();_historyAnalysisWindow?.StopPlayback();}};
        _player.AvailabilityChanged+=TrackAvailabilityChanged;
        Title="Cratebase · DJ Library Workspace"; Width=1400; Height=820; MinWidth=1050; MinHeight=680;
        Content=Build();
        Opened+=async (_,_)=>
        {
            if(Screens.ScreenFromWindow(this) is { } screen) { Height=Math.Min(Height,screen.WorkingArea.Height/RenderScaling-36); Width=Math.Min(Width,screen.WorkingArea.Width/RenderScaling-24); }
            await Safe(InitializeAsync);
            await Safe(InitializeKeyboardAsync);
            await Safe(InitializeServerAsync);
            if(OperatingSystem.IsWindows() && TryGetPlatformHandle() is {HandleDescriptor:"HWND"} handle)
                await Safe(()=> { _mediaSession=new WindowsMediaSession(handle.Handle,_player,async field=> {if(!_keyboardSuspended&&!_closing&&!OwnedWindows.Any(w=>w.IsVisible))await TogglePlayerFlag(field);});_mediaSession.SetTagMode(_keyboard.ActiveProfile.MediaKeysTagMode); return Task.CompletedTask; });
        };
        Closing+=OnClosing;
        _midi.Pressed+=(channel,message,number)=>Dispatcher.UIThread.Post(async ()=>await Safe(async ()=>
        {
            if(_learnSlot is { } learning)
            {
                _settings.Midi.RemoveAll(b=>b.Slot==learning || (b.Channel==channel && b.Message==message && b.Number==number));
                _settings.Midi.Add(new(learning,channel,message,number)); _learnSlot=null;
                await SaveSettings(); Status($"MIDI für Slot {learning+1} gespeichert."); RebuildSlots(); return;
            }
            var binding=_settings.Midi.FirstOrDefault(b=>b.Channel==channel && b.Message==message && b.Number==number);
            if(binding==null) return;
            if(_player.CurrentTrack==null) { Status("Kein Track im Player geladen."); return; }
            var target=ActiveProfile?.Targets.GetValueOrDefault(binding.Slot);
            if(target==null) { Status("MIDI-Zielslot ist nicht belegt."); return; }
            await AddToTarget(target,[_player.CurrentTrack]);
        }));
    }

    private Control Build()
    {
        var shell=new Grid { RowDefinitions=new("Auto,*,Auto,Auto,100"), Margin=new Thickness(14) };
        var header=new Grid { ColumnDefinitions=new("Auto,*"), Margin=new Thickness(0,0,0,14) };
        var brand=new StackPanel { Orientation=Orientation.Horizontal, Spacing=12 };
        brand.Children.Add(new TextBlock { Text="CRATEBASE", FontSize=23,FontWeight=FontWeight.Bold,Foreground=Accent,VerticalAlignment=VerticalAlignment.Center });
        var workspace=new ComboBox { ItemsSource=new[] { "Library Workspace", "Set Recorder", "Set & Transition Planning" },SelectedIndex=0,MinWidth=190,VerticalAlignment=VerticalAlignment.Center };
        brand.Children.Add(workspace);
        var recordingIndicator=new TextBlock {Text="● REC",Foreground=Brushes.OrangeRed,VerticalAlignment=VerticalAlignment.Center};
        recordingIndicator.Bind(IsVisibleProperty,new Binding(nameof(MainViewModel.IsRecording)){Source=_recorder}); brand.Children.Add(recordingIndicator);
        header.Children.Add(brand);
        var tools=new WrapPanel {HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(10,0,0,0)};
        var traktorButton=Button("Traktor abgleichen",ImportAsync);
        traktorButton.ContextMenu=new ContextMenu { Items={ Menu("NML-Quelle wechseln …",()=>ImportAsync(true)) } };
        ToolTip.SetTip(traktorButton,"Gespeicherte Traktor-NML abgleichen. Rechtsklick: Quelle wechseln.");
        tools.Children.Add(traktorButton);
        tools.Children.Add(Button("NML exportieren",ExportAsync));
        var clubExport=Button("Club exportieren",ExportClubMediaAsync);
        clubExport.ContextMenu=new ContextMenu {Items={Menu("Nur Club-NML exportieren …",ExportClubAsync)}};tools.Children.Add(clubExport);
        tools.Children.Add(Button("Übernahme bestätigen",ConfirmAsync));
        tools.Children.Add(Button("MIDI",MidiDialog));
        tools.Children.Add(Button("Stems",StemDialogAsync));
        tools.Children.Add(Button("Handy-Server",ServerDialogAsync));
        tools.Children.Add(_keyboardButton);tools.Children.Add(_globalKeysToggle);
        tools.Children.Add(_mediaKeysMode);
        tools.Children.Add(Button("Ansicht",ViewOptionsAsync));
        var split=new CheckBox { Content="Zweite Liste",Margin=new Thickness(8,0) };
        split.IsCheckedChanged+=async (_,_)=> { if(_restoring) return; _settings.DualPane=split.IsChecked==true; ApplySplit(); await Safe(async ()=> { if(_settings.DualPane&&_panes[1].Tracks.Count==0) await _panes[1].ReloadAsync(); await SaveSettings(); }); };
        split.Name="DualPaneToggle"; tools.Children.Add(split);
        Grid.SetColumn(tools,1); header.Children.Add(tools); shell.Children.Add(header);

        var body=new Grid { ColumnDefinitions=new("250,6,*") }; Grid.SetRow(body,1);
        var playlistSection=new Grid { RowDefinitions=new("Auto,Auto,*") };
        playlistSection.Children.Add(Label("COLLECTION & PLAYLISTEN"));
        var playlistTools=Row(); Grid.SetRow(playlistTools,1);
        playlistTools.Children.Add(Button("+ Playlist",()=>CreateNode(PlaylistNodeType.Playlist)));
        playlistTools.Children.Add(Button("+ Gruppe",()=>CreateNode(PlaylistNodeType.Folder)));
        playlistTools.Children.Add(Button("+ Smart",()=>EditSmartPlaylist()));
        playlistSection.Children.Add(playlistTools);
        var playlistTree=MakeTree(_playlistEntries); Grid.SetRow(playlistTree,2); playlistSection.Children.Add(playlistTree); _sidebar.Children.Add(playlistSection);
        var separator=new GridSplitter { ResizeDirection=GridResizeDirection.Rows,HorizontalAlignment=HorizontalAlignment.Stretch,Background=Brush.Parse("#29313E") };
        Grid.SetRow(separator,1); _sidebar.Children.Add(separator);
        separator.PointerReleased+=(_,_)=> { _settings.SidebarTop=_sidebar.RowDefinitions[0].ActualHeight; _=Safe(SaveSettings); };
        var folders=new Grid { RowDefinitions=new("Auto,Auto,*") }; Grid.SetRow(folders,2);
        folders.Children.Add(Label("ORDNER & LAUFWERKE"));
        var folderTools=Row(); Grid.SetRow(folderTools,1);
        folderTools.Children.Add(Button("+ Ordner",AddFolderAsync));
        folderTools.Children.Add(Button("Scannen",ScanSelectedAsync)); folders.Children.Add(folderTools);
        folderTools.Children.Add(Button("Pfade",RelocateAsync));
        var folderTree=MakeTree(_folderEntries); Grid.SetRow(folderTree,2); folders.Children.Add(folderTree); _sidebar.Children.Add(folders);
        body.Children.Add(_sidebar);
        var vertical=new GridSplitter { ResizeDirection=GridResizeDirection.Columns,Background=Brush.Parse("#29313E") }; Grid.SetColumn(vertical,1); body.Children.Add(vertical);
        Grid.SetColumn(_lists,2); body.Children.Add(_lists);
        for(var i=0;i<2;i++) { var pane=BuildPane(i); Grid.SetRow(pane,i*2); _lists.Children.Add(pane); }
        var listSplitter=new GridSplitter { ResizeDirection=GridResizeDirection.Rows,HorizontalAlignment=HorizontalAlignment.Stretch,Background=Brush.Parse("#29313E") }; Grid.SetRow(listSplitter,1); _lists.Children.Add(listSplitter);
        listSplitter.PointerReleased+=(_,_)=> { _settings.TopHeight=_lists.RowDefinitions[0].ActualHeight; _=Safe(SaveSettings); };
        shell.Children.Add(body);

        var favorites=new Grid { RowDefinitions=new("Auto,Auto"),Margin=new Thickness(0,10,0,4) }; Grid.SetRow(favorites,2);
        var favoriteTools=Row(); favoriteTools.Children.Add(Label("FAVORITEN"));
        _profile.SelectionChanged+=(_,_)=> { if(_restoring) return; _favorites.ActiveProfileName=(_profile.SelectedItem as FavoriteProfile)?.Name; RebuildSlots(); _=Safe(SaveFavorites); };
        _profile.ItemTemplate=new FuncDataTemplate<FavoriteProfile>((p,_)=>new TextBlock { Text=p?.Name ?? "" });
        favoriteTools.Children.Add(_profile); favoriteTools.Children.Add(Button("+ Profil",NewProfileAsync));
        favoriteTools.Children.Add(Button("Profil löschen",DeleteProfileAsync));
        favoriteTools.Children.Add(new TextBlock { Text="Slots",VerticalAlignment=VerticalAlignment.Center,Foreground=Muted });
        var count=new NumericUpDown { Minimum=1,Maximum=64,Value=12,Width=110,Name="SlotCount" };
        count.ValueChanged+=(_,_)=> { if(_restoring) return; _favorites.SlotCount=(int)(count.Value??12); RebuildSlots(); _=Safe(SaveFavorites); }; favoriteTools.Children.Add(count);
        favoriteTools.Children.Add(new TextBlock { Text="Ziel auf Slot ziehen · Rechtsklick für MIDI",VerticalAlignment=VerticalAlignment.Center,Foreground=Muted,FontSize=11 });
        favorites.Children.Add(favoriteTools);
        var slotScroll=new ScrollViewer { Content=_slots,MaxHeight=130,VerticalScrollBarVisibility=Avalonia.Controls.Primitives.ScrollBarVisibility.Auto }; Grid.SetRow(slotScroll,1); favorites.Children.Add(slotScroll); shell.Children.Add(favorites);
        var recorderView=new SetRecorderView { DataContext=_recorder,IsVisible=false,SelectFfmpeg=SelectRecorderFfmpeg,ImportHistory=OpenHistoryAnalysisAsync,OpenArtwork=OpenSetArtworkAsync };
        Grid.SetRow(recorderView,1); Grid.SetRowSpan(recorderView,2); shell.Children.Add(recorderView);
        var dataDirectory=Environment.GetEnvironmentVariable("CRATEBASE_DATA_DIR")??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Cratebase");
        _planningView=new SetPlanningView(_state,_tracks,_roots,_playlists,_planningCues,new Cratebase.Analysis.FfmpegWaveformGenerator(),
            new Cratebase.Analysis.FileWaveformCache(Path.Combine(dataDirectory,"waveforms")),new Cratebase.Analysis.TraktorStripeWaveformSource(new Cratebase.Import.TraktorAnalysisLookup(_state,_roots)),
            PlanningCandidatesAsync,()=>_tables[_active].SelectedItems.OfType<Track>().ToArray(),ReloadBrowsers,()=>_player.PauseCommand.Execute(null),Status,
            ()=>_settings.RecorderFfmpegPath??(File.Exists(Path.Combine(dataDirectory,"tools","ffmpeg.exe"))?Path.Combine(dataDirectory,"tools","ffmpeg.exe"):"ffmpeg"),()=>_syncActive,_query,PreparePlanningSourceAsync,indexFolder:IndexPlanningFolderAsync) {IsVisible=false};
        Grid.SetRow(_planningView,1);Grid.SetRowSpan(_planningView,2);shell.Children.Add(_planningView);
        workspace.SelectionChanged+=async (_,_)=>await Safe(async ()=>
        {
            var recording=workspace.SelectedIndex==1;
            var planning=workspace.SelectedIndex==2;
            body.IsVisible=!recording&&!planning; favorites.IsVisible=!recording&&!planning;
            recorderView.IsVisible=recording;
            _planningView.IsVisible=planning;
            if(planning)await _planningView.InitializeAsync();else _planningView.StopPreview();
            Title=recording ? "Cratebase · Set Recorder" : planning?"Cratebase · Set & Transition Planning":"Cratebase · DJ Library Workspace";
            if(recording && !_recorder.IsRecording)
            {
                _audioRecorder.ExecutablePath=_settings.RecorderFfmpegPath ?? "ffmpeg";
                await _recorder.LoadRecordingDevicesCommand.ExecuteAsync(null);
            }
        });
        var statusRow=new Grid { ColumnDefinitions=new("*,Auto,Auto"),Margin=new Thickness(0,6) }; Grid.SetRow(statusRow,3);
        statusRow.Children.Add(_status); Grid.SetColumn(_budget,1); statusRow.Children.Add(_budget);
        _cancel.Click+=(_,_)=>_job?.Cancel(); Grid.SetColumn(_cancel,2); statusRow.Children.Add(_cancel); shell.Children.Add(statusRow);
        var player=new Grid { ColumnDefinitions=new("230,Auto,*,130"),Background=Surface,Margin=new Thickness(0,8,0,0),DataContext=_player }; Grid.SetRow(player,4);
        var info=new StackPanel { Margin=new Thickness(14),VerticalAlignment=VerticalAlignment.Center };
        info.Children.Add(BoundText("Title",14)); info.Children.Add(BoundText("Artist",11)); info.Children.Add(BoundText("Time",11)); player.Children.Add(info);
        var transport=Row(); transport.VerticalAlignment=VerticalAlignment.Center;
        foreach(var item in new[] { ("⏮","PreviousCommand"),("⏯","ToggleCommand"),("⏭","NextCommand") }) { var b=new Button { Content=item.Item1,Padding=new Thickness(12) }; b.Bind(Avalonia.Controls.Button.CommandProperty,new Binding(item.Item2)); transport.Children.Add(b); }
        AddPlayerFlags(transport);
        Grid.SetColumn(transport,1); player.Children.Add(transport);
        var waveArea=new Grid { RowDefinitions=new("*,Auto"),Margin=new Thickness(20,8) };
        var wave=new WaveformView { MinHeight=40,HorizontalAlignment=HorizontalAlignment.Stretch,VerticalAlignment=VerticalAlignment.Stretch };
        wave.Bind(WaveformView.WaveformProperty,new Binding("Waveform")); wave.Bind(WaveformView.ProgressProperty,new Binding("Progress")); wave.Bind(WaveformView.SeekCommandProperty,new Binding("SeekCommand")); wave.Bind(WaveformView.IsLoadingProperty,new Binding("IsWaveformLoading"));
        waveArea.Children.Add(wave);
        var waveStatus=BoundText("WaveformStatus",10); waveStatus.Foreground=Muted; Grid.SetRow(waveStatus,1); waveArea.Children.Add(waveStatus);
        waveArea.Bind(ToolTip.TipProperty,new Binding("WaveformStatus")); Grid.SetColumn(waveArea,2); player.Children.Add(waveArea);
        var volume=new Slider { Minimum=0,Maximum=100,Margin=new Thickness(10),VerticalAlignment=VerticalAlignment.Center }; volume.Bind(Slider.ValueProperty,new Binding("Volume") { Mode=BindingMode.TwoWay }); Grid.SetColumn(volume,3); player.Children.Add(volume); shell.Children.Add(player);
        return new LayoutTransformControl { Child=shell,LayoutTransform=_uiScale };
    }

    private Border BuildPane(int index)
    {
        var vm=_panes[index];
        var content=new Grid { RowDefinitions=new("Auto,Auto,*,Auto"),Margin=new Thickness(10),DataContext=vm };
        var top=new Grid { ColumnDefinitions=new("*,Auto") };
        var title=BoundText("Title",16); title.FontWeight=FontWeight.SemiBold; top.Children.Add(title);
        var marker=new TextBlock { Text=index==0 ? "OBEN" : "UNTEN",FontSize=10,Foreground=Muted }; Grid.SetColumn(marker,1); top.Children.Add(marker); content.Children.Add(top);
        var toolbar=new WrapPanel { Orientation=Orientation.Horizontal,Margin=new Thickness(0,8) }; Grid.SetRow(toolbar,1);
        var search=new TextBox { PlaceholderText="Metadaten suchen …",MinWidth=170,Width=210 }; search.Bind(TextBox.TextProperty,new Binding("Search") { Mode=BindingMode.TwoWay }); toolbar.Children.Add(search);
        var all=new MetadataField("all","Alle Metadaten","");
        var searchFields=new[] {all}.Concat(TrackMetadata.Fields).ToArray();
        var scope=new ComboBox { Width=145,ItemsSource=searchFields,SelectedIndex=0 };
        scope.SelectionChanged+=(_,_)=> { if(scope.SelectedItem is MetadataField field)vm.SearchField=field.Key; };toolbar.Children.Add(scope);
        ToolTip.SetTip(scope,"Suchfeld auswählen; alle Metadaten durchsucht auch ausgeblendete Spalten.");
        var defaultSort=new MetadataField("default","Reihenfolge","");var sortFields=new[]{defaultSort}.Concat(TrackMetadata.Fields).ToArray();
        var sort=new ComboBox { Width=140,ItemsSource=sortFields,SelectedIndex=0 };
        sort.SelectionChanged+=(_,_)=> { if(sort.SelectedItem is MetadataField field)vm.Sort=field.Key; }; toolbar.Children.Add(sort);
        var descending=new CheckBox { Content="↓",Margin=new Thickness(6,0) }; descending.Bind(CheckBox.IsCheckedProperty,new Binding("Descending") { Mode=BindingMode.TwoWay }); toolbar.Children.Add(descending);
        vm.PropertyChanged+=(_,e)=> { if(e.PropertyName==nameof(vm.Sort)) sort.SelectedItem=sortFields.FirstOrDefault(f=>f.Key==vm.Sort)??defaultSort; if(e.PropertyName==nameof(vm.SearchField)) scope.SelectedItem=searchFields.FirstOrDefault(f=>f.Key==vm.SearchField)??all; };
        toolbar.Children.Add(Button("Spalten",()=>ChooseColumns(index)));
        var filter=Button("Filter",()=>EditFilters(index));filter.Bind(Avalonia.Controls.Button.ContentProperty,new Binding("FilterLabel"));toolbar.Children.Add(filter);
        toolbar.Children.Add(Button("✓ Club",()=>MarkSelection(index,true))); toolbar.Children.Add(Button("✕ Club",()=>MarkSelection(index,false)));
        toolbar.Children.Add(Button("Tags",()=>EditTags(index))); content.Children.Add(toolbar);
        var table=new DataGrid { ItemsSource=vm.Tracks,AutoGenerateColumns=false,IsReadOnly=true,SelectionMode=DataGridSelectionMode.Extended,CanUserSortColumns=false,CanUserReorderColumns=true,CanUserResizeColumns=true,GridLinesVisibility=DataGridGridLinesVisibility.Horizontal,RowHeight=31,HeadersVisibility=DataGridHeadersVisibility.Column };
        _tables[index]=table; Grid.SetRow(table,2);
        foreach(var definition in TrackMetadata.Fields)
        {
            var header=Button(definition.Label,()=> { if(vm.Sort==definition.Key)vm.Descending=!vm.Descending;else vm.Sort=definition.Key;return Task.CompletedTask; });
            header.Padding=new Thickness(3);header.BorderThickness=new Thickness(0);header.Background=Brushes.Transparent;
            DataGridColumn column;
            if(definition.Kind==MetadataKind.Boolean)
                column=new DataGridTemplateColumn {CellTemplate=new FuncDataTemplate<Track>((track,_)=>
                {
                    var checkbox=new CheckBox {HorizontalAlignment=HorizontalAlignment.Center};
                    checkbox.Bind(CheckBox.IsCheckedProperty,new Binding(definition.Binding){Source=track,Mode=BindingMode.OneWay});
                    checkbox.IsCheckedChanged+=async (_,_)=> { if(track==null||_updatingClub)return;await Safe(()=>SetFlag([track.Id],definition.Key,checkbox.IsChecked==true)); };return checkbox;
                })};
            else column=new DataGridTextColumn {Binding=new Binding(definition.Binding){StringFormat=definition.Kind==MetadataKind.Date?"{0:dd.MM.yyyy HH:mm}":definition.Key=="filesize"?"{0:0.00}":null}};
            column.Header=header;column.Width=new DataGridLength(definition.Width);column.IsVisible=definition.DefaultVisible;
            table.Columns.Add(column);_columns[index][definition.Key]=column;
        }
        table.ColumnReordered+=(_,_)=> { if(_restoring)return;vm.Options.VisibleColumns=_columns[index].Where(p=>p.Value.IsVisible).OrderBy(p=>p.Value.DisplayIndex).Select(p=>p.Key).ToList();_=Safe(SaveSettings); };
        table.DoubleTapped+=async (_,_)=> { if(table.SelectedItem is Track track) await _player.PlayAsync(track,vm.Tracks.ToArray()); };
        table.SelectionChanged+=(_,_)=>Activate(index);
        PointerPressedEventArgs? pressed=null; Point start=default;
        table.AddHandler(PointerPressedEvent,(_,e)=> { Activate(index); if(e.GetCurrentPoint(table).Properties.IsLeftButtonPressed) { pressed=e; start=e.GetPosition(table); } },RoutingStrategies.Tunnel);
        table.PointerMoved+=async (_,e)=>
        {
            if(pressed==null || !e.GetCurrentPoint(table).Properties.IsLeftButtonPressed) return;
            var point=e.GetPosition(table); if(Math.Abs(point.X-start.X)+Math.Abs(point.Y-start.Y)<8) return;
            var gesture=pressed; pressed=null; var selection=table.SelectedItems.OfType<Track>().ToArray(); if(selection.Length==0) return;
            var transfer=new DataTransfer(); transfer.Add(DataTransferItem.Create(DragFormat,new DragPayload(null,selection)));
            await DragDrop.DoDragDropAsync(gesture,transfer,DragDropEffects.Copy);
        };
        table.PointerReleased+=(_,_)=>pressed=null;
        WireDrop(table,()=>vm.Source);
        var menu=new ContextMenu();
        foreach(var flag in new[]{("Extended","extended"),("Intro","intro"),("Trash","trash")})
        {
            menu.Items.Add(Menu(flag.Item1+" markieren",()=>SetFlag(table.SelectedItems.OfType<Track>().Select(t=>t.Id).ToArray(),flag.Item2,true)));
            menu.Items.Add(Menu(flag.Item1+" entfernen",()=>SetFlag(table.SelectedItems.OfType<Track>().Select(t=>t.Id).ToArray(),flag.Item2,false)));
        }
        menu.Items.Add(Menu("Aus Playlist entfernen",async ()=>
        {
            if(vm.Source.Kind!=LibraryTargetKind.Playlist) { Status("Nur in einer Playlist verfügbar."); return; }
            foreach(var track in table.SelectedItems.OfType<Track>().ToArray()) await _playlists.RemoveTrackAsync(vm.Source.PlaylistId!.Value,track.Id);
            await vm.ReloadAsync();
        }));
        table.ContextMenu=menu; content.Children.Add(table);
        var footer=new Grid { ColumnDefinitions=new("*,Auto") }; Grid.SetRow(footer,3); footer.Children.Add(BoundText("Summary",11));
        var more=Button("Weitere laden",()=>vm.ReloadAsync(false)); Grid.SetColumn(more,1); footer.Children.Add(more); content.Children.Add(footer);
        var border=new Border { Child=content,Background=Surface,BorderBrush=index==0?Accent:Brush.Parse("#29313E"),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(6) };
        border.AddHandler(PointerPressedEvent,(_,_)=>Activate(index),RoutingStrategies.Tunnel); _paneBorders[index]=border;
        return border;
    }

    private TreeView MakeTree(ObservableCollection<BrowserEntry> entries)
    {
        var tree=new TreeView { ItemsSource=entries,Margin=new Thickness(0,6,6,0) };
        tree.Styles.Add(new Style(s=>s.OfType<TreeViewItem>()) { Setters={new Setter(TreeViewItem.IsExpandedProperty,new Binding(nameof(BrowserEntry.Expanded)) { Mode=BindingMode.TwoWay })} });
        tree.ItemTemplate=new FuncTreeDataTemplate<BrowserEntry>((entry,_)=>
        {
            if(entry==null) return new TextBlock();
            var visual=new Border { Padding=new Thickness(4,6),Child=new TextBlock { Text=entry.Label,TextTrimming=TextTrimming.CharacterEllipsis },Background=Brushes.Transparent };
            var context=new ContextMenu();
            context.Items.Add(Menu("Oben öffnen",()=>Open(entry.Target,0)));
            if(entry.Target.Kind==LibraryTargetKind.SmartPlaylist)
            {
                context.Items.Add(Menu("Regeln bearbeiten …",()=>EditSmartPlaylist(entry.Target.Path)));
                context.Items.Add(Menu("Als normale Playlist speichern …",()=>MaterializeSmartPlaylist(entry.Target)));
                context.Items.Add(Menu("Smarte Playlist löschen …",()=>DeleteSmartPlaylist(entry.Target)));
            }
            if(entry.Target.Kind==LibraryTargetKind.TraktorCollection)
            {
                context.Items.Add(Menu("Jetzt abgleichen",ImportAsync));
                context.Items.Add(Menu("NML-Quelle wechseln …",()=>ImportAsync(true)));
            }
            if(entry.Target.Kind==LibraryTargetKind.ClubCollection)
            {
                context.Items.Add(Menu("Club Collection mit Audiodateien exportieren …",ExportClubMediaAsync));
                context.Items.Add(Menu("Club Collection als NML exportieren …",ExportClubAsync));
                context.Items.Add(Menu("+ Club-Playlist",()=>CreateNode(PlaylistNodeType.Playlist,entry.Target)));
                context.Items.Add(Menu("+ Club-Gruppe",()=>CreateNode(PlaylistNodeType.Folder,entry.Target)));
            }
            context.Items.Add(Menu("Unten öffnen",async ()=> { _settings.DualPane=true; ApplySplit(); await Open(entry.Target,1); await SaveSettings(); }));
            var assign=new MenuItem { Header="Favoriten-Slot zuweisen" };
            for(var s=0;s<_favorites.SlotCount;s++) { var slot=s; assign.Items.Add(Menu($"Slot {s+1}",()=>AssignSlot(slot,entry.Target))); }
            context.Items.Add(assign);
            if(entry.Target.Kind is LibraryTargetKind.Playlist or LibraryTargetKind.PlaylistFolder)
            {
                if(entry.Target.Kind==LibraryTargetKind.PlaylistFolder)
                {
                    context.Items.Add(Menu("+ Playlist",()=>CreateNode(PlaylistNodeType.Playlist,entry.Target)));
                    context.Items.Add(Menu("+ Gruppe",()=>CreateNode(PlaylistNodeType.Folder,entry.Target)));
                }
                context.Items.Add(Menu("In Gruppe verschieben …",()=>MovePlaylistDialog(entry.Target)));
                context.Items.Add(Menu("Umbenennen",async ()=> { var name=await Prompt("Umbenennen",entry.Target.Name); if(string.IsNullOrWhiteSpace(name)) return; await _playlists.RenameAsync(entry.Target.PlaylistId!.Value,name.Trim()); await ReloadBrowsers(); }));
                context.Items.Add(Menu("Löschen …",async ()=> { if(await Choose("Playlist/Gruppe löschen?","Nur Playlist-Zuordnungen werden entfernt. Audiodateien bleiben erhalten.",["Löschen","Abbrechen"])!=0) return; await _playlists.DeleteAsync(entry.Target.PlaylistId!.Value); await ReloadBrowsers(); await RefreshLists(); }));
            }
            visual.ContextMenu=context;
            PointerPressedEventArgs? press=null; Point start=default;
            visual.PointerPressed+=(_,e)=> { _selectedBrowser=entry; if(e.GetCurrentPoint(visual).Properties.IsLeftButtonPressed) { press=e; start=e.GetPosition(visual); } };
            visual.PointerMoved+=async (_,e)=>
            {
                if(press==null || !e.GetCurrentPoint(visual).Properties.IsLeftButtonPressed) return;
                var p=e.GetPosition(visual); if(Math.Abs(p.X-start.X)+Math.Abs(p.Y-start.Y)<8) return;
                var gesture=press; press=null;
                var transfer=new DataTransfer(); transfer.Add(DataTransferItem.Create(DragFormat,new DragPayload(entry.Target,[])));
                await DragDrop.DoDragDropAsync(gesture,transfer,DragDropEffects.Copy|DragDropEffects.Move);
            };
            visual.PointerReleased+=(_,_)=>press=null;
            WireDrop(visual,()=>entry.Target);
            return visual;
        },entry=>entry.Children);
        tree.SelectionChanged+=async (_,_)=> { if(tree.SelectedItem is BrowserEntry entry) { _selectedBrowser=entry; await Safe(()=>Open(entry.Target,_active)); } };
        tree.AddHandler(TreeViewItem.ExpandedEvent,async (sender,e)=>
        {
            if(e.Source is not TreeViewItem { DataContext: BrowserEntry entry } || entry.Target.Kind!=LibraryTargetKind.Directory || entry.Loaded) return;
            entry.Loaded=true;
            await Safe(async ()=>
            {
                var children=await Task.Run(()=>Directory.GetDirectories(entry.Target.Path!).Order(StringComparer.OrdinalIgnoreCase).ToArray());
                entry.Children.Clear(); foreach(var child in children) entry.Children.Add(Folder(child));
            });
        });
        if(ReferenceEquals(entries,_playlistEntries)) WireDrop(tree,()=>new(LibraryTargetKind.Collection,"Hauptebene"));
        return tree;
    }

    private static BrowserEntry Folder(string path)
    {
        var entry=new BrowserEntry(new(LibraryTargetKind.Directory,Path.GetFileName(path.TrimEnd('\\','/')) is { Length:>0 } name ? name : path,Path:path));
        entry.Children.Add(new(new(LibraryTargetKind.Directory,"Laden …",Path:path))); return entry;
    }
    private void WireDrop(Control control,Func<LibraryTarget?> target,int? slot=null)
    {
        DragDrop.SetAllowDrop(control,true);
        control.AddHandler(DragDrop.DragOverEvent,(_,e)=>
        {
            var payload=e.DataTransfer.TryGetValue(DragFormat);
            var destination=target();
            var moving=!slot.HasValue&&payload?.Target?.Kind is LibraryTargetKind.Playlist or LibraryTargetKind.PlaylistFolder && destination?.Kind is LibraryTargetKind.PlaylistFolder or LibraryTargetKind.Collection or LibraryTargetKind.TraktorCollection or LibraryTargetKind.ClubCollection && payload.Target.PlaylistId!=destination.PlaylistId;
            e.DragEffects=moving?DragDropEffects.Move:payload!=null && ((slot.HasValue && payload.Target?.Kind is LibraryTargetKind.Playlist or LibraryTargetKind.Directory) || (payload.Tracks.Length>0 && destination?.Kind is LibraryTargetKind.Playlist or LibraryTargetKind.Directory)) ? DragDropEffects.Copy : DragDropEffects.None; e.Handled=true;
        });
        control.AddHandler(DragDrop.DropEvent,async (_,e)=>
        {
            e.Handled=true; var payload=e.DataTransfer.TryGetValue(DragFormat); if(payload==null) return;
            await Safe(async ()=>
            {
                if(slot.HasValue && payload.Target!=null) await AssignSlot(slot.Value,payload.Target);
                else if(payload.Target?.Kind is LibraryTargetKind.Playlist or LibraryTargetKind.PlaylistFolder && target() is { Kind:LibraryTargetKind.PlaylistFolder or LibraryTargetKind.Collection or LibraryTargetKind.TraktorCollection or LibraryTargetKind.ClubCollection } group)
                    await MovePlaylist(payload.Target,group.Kind is LibraryTargetKind.PlaylistFolder or LibraryTargetKind.ClubCollection?group.PlaylistId:null);
                else if(target() is { } destination && payload.Tracks.Length>0) await AddToTarget(destination,payload.Tracks);
            });
        });
    }

    private async Task InitializeAsync()
    {
        _settings=JsonSerializer.Deserialize<WorkspaceSettings>(await _state.ReadAsync("workspace-layout")??"{}")??new();
        if(!_settings.TrashColumnInitialized){foreach(var pane in _settings.Panes)if(!pane.VisibleColumns.Contains("trash"))pane.VisibleColumns.Insert(Math.Max(0,pane.VisibleColumns.IndexOf("intro")+1),"trash");_settings.TrashColumnInitialized=true;}
        _settings.UiScalePercent=Math.Clamp(_settings.UiScalePercent,70,125);_uiScale.ScaleX=_uiScale.ScaleY=_settings.UiScalePercent/100;
        while(_settings.Panes.Count<2)_settings.Panes.Add(new());
        for(var i=0;i<2;i++) { _panes[i].Configure(_settings.Panes[i]);_panes[i].ConfigurationChanged=()=>_=Safe(SaveSettings);ApplyColumns(i); }
        _favorites=await _favoriteStore.LoadAsync();
        if(_favorites.Profiles.Count==0) _favorites.Profiles.Add(new() { Name="Standard" });
        foreach(var profile in _favorites.Profiles) foreach(var pair in profile.Slots) profile.Targets.TryAdd(pair.Key,new(LibraryTargetKind.Playlist,"Playlist",pair.Value));
        _restoring=true;
        _profile.ItemsSource=_favorites.Profiles; _profile.SelectedItem=_favorites.Profiles.FirstOrDefault(p=>p.Name==_favorites.ActiveProfileName)??_favorites.Profiles[0];
        FindControl<NumericUpDown>("SlotCount").Value=_favorites.SlotCount;
        FindControl<CheckBox>("DualPaneToggle").IsChecked=_settings.DualPane;
        _sidebar.RowDefinitions[0].Height=new GridLength(Math.Clamp(_settings.SidebarTop,120,600));
        _restoring=false; ApplySplit(); await ReloadBrowsers(); await _panes[0].ReloadAsync(); await RefreshClub(); RebuildSlots();
        await Task.Run(()=>_sync.RefreshTechnicalMetadataAsync());
        await RefreshLists();
    }
    private T FindControl<T>(string name) where T:Control => Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(this).OfType<T>().First(c=>c.Name==name);
    private FavoriteProfile? ActiveProfile => _profile.SelectedItem as FavoriteProfile;
    private async Task ReloadBrowsers()
    {
        await _sync.EnsureCollectionMembershipAsync();
        IEnumerable<BrowserEntry> Walk(IEnumerable<BrowserEntry> entries) => entries.SelectMany(e=>new[]{e}.Concat(Walk(e.Children)));
        var expanded=Walk(_playlistEntries).Where(e=>e.Target.PlaylistId!=null&&e.Target.Kind!=LibraryTargetKind.ClubUnsorted).ToDictionary(e=>e.Target.PlaylistId!.Value,e=>e.Expanded);
        _playlistEntries.Clear();
        _playlistEntries.Add(new(new(LibraryTargetKind.Collection,"Gesamte Collection")));
        _playlistEntries.Add(new(new(LibraryTargetKind.UnreachableFiles,"Datei nicht erreichbar")));
        foreach(var smart in (await ReadSmartPlaylists()).OrderBy(p=>p.Name))_playlistEntries.Add(new(smart.Target));
        var traktorTotals=await _query.TotalsAsync(new(new(LibraryTargetKind.TraktorCollection,"Traktor Collection")));
        _playlistEntries.Add(new(new(LibraryTargetKind.TraktorCollection,$"Traktor Collection ({traktorTotals.Count:N0})")));
        var clubId=await _playlists.EnsureClubRootAsync();
        var club=new BrowserEntry(new(LibraryTargetKind.ClubCollection,"Club Collection",clubId)) { Expanded=expanded.GetValueOrDefault(clubId,true) };
        club.Children.Add(new(new(LibraryTargetKind.ClubUnsorted,"Nicht einsortiert",clubId)));
        _playlistEntries.Add(club);
        var nodes=await _playlists.GetAllNodesAsync();
        var map=nodes.ToDictionary(n=>n.Id,n=>new BrowserEntry(new(n.NodeType==PlaylistNodeType.Playlist ? LibraryTargetKind.Playlist : LibraryTargetKind.PlaylistFolder,n.Name,n.Id)));
        foreach(var pair in map) pair.Value.Expanded=expanded.GetValueOrDefault(pair.Key);
        for(var reveal=_revealPlaylist;reveal.HasValue;reveal=nodes.FirstOrDefault(n=>n.Id==reveal)?.ParentId)
        { if(reveal==clubId)club.Expanded=true;else if(map.TryGetValue(reveal.Value,out var entry))entry.Expanded=true; }
        _revealPlaylist=null;
        foreach(var node in nodes)
        {
            if(node.Id==clubId) continue;
            if(node.ParentId==clubId) club.Children.Add(map[node.Id]);
            else if(node.ParentId is { } parent && map.TryGetValue(parent,out var group)) group.Children.Add(map[node.Id]);
            else _playlistEntries.Add(map[node.Id]);
        }
        _folderEntries.Clear();
        var paths=(await _quickStore.LoadAsync()).Select(f=>f.Path).Concat((await _roots.GetAllAsync()).Select(r=>r.Path)).Concat(DriveInfo.GetDrives().Where(d=>d.IsReady).Select(d=>d.RootDirectory.FullName)).Distinct(StringComparer.OrdinalIgnoreCase);
        foreach(var path in paths) _folderEntries.Add(Folder(path));
        foreach(var profile in _favorites.Profiles)
            foreach(var slot in profile.Targets.Keys.ToArray())
                if(profile.Targets[slot] is { Kind:LibraryTargetKind.Playlist,PlaylistId:{ } id } target && map.TryGetValue(id,out var node)) profile.Targets[slot]=target with { Name=node.Target.Name };
        RebuildSlots();
    }
    private void TrackAvailabilityChanged(Track changed)
    {
        if(!Dispatcher.UIThread.CheckAccess()) {Dispatcher.UIThread.Post(()=>TrackAvailabilityChanged(changed));return;}
        if(_closing)return;
        foreach(var track in _panes.SelectMany(p=>p.Tracks).Where(t=>t.Id==changed.Id))track.MissingSinceUtc=changed.MissingSinceUtc;
        if(_player.CurrentTrack is {} current&&current.Id==changed.Id)current.MissingSinceUtc=changed.MissingSinceUtc;
        _availabilityRefresh?.Cancel();
        var request=_availabilityRefresh=new();
        _=RefreshAvailabilityViewsAsync(request);
    }
    private async Task RefreshAvailabilityViewsAsync(CancellationTokenSource request)
    {
        try
        {
            await Task.Delay(200,request.Token);
            if(_closing)return;
            foreach(var pane in _panes.Where(p=>p.Source.Kind is LibraryTargetKind.UnreachableFiles or LibraryTargetKind.SmartPlaylist||p.Sort is "missing" or "availability"||p.Options.Filters.Any(f=>f.Field is "missing" or "availability")||!string.IsNullOrWhiteSpace(p.Search)))
            {request.Token.ThrowIfCancellationRequested();await pane.ReloadAsync();}
        }
        catch(OperationCanceledException) { }
        catch(Exception ex) {Status(ex.Message);}
        finally {if(ReferenceEquals(_availabilityRefresh,request))_availabilityRefresh=null;request.Dispose();}
    }
    private void Activate(int index) { _active=index; for(var i=0;i<2;i++) _paneBorders[i].BorderBrush=i==index ? Accent : Brush.Parse("#29313E"); }
    private void ApplySplit()
    {
        _restoring=true; FindControl<CheckBox>("DualPaneToggle").IsChecked=_settings.DualPane; _restoring=false;
        _paneBorders[1].IsVisible=_settings.DualPane;
        _lists.RowDefinitions[0].Height=_settings.DualPane ? new GridLength(Math.Clamp(_settings.TopHeight,150,Math.Max(150,(_lists.Bounds.Height>0?_lists.Bounds.Height:500)-155))) : new GridLength(1,GridUnitType.Star);
        _lists.RowDefinitions[1].Height=new(_settings.DualPane?5:0); _lists.RowDefinitions[2].Height=_settings.DualPane?new(1,GridUnitType.Star):new(0);
        if(!_settings.DualPane) Activate(0);
    }
    private async Task Open(LibraryTarget target,int index)
    {
        Activate(index);
        if(target.Kind==LibraryTargetKind.Directory)
        {
            // Read tags only for this folder; avoid a full-drive scan just to browse.
            Status("Ordner wird eingelesen …");
            await Task.Run(async ()=>
            {
                foreach(var file in Directory.EnumerateFiles(target.Path!).Where(_tags.CanRead)) await _actions.RegisterAsync(file,CancellationToken.None);
            });
        }
        await _panes[index].OpenAsync(target); Status(target.Name);
    }
    private async Task RefreshLists() { await _panes[0].ReloadAsync(); if(_settings.DualPane) await _panes[1].ReloadAsync(); }
    private async Task MarkSelection(int pane,bool value)
    {
        var selection=_tables[pane].SelectedItems.OfType<Track>().ToArray(); if(selection.Length==0) return;
        await _query.SetClubReadyAsync(selection.Select(t=>t.Id).ToArray(),value); await RefreshLists(); await RefreshClub();
    }
    private void MirrorClub(IReadOnlyCollection<long> ids,bool value)
    {
        _updatingClub=true;
        try { foreach(var pane in _panes) foreach(var track in pane.Tracks.Where(t=>ids.Contains(t.Id))) track.ClubReady=value; if(_player.CurrentTrack is { } current && ids.Contains(current.Id)) current.ClubReady=value; }
        finally { _updatingClub=false; }
    }
    private async Task RefreshClub()
    {
        if(_player.CurrentTrack is { } current && await _tracks.GetByIdAsync(current.Id) is { } stored)
            MirrorClub([current.Id],stored.ClubReady);
        var totals=await _query.TotalsAsync(new(new(LibraryTargetKind.ClubCollection,"Club Collection")));
        var gb=totals.Bytes/1_000_000_000.0;
        _budget.Text=$"Club: {gb:N2} / {_settings.ClubBudgetGb:N0} GB  "; _budget.Foreground=gb>_settings.ClubBudgetGb?Brushes.Orange:Accent;
        _budget.Cursor=new Cursor(StandardCursorType.Hand);
        _budget.PointerPressed-=BudgetClicked; _budget.PointerPressed+=BudgetClicked;
        // Mirror changed club values without replacing selections or scroll positions.
        var clubNodes=(await _playlists.GetClubNodeIdsAsync()).ToHashSet();
        foreach(var pane in _panes)
        {
            if(pane.Source.Kind is LibraryTargetKind.ClubCollection or LibraryTargetKind.ClubUnsorted || pane.Source.PlaylistId is { } id && clubNodes.Contains(id)) await pane.ReloadAsync();
        }
    }
    private async void BudgetClicked(object? sender,PointerPressedEventArgs e) => await Safe(async ()=>
    {
        var text=await Prompt("Club-Richtwert in GB",_settings.ClubBudgetGb.ToString());
        if(double.TryParse(text,out var value) && double.IsFinite(value) && value>0) { _settings.ClubBudgetGb=value; await SaveSettings(); await RefreshClub(); }
    });
    private async Task AssignSlot(int slot,LibraryTarget target)
    {
        if(ActiveProfile==null || target.Kind is not (LibraryTargetKind.Playlist or LibraryTargetKind.Directory)) { Status("Slots unterstützen Playlisten und Festplattenordner."); return; }
        ActiveProfile.Targets[slot]=target;
        if(target.Kind==LibraryTargetKind.Playlist) ActiveProfile.Slots[slot]=target.PlaylistId!.Value; else ActiveProfile.Slots.Remove(slot);
        await SaveFavorites(); RebuildSlots();
    }
    private void RebuildSlots()
    {
        _slots.Children.Clear();
        for(var s=0;s<_favorites.SlotCount;s++)
        {
            var slot=s; var target=ActiveProfile?.Targets.GetValueOrDefault(s);
            var learned=_settings.Midi.Any(b=>b.Slot==slot);
            var button=new Button { Content=$"{s+1:00}  {target?.Name??"Ziel zuweisen"}{(learned?"  ♪":"")}",MinWidth=150,Margin=new Thickness(0,4,6,0),Padding=new Thickness(10,9),BorderBrush=target==null?Muted:Accent,BorderThickness=new Thickness(1) };
            button.Click+=async (_,_)=> { if(target!=null) await Safe(()=>Open(target,_active)); };
            WireDrop(button,()=>ActiveProfile?.Targets.GetValueOrDefault(slot),slot);
            var menu=new ContextMenu();
            menu.Items.Add(Menu("Gewähltes Browser-Ziel zuweisen",async ()=> { if(_selectedBrowser!=null) await AssignSlot(slot,_selectedBrowser.Target); }));
            menu.Items.Add(Menu("MIDI lernen",()=> { _learnSlot=slot; Status($"MIDI-Learn: Taste für Slot {slot+1} drücken. MIDI-Gerät vorher im MIDI-Dialog verbinden."); return Task.CompletedTask; }));
            menu.Items.Add(Menu("MIDI-Zuweisung entfernen",async ()=> { _settings.Midi.RemoveAll(b=>b.Slot==slot); await SaveSettings(); RebuildSlots(); }));
            menu.Items.Add(Menu("Slot leeren",async ()=> { ActiveProfile?.Targets.Remove(slot); ActiveProfile?.Slots.Remove(slot); await SaveFavorites(); RebuildSlots(); }));
            button.ContextMenu=menu; _slots.Children.Add(button);
        }
    }
    private async Task AddToTarget(LibraryTarget target,Track[] selection)
    {
        if(_syncActive) { Status("Während des Traktor-Abgleichs sind Zielaktionen gesperrt."); return; }
        if(_job!=null) { Status("Ein Auftrag läuft bereits."); return; }
        var collision=CopyCollision.Skip;
        if(target.Kind==LibraryTargetKind.Directory)
        {
            var choice=await Choose("Dateien kopieren",$"{selection.Length} Track(s) nach {target.Path}. Bei gleichen Dateinamen:",["Überspringen","Beide behalten","Abbrechen"]);
            if(choice<0 || choice==2) return; collision=choice==0?CopyCollision.Skip:CopyCollision.KeepBoth;
        }
        _job=new(); _cancel.IsVisible=true;
        try
        {
            var progress=new Progress<TransferProgress>(p=>Status($"{p.Completed}/{p.Total} · {p.Message}"));
            await Task.Run(()=>_actions.AddAsync(target,selection,collision,progress,_job.Token));
            await RefreshLists(); await RefreshClub(); Status("Ziel aktualisiert.");
        }
        finally { _job.Dispose(); _job=null; _cancel.IsVisible=false; }
    }
    private async Task AddFolderAsync()
    {
        var chosen=await StorageProvider.OpenFolderPickerAsync(new() { Title="Musikordner hinzufügen",AllowMultiple=false });
        var path=chosen.FirstOrDefault()?.TryGetLocalPath(); if(path==null) return;
        var folders=(await _quickStore.LoadAsync()).ToList(); if(!folders.Any(f=>string.Equals(f.Path,path,StringComparison.OrdinalIgnoreCase))) folders.Add(new(Path.GetFileName(path),path));
        await _quickStore.SaveAsync(folders); await ReloadBrowsers();
    }
    private async Task ScanSelectedAsync()
    {
        if(_job!=null) { Status("Ein Auftrag läuft bereits."); return; }
        if(_selectedBrowser?.Target is not { Kind:LibraryTargetKind.Directory,Path:{ } path }) { Status("Zuerst einen Festplattenordner auswählen."); return; }
        var root=(await _roots.GetAllAsync()).Where(r=>string.Equals(Path.GetFullPath(r.Path).TrimEnd('\\','/'),Path.GetFullPath(path).TrimEnd('\\','/'),StringComparison.OrdinalIgnoreCase) || Path.GetFullPath(path).StartsWith(Path.GetFullPath(r.Path).TrimEnd('\\','/')+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)).OrderByDescending(r=>r.Path.Length).FirstOrDefault()
            ?? await _roots.AddOrGetAsync(path,Path.GetFileName(path));
        _job=new(); _cancel.IsVisible=true;
        try { await Task.Run(()=>_scanner.ScanFolderAsync(root,path,new Progress<ScanProgress>(p=>Status($"Scan: {p.FilesSeen:N0} Dateien · {p.FilesAdded:N0} neu · {p.FilesUpdated:N0} aktualisiert · {p.FilesFailed:N0} nicht lesbar · {p.CurrentRelativePath}")),_job.Token)); await RefreshLists(); await ReloadBrowsers(); await RefreshClub(); Status("Scan abgeschlossen."); }
        finally { _job.Dispose(); _job=null; _cancel.IsVisible=false; }
    }
    private async Task RelocateAsync()
    {
        var available=await _roots.GetAllAsync();
        if(available.Count==0) { Status("Zuerst eine Collection importieren oder einen Ordner scannen."); return; }
        var dialog=Dialog("Dateipfade lokal zuordnen",650,350);
        var panel=new StackPanel { Margin=new Thickness(20),Spacing=12 };
        panel.Children.Add(new TextBlock { Text="Wähle die bisherige Wurzel und optional einen relativen Unterordner. Danach den lokal entsprechenden Ordner auswählen. Traktor-Pfade und Track-IDs bleiben erhalten.",TextWrapping=TextWrapping.Wrap });
        var rootPicker=new ComboBox { ItemsSource=available,SelectedIndex=0,HorizontalAlignment=HorizontalAlignment.Stretch,ItemTemplate=new FuncDataTemplate<LibraryRoot>((r,_)=>new TextBlock { Text=r?.Path }) }; panel.Children.Add(rootPicker);
        var prefix=new TextBox { PlaceholderText="Optional: bisheriger Unterordner, z. B. Musik/House" }; panel.Children.Add(prefix);
        panel.Children.Add(Button("Lokalen Zielordner auswählen …",async ()=>
        {
            if(rootPicker.SelectedItem is not LibraryRoot root) return;
            var folders=await StorageProvider.OpenFolderPickerAsync(new() { Title="Entsprechenden lokalen Ordner auswählen",AllowMultiple=false });
            var path=folders.FirstOrDefault()?.TryGetLocalPath(); if(path==null) return;
            await _roots.RelocateAsync(root.Id,path,prefix.Text??""); dialog.Close(); await ReloadBrowsers(); await RefreshLists(); Status("Pfadzuordnung gespeichert. Den zugeordneten Ordner scannen, um Verfügbarkeit und Dateigrößen zu aktualisieren.");
        })); dialog.Content=panel; await dialog.ShowDialog(this);
    }
    private async Task CreateNode(PlaylistNodeType type,LibraryTarget? location=null)
    {
        var name=await Prompt(type==PlaylistNodeType.Playlist?"Neue Playlist":"Neue Playlist-Gruppe",""); if(string.IsNullOrWhiteSpace(name)) return;
        var target=location??_selectedBrowser?.Target;
        var parent=target?.Kind is LibraryTargetKind.PlaylistFolder or LibraryTargetKind.ClubCollection or LibraryTargetKind.ClubUnsorted?target.PlaylistId:null;
        if(target is {Kind:LibraryTargetKind.Playlist,PlaylistId:{ } selected}) parent=(await _playlists.GetAllNodesAsync()).FirstOrDefault(n=>n.Id==selected)?.ParentId;
        var created=await _playlists.CreateAsync(name.Trim(),type,parent); _revealPlaylist=created.Id; await ReloadBrowsers();
    }
    private async Task NewProfileAsync()
    {
        var name=await Prompt("Neues Favoritenprofil",""); if(string.IsNullOrWhiteSpace(name)) return;
        name=name.Trim(); if(_favorites.Profiles.Any(p=>p.Name==name)) { Status("Profilname existiert bereits."); return; }
        var profile=new FavoriteProfile { Name=name }; _favorites.Profiles.Add(profile); _profile.ItemsSource=null; _profile.ItemsSource=_favorites.Profiles; _profile.SelectedItem=profile; await SaveFavorites(); RebuildSlots();
    }
    private async Task DeleteProfileAsync()
    {
        if(ActiveProfile is not { } profile || _favorites.Profiles.Count==1) { Status("Mindestens ein Profil bleibt erhalten."); return; }
        if(await Choose("Profil löschen?",profile.Name,["Löschen","Abbrechen"])!=0) return;
        _favorites.Profiles.Remove(profile); _profile.ItemsSource=null; _profile.ItemsSource=_favorites.Profiles; _profile.SelectedIndex=0; await SaveFavorites(); RebuildSlots();
    }
    private async Task EditTags(int index)
    {
        if(_tables[index].SelectedItem is not Track track) return;
        var dialog=Dialog("Metadaten bearbeiten",520,540);
        var panel=new StackPanel { Spacing=8,Margin=new Thickness(20) };
        var fields=new Dictionary<string,TextBox>();
        foreach(var pair in new[] { ("Titel",track.Title),("Artist",track.Artist),("Album",track.Album),("Genre",track.GenreRaw),("Kommentar",track.Comment),("Key",track.MusicalKey),("BPM",track.Bpm?.ToString()) })
        { panel.Children.Add(Label(pair.Item1)); var text=new TextBox { Text=pair.Item2 }; fields[pair.Item1]=text; panel.Children.Add(text); }
        var original=fields.ToDictionary(p=>p.Key,p=>p.Value.Text??"");
        panel.Children.Add(Button("Speichern",async ()=>
        {
            double? bpm=null;
            if(!string.IsNullOrWhiteSpace(fields["BPM"].Text)) { if(!double.TryParse(fields["BPM"].Text,out var parsed) || !double.IsFinite(parsed) || parsed<=0) { Status("BPM muss eine positive Zahl sein."); return; } bpm=parsed; }
            track.Title=fields["Titel"].Text; track.Artist=fields["Artist"].Text; track.Album=fields["Album"].Text; track.GenreRaw=fields["Genre"].Text; track.Comment=fields["Kommentar"].Text; track.MusicalKey=fields["Key"].Text; track.Bpm=bpm; track.DateModifiedUtc=DateTime.UtcNow;
            var keys=new Dictionary<string,string>{{"Titel","title"},{"Artist","artist"},{"Album","album"},{"Genre","genre"},{"Kommentar","comment"},{"Key","key"},{"BPM","bpm"}};
            await _tracks.UpdateMetadataAsync(track,fields.Where(p=>(p.Value.Text??"")!=original[p.Key]).Select(p=>keys[p.Key]).ToArray()); dialog.Close(); await RefreshLists();
        }));
        dialog.Content=new ScrollViewer { Content=panel }; await dialog.ShowDialog(this);
    }
    private async Task MidiDialog()
    {
        var dialog=Dialog("MIDI-Eingang",480,280); var panel=new StackPanel { Margin=new Thickness(20),Spacing=12 };
        panel.Children.Add(new TextBlock { Text="Gerät verbinden, danach am Favoriten-Slot ‚MIDI lernen‘ wählen.",TextWrapping=TextWrapping.Wrap });
        var devices=WindowsMidiInput.Devices(); var picker=new ComboBox { ItemsSource=devices,SelectedIndex=devices.Count>0?0:-1,HorizontalAlignment=HorizontalAlignment.Stretch }; panel.Children.Add(picker);
        panel.Children.Add(Button("Verbinden",()=> { if(picker.SelectedIndex<0) { Status("Kein MIDI-Gerät gefunden."); return Task.CompletedTask; } _midi.Open(picker.SelectedIndex); Status("MIDI verbunden: "+devices[picker.SelectedIndex]); dialog.Close(); return Task.CompletedTask; }));
        panel.Children.Add(Button("Trennen / Learn abbrechen",()=> { _midi.Dispose(); _learnSlot=null; Status("MIDI getrennt."); dialog.Close(); return Task.CompletedTask; })); dialog.Content=panel; await dialog.ShowDialog(this);
    }
    private Task ImportAsync() => ImportAsync(false);
    private async Task ImportAsync(bool changeSource)
    {
        if(_job!=null || _syncActive || _planningView?.Busy==true) { Status("Ein Auftrag läuft bereits."); return; }
        var path=changeSource?null:await _sync.GetSourcePathAsync();
        if(path!=null&&!File.Exists(path))
        {
            await Choose("Traktor-Quelle nicht erreichbar",$"Die gespeicherte NML ist derzeit nicht erreichbar:\n{path}\nBitte Laufwerk verbinden oder per Rechtsklick auf ‚Traktor abgleichen‘ eine andere Quelle wählen.",["Schließen"]);
            return;
        }
        if(path==null)
        {
            var files=await StorageProvider.OpenFilePickerAsync(new() { Title="Traktor collection.nml einmalig auswählen",AllowMultiple=false,FileTypeFilter=[new("Traktor NML") { Patterns=["*.nml"] }] });
            path=files.FirstOrDefault()?.TryGetLocalPath(); if(path==null) return;
        }
        _syncActive=true;
        try
        {
        Status("Abgleichvorschau wird berechnet …"); var preview=await Task.Run(()=>_sync.PreviewAsync(path));
        if(preview.Changes.All(c=>!c.Conflict))
        {
            IsEnabled=false;
            try
            {
                if(preview.Changes.Count>0) await Task.Run(()=>_sync.ApplyAsync(preview,progress:new Progress<TransferProgress>(p=>Status($"Traktor-Import: {p.Completed:N0}/{p.Total:N0} Tracks"))));
                await ShowImportedCollection();
            }
            finally { IsEnabled=true; }
            return;
        }
        var dialog=Dialog("Traktor-Abgleich prüfen",1000,680); var layout=new Grid { RowDefinitions=new("Auto,Auto,*,Auto"),Margin=new Thickness(18) };
        layout.Children.Add(new TextBlock { Text=preview.Warning,TextWrapping=TextWrapping.Wrap,Foreground=Muted,Margin=new Thickness(0,0,0,10) });
        var summary=new TextBlock { Text=$"{preview.Changes.Count:N0} Änderungen · {preview.Changes.Count(c=>c.Conflict):N0} Konflikte. Auswahl: Traktor übernehmen oder Cratebase behalten.",Margin=new Thickness(0,8) }; Grid.SetRow(summary,1); layout.Children.Add(summary);
        var decisions=new HashSet<SyncChange>();
        var list=new ListBox { ItemsSource=preview.Changes,ItemTemplate=new FuncDataTemplate<SyncChange>((change,_)=>
        {
            if(change==null) return new TextBlock();
            var row=new Grid { ColumnDefinitions=new("*,190"),Margin=new Thickness(0,5) };
            var description=new StackPanel(); description.Children.Add(new TextBlock { Text=change.Description,TextTrimming=TextTrimming.CharacterEllipsis,Foreground=change.Conflict?Brushes.Orange:Brushes.White });
            description.Children.Add(new TextBlock { Text=$"Cratebase: {change.Local??"—"}   →   Traktor: {change.Remote??"—"}",FontSize=11,Foreground=Muted,TextTrimming=TextTrimming.CharacterEllipsis,MaxWidth=650 }); row.Children.Add(description);
            var picker=new ComboBox { ItemsSource=change.Conflict ? new[] { "Entscheiden …","Traktor übernehmen","Cratebase behalten" } : new[] { "Traktor übernehmen","Cratebase behalten" },SelectedIndex=change.Conflict ? (decisions.Contains(change)?(change.UseRemote?1:2):0) : (change.UseRemote?0:1),Width=185 };
            picker.SelectionChanged+=(_,_)=> { if(change.Conflict) { if(picker.SelectedIndex==0) decisions.Remove(change); else { decisions.Add(change); change.UseRemote=picker.SelectedIndex==1; } } else change.UseRemote=picker.SelectedIndex==0; };
            Grid.SetColumn(picker,1); row.Children.Add(picker); return row;
        }) };
        Grid.SetRow(list,2); layout.Children.Add(list);
        var buttons=Row(); Grid.SetRow(buttons,3);
        buttons.Children.Add(Button("Auswahl übernehmen",async ()=>
        {
            if(preview.Changes.Any(c=>c.Conflict&&!decisions.Contains(c))) { summary.Text="Bitte zuerst alle Konflikte entscheiden."; return; }
            dialog.IsEnabled=false;
            try { await Task.Run(()=>_sync.ApplyAsync(preview)); dialog.Close(); await ShowImportedCollection(); }
            finally { dialog.IsEnabled=true; }
        }));
        buttons.Children.Add(Button("Abbrechen",()=> { dialog.Close(); return Task.CompletedTask; })); layout.Children.Add(buttons); dialog.Content=layout; await dialog.ShowDialog(this);
        }
        catch(Exception ex)
        {
            Status("Traktor-Import fehlgeschlagen: "+ex.Message);
            await Choose("Traktor-Import fehlgeschlagen",ex.Message,["Schließen"]);
        }
        finally { _syncActive=false; }
    }
    private async Task ShowImportedCollection()
    {
        await ReloadBrowsers();
        _panes[_active].Search="";
        await Open(new(LibraryTargetKind.TraktorCollection,"Traktor Collection"),_active);
        if(_settings.DualPane) await _panes[1-_active].ReloadAsync();
        await RefreshClub();
        var totals=await _query.TotalsAsync(new(new(LibraryTargetKind.TraktorCollection,"Traktor Collection")));
        Status($"Traktor Collection geladen: {totals.Count:N0} Tracks. NML-Quelle gespeichert; weitere Abgleiche verwenden dieselbe Datei.");
    }
    private async Task ExportClubAsync()
    {
        if(_job!=null || _syncActive) { Status("Ein Auftrag läuft bereits."); return; }
        var count=(await _query.TotalsAsync(new(new(LibraryTargetKind.ClubCollection,"Club Collection")))).Count;
        if(count==0) { Status("Die Club Collection ist leer. Zuerst Tracks als Club-tauglich markieren."); return; }
        var file=await StorageProvider.SaveFilePickerAsync(new() { Title="Club Collection als separate Traktor-NML exportieren",SuggestedFileName="Cratebase-Club-Collection.nml",DefaultExtension="nml",FileTypeChoices=[new("Traktor NML") { Patterns=["*.nml"] }] });
        var path=file?.TryGetLocalPath(); if(path==null) return;
        if(string.Equals(Path.GetFileName(path),"collection.nml",StringComparison.OrdinalIgnoreCase)) { Status("Bitte einen separaten Namen für die Club-NML verwenden."); return; }
        _syncActive=true;
        try { await Task.Run(()=>_sync.ExportClubAsync(path)); Status($"Club-NML exportiert: {count:N0} Tracks mit gefilterten Playlisten und Cues. Die NML enthält Dateiverweise; Audiodateien zusätzlich auf dem Laptop bereitstellen."); }
        finally { _syncActive=false; }
    }
    private async Task ExportAsync()
    {
        if(_job!=null) { Status("Ein Auftrag läuft bereits."); return; }
        var file=await StorageProvider.SaveFilePickerAsync(new() { Title="Separate NML exportieren",SuggestedFileName="Cratebase-Export.nml",DefaultExtension="nml",FileTypeChoices=[new("Traktor NML") { Patterns=["*.nml"] }] });
        var path=file?.TryGetLocalPath(); if(path==null) return;
        if(string.Equals(Path.GetFileName(path),"collection.nml",StringComparison.OrdinalIgnoreCase)) { Status("Bitte einen separaten Exportnamen verwenden, beispielsweise Cratebase-Export.nml."); return; }
        await Task.Run(()=>_sync.ExportAsync(path)); Status("Export gespeichert. In Traktor importieren, danach hier ‚Übernahme bestätigen‘ wählen.");
    }
    private async Task ConfirmAsync()
    {
        if(_syncActive||_planningView?.Busy==true){Status("Bitte den laufenden Auftrag abwarten.");return;}
        if(await Choose("Traktor-Übernahme bestätigen","Hast du die zuletzt exportierte NML erfolgreich in Traktor importiert? Erst diese Bestätigung setzt den gemeinsamen Abgleichstand.",["Ja, übernommen","Abbrechen"])!=0) return;
        await _sync.ConfirmExportAsync(); Status("Gemeinsamer Abgleichstand bestätigt.");
    }
    private Task SaveSettings() => _state.WriteAsync("workspace-layout",JsonSerializer.Serialize(_settings));
    private Task SaveFavorites() { _favorites.ActiveProfileName=ActiveProfile?.Name; return _favoriteStore.SaveAsync(_favorites); }
    private void Status(string text) { if(Dispatcher.UIThread.CheckAccess()) _status.Text=text; else Dispatcher.UIThread.Post(()=>_status.Text=text); }
    private async Task Safe(Func<Task> work) { try { await work(); } catch(OperationCanceledException) { Status("Auftrag abgebrochen."); } catch(Exception ex) { Status(ex.Message); } }
    private async void OnClosing(object? sender,WindowClosingEventArgs e)
    {
        if(_closing) return;
        if(_recorder.StartRecordingCommand.IsRunning || _recorder.StopRecordingCommand.IsRunning)
        { e.Cancel=true; Status("Bitte warten, bis der Recorder den Start oder das Speichern abgeschlossen hat."); return; }
        if(_job!=null || _syncActive || _planningView?.Busy==true) { e.Cancel=true; _job?.Cancel(); Status("Bitte warten, bis der laufende Auftrag beendet ist, und danach schließen."); return; }
        e.Cancel=true; _closing=true; _job?.Cancel();
        try { await _recorder.StopRecordingCommand.ExecuteAsync(null); }
        catch(Exception ex) { _closing=false; Status($"Aufnahme konnte nicht beendet werden: {ex.Message}"); return; }
        if(_recorder.IsRecording) { _closing=false; Status(_recorder.StatusMessage); return; }
        await Safe(async ()=> { await SaveSettings(); await SaveFavorites(); });
        try {if(_planningView!=null)await _planningView.SaveAsync();}
        catch(Exception ex){_closing=false;Status("Set-Plan konnte nicht gespeichert werden: "+ex.Message);return;}
        _planningView?.Dispose();
        _mediaSession?.Dispose();
        await StopServerAsync();
        _player.AvailabilityChanged-=TrackAvailabilityChanged;_availabilityRefresh?.Cancel();
        DisposeKeyboard();
        foreach(var pane in _panes) pane.Dispose(); _player.Dispose(); _midi.Dispose(); Close();
    }
    private Button Button(string text,Func<Task> action)
    { var button=new Button { Content=text,Padding=new Thickness(10,7) }; button.Click+=async (_,_)=>await Safe(action); return button; }
    private MenuItem Menu(string text,Func<Task> action)
    { var item=new MenuItem { Header=text }; item.Click+=async (_,_)=>await Safe(action); return item; }
    private static StackPanel Row() => new() { Orientation=Orientation.Horizontal,Spacing=6,VerticalAlignment=VerticalAlignment.Center };
    private static TextBlock Label(string text) => new() { Text=text,Foreground=Muted,FontSize=10,Margin=new Thickness(2,8),VerticalAlignment=VerticalAlignment.Center };
    private static TextBlock BoundText(string path,double size)
    { var text=new TextBlock { FontSize=size,TextTrimming=TextTrimming.CharacterEllipsis,VerticalAlignment=VerticalAlignment.Center }; text.Bind(TextBlock.TextProperty,new Binding(path)); return text; }
    private Window Dialog(string title,double width,double height) => new() { Title=title,Width=width,Height=height,WindowStartupLocation=WindowStartupLocation.CenterOwner,CanResize=true };
    private async Task<string?> Prompt(string title,string initial)
    {
        var dialog=Dialog(title,460,180); var panel=new StackPanel { Margin=new Thickness(20),Spacing=12 };
        var text=new TextBox { Text=initial }; panel.Children.Add(text); var row=Row();
        row.Children.Add(Button("Übernehmen",()=> { dialog.Close(text.Text); return Task.CompletedTask; })); row.Children.Add(Button("Abbrechen",()=> { dialog.Close(null); return Task.CompletedTask; })); panel.Children.Add(row); dialog.Content=panel;
        return await dialog.ShowDialog<string?>(this);
    }
    private async Task<int> Choose(string title,string message,string[] choices)
    {
        var dialog=Dialog(title,600,230); var panel=new StackPanel { Margin=new Thickness(20),Spacing=18 };
        panel.Children.Add(new TextBlock { Text=message,TextWrapping=TextWrapping.Wrap }); var row=Row();
        for(var i=0;i<choices.Length;i++) { var choice=i; row.Children.Add(Button(choices[i],()=> { dialog.Close(choice); return Task.CompletedTask; })); }
        panel.Children.Add(row); dialog.Content=panel; return await dialog.ShowDialog<int?>(this)??-1;
    }
}
