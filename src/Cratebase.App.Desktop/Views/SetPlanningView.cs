using System.Collections.ObjectModel;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using Cratebase.Analysis;
using Cratebase.App.Desktop.Controls;
using Cratebase.Audio;
using Cratebase.Core.Abstractions;
using Cratebase.Core.Models;

namespace Cratebase.App.Desktop.Views;

/// <summary>Separate workspace state; source audio and imported Traktor cues are read-only.</summary>
public sealed partial class SetPlanningView : UserControl, IDisposable
{
    private readonly IWorkspaceStateStore _state;
    private readonly ITrackRepository _tracks;
    private readonly ILibraryRootRepository _roots;
    private readonly IPlaylistRepository _playlists;
    private readonly ICuePointRepository? _cues;
    private readonly IWaveformGenerator _generator;
    private readonly IWaveformCache _cache;
    private readonly ITrackWaveformSource _stripes;
    private readonly Func<int,CancellationToken,Task<IReadOnlyList<Track>>> _candidates;
    private readonly Func<IReadOnlyList<Track>> _selection;
    private readonly Func<Task> _playlistChanged;
    private readonly Action _pauseLibrary;
    private readonly Action<string> _status;
    private readonly LocalPlaylistAi _ai;
    private readonly string _directory;
    private readonly Func<string> _ffmpeg;
    private readonly Func<bool> _libraryLocked;
    private readonly ObservableCollection<PlanRow> _rows=[];
    private readonly ComboBox _plans=new() {Width=160};
    private readonly TextBox _name=new() {Text="Neues Set",Width=160};
    private readonly TextBox _brief=new() {PlaceholderText="Thema: depressiver Hardstyle, Wasted Penguinz …",Width=330};
    private readonly NumericUpDown _minutes=new() {Minimum=5,Maximum=1440,Value=60,Width=65,ShowButtonSpinner=false};
    private readonly NumericUpDown _step=new() {Minimum=0,Maximum=50,Value=4,Width=60,ShowButtonSpinner=false};
    private readonly CheckBox _openFormat=new() {Content="Genre-Wechsel erlauben"};
    private readonly CheckBox _online=new() {Content="Öffentliche Set-Inspiration suchen",IsChecked=true};
    private readonly CheckBox _focusGenres=new() {Name="SetFocusGenres",Content="Zuerst passende Genre-Ordner",IsChecked=true};
    private readonly TextBox _journey=new() {PlaceholderText="Genre-Reise optional: Goth > EBM > Techno > Hard Techno",Width=390};
    private readonly TextBlock _details=new() {TextWrapping=TextWrapping.Wrap,MaxHeight=65};
    private readonly DataGrid _table;
    private readonly Deck _a;
    private readonly Deck _b;
    private readonly List<Button> _workButtons=[];
    private readonly Button _planCancel=new() {Content="Auftrag abbrechen",IsVisible=false};
    private List<SetPlan> _saved=[];
    private SetPlan _plan=new();
    private bool _initialized,_rebuilding;
    private CancellationTokenSource? _job,_wave;
    private IAudioPlayer? _preview;
    private readonly Func<IAudioPlayer> _audioFactory;
    private Deck? _auditionDeck;
    private bool _auditionPaused;
    private long? _auditionSeek;
    public bool Busy=>_job!=null;

    public SetPlanningView(IWorkspaceStateStore state,ITrackRepository tracks,ILibraryRootRepository roots,IPlaylistRepository playlists,ICuePointRepository? cues,
        IWaveformGenerator generator,IWaveformCache cache,ITrackWaveformSource stripes,
        Func<int,CancellationToken,Task<IReadOnlyList<Track>>> candidates,Func<IReadOnlyList<Track>> selection,Func<Task> playlistChanged,Action pauseLibrary,Action<string> status,Func<string> ffmpeg,Func<bool>? libraryLocked=null,ILibraryQuery? browserQuery=null,Func<LibraryTarget,CancellationToken,Task>? prepareBrowserSource=null,Func<IAudioPlayer>? audioFactory=null,Func<string,CancellationToken,Task>? indexFolder=null)
    {
        _state=state;_tracks=tracks;_roots=roots;_playlists=playlists;_cues=cues;_generator=generator;_cache=cache;_stripes=stripes;
        _candidates=candidates;_selection=selection;_playlistChanged=playlistChanged;_pauseLibrary=pauseLibrary;_status=status;_ffmpeg=ffmpeg;
        _libraryLocked=libraryLocked??(()=>false);
        _browserQuery=browserQuery;_prepareBrowserSource=prepareBrowserSource;
        _indexFolder=indexFolder;
        _audioFactory=audioFactory??(()=>new LibVlcAudioPlayer());
        _directory=Path.Combine(Environment.GetEnvironmentVariable("CRATEBASE_DATA_DIR")??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Cratebase"),"planning");
        _ai=new(Path.Combine(Path.GetDirectoryName(_directory)!,"ai"));
        var layout=new Grid {RowDefinitions=new("Auto,Auto,240,*,Auto"),RowSpacing=10,Margin=new Thickness(10)};
        var tools=new WrapPanel {Orientation=Orientation.Horizontal};
        foreach(var c in new Control[]{_plans,_name,Work("Neues Set",NewAsync),Work("Speichern",SaveAsync),Work("Auswahl anhängen",AppendSelectionAsync),Work("↑",()=>MoveAsync(-1)),Work("↓",()=>MoveAsync(1)),Work("Entfernen",RemoveAsync),Work("Als Playlist speichern",PublishAsync)}.Where(c=>browserQuery==null||c is not Button {Content:"Auswahl anhängen"}))tools.Children.Add(c);
        layout.Children.Add(tools);
        tools.Children.Add(Work("Session-Histories …",HistoriesAsync));
        _planCancel.Click+=(_,_)=>_job?.Cancel();tools.Children.Add(_planCancel);
        _plans.ItemTemplate=new FuncDataTemplate<SetPlan>((p,_)=>new TextBlock {Text=p?.Name});
        _plans.SelectionChanged+=async(_,_)=>{if(!_rebuilding&&_plans.SelectedItem is SetPlan plan)await Run(async()=>{await SaveCurrentAsync();_plan=plan;RefreshPicker();await LoadRowsAsync();});};
        var generation=new WrapPanel {Orientation=Orientation.Horizontal};
        var preset=new ComboBox {ItemsSource=new[]{"Eigenes Thema","Dark to Hard","Melancholischer Hardstyle","House / Techno"},SelectedIndex=0,Width=185};
        preset.SelectionChanged+=(_,_)=>
        {
            if(preset.SelectedIndex==1){_brief.Text="Dunkler Club-Aufbau von Goth, EBM und Aggrotech über Dark Techno bis Hard Techno. Passende Brücken und allmähliche Steigerung.";_journey.Text="Goth > EBM > Aggrotech > Dark Techno > Hard Techno";_openFormat.IsChecked=true;}
            if(preset.SelectedIndex==2){_brief.Text="Depressiver, melancholischer und emotionaler Hardstyle mit Klassikern von Wasted Penguinz und musikalisch passenden Tracks. Keine beliebigen Hardstyle-Tracks ohne passende Stimmung.";_journey.Text="";_openFormat.IsChecked=false;}
            if(preset.SelectedIndex==3){_brief.Text="Ein zusammenhängendes Club-Set aus House und Techno mit passenden BPM, Tonarten und kurzen Basswechseln.";_journey.Text="House > Techno";_openFormat.IsChecked=true;}
        };generation.Children.Add(preset);
        ToolTip.SetTip(_focusGenres,"Grenzt anhand genannter Genres und gespeicherter Ordnerpfade ein. Keine passenden Treffer: breitere Metadatensuche. Ausschalten für freie Suche über alle Genres.");
        foreach(var c in new Control[]{new TextBlock {Text="Minuten",VerticalAlignment=VerticalAlignment.Center},_minutes,new TextBlock {Text="Max. Δ BPM",VerticalAlignment=VerticalAlignment.Center},_step,_openFormat,_journey,_brief,_online,_focusGenres,
            Work("Regelbasiert vorschlagen",()=>GenerateAsync(false)),Work("Lokale KI vorschlagen",()=>GenerateAsync(true))}){c.Margin=new Thickness(3);generation.Children.Add(c);}
        var options=new Button {Content="KI & Referenzen ▾",Margin=new Thickness(3)};var menu=new ContextMenu();
        foreach(var item in new (string Name,Func<Task> Action)[]{("Lokale KI einrichten (2,54 GB)",InstallAiAsync),("Set-Referenzen",ReferencesAsync),("GPU / CPU wechseln",()=>{_ai.Stop();_ai.CpuOnly=!_ai.CpuOnly;_status(_ai.CpuOnly?"Lokale KI: CPU-Modus":"Lokale KI: GPU-Modus (Vulkan)");return Task.CompletedTask;}),("KI stoppen",()=>{_ai.Stop();return Task.CompletedTask;})})
        {var entry=new MenuItem {Header=item.Name};entry.Click+=async(_,_)=>await Run(item.Action);menu.Items.Add(entry);}
        options.ContextMenu=menu;options.Click+=(_,_)=>menu.Open(options);_workButtons.Add(options);generation.Children.Add(options);
        var generationSection=new Expander {Header="Set-Vorschläge · Regeln, Thema & lokale KI",Content=generation,IsExpanded=false,HorizontalAlignment=HorizontalAlignment.Stretch};
        var generationRow=new Grid {ColumnDefinitions=new("*,Auto")};generationRow.Children.Add(generationSection);
        _candidateListButton.Click+=(_,_)=>ShowCandidateList();Grid.SetColumn(_candidateListButton,1);generationRow.Children.Add(_candidateListButton);
        Grid.SetRow(generationRow,1);layout.Children.Add(generationRow);
        var decks=new Grid {ColumnDefinitions=new("*,*"),ColumnSpacing=12};
        _a=MakeDeck("A · ausgehender Track");_b=MakeDeck("B · nächster Track");
        decks.Children.Add(_a.View);Grid.SetColumn(_b.View,1);decks.Children.Add(_b.View);Grid.SetRow(decks,2);layout.Children.Add(decks);
        _table=new DataGrid {Name="SetPlanTracks",ItemsSource=_rows,AutoGenerateColumns=false,IsReadOnly=true,SelectionMode=DataGridSelectionMode.Single,CanUserSortColumns=false,RowHeight=26,ColumnHeaderHeight=28};
        foreach(var c in new[]{("#","Number",45d),("Artist","Artist",170d),("Titel","Title",320d),("Genre","Genre",160d),("BPM","Bpm",70d),("Key","Key",65d),("Übergang zum nächsten Track","Assessment",420d)})
            _table.Columns.Add(new DataGridTextColumn {Header=c.Item1,Binding=new Binding(c.Item2),Width=new DataGridLength(c.Item3)});
        _table.SelectionChanged+=async(_,_)=>{if(!_rebuilding&&!Busy)await Run(LoadDecksAsync);};Grid.SetRow(_table,3);layout.Children.Add(_table);
        _table.DoubleTapped+=async(_,_)=>await Run(()=>ToggleDeckAsync(_a));
        var bottom=new StackPanel {Spacing=6};var preview= new WrapPanel();
        _details.Bind(ToolTip.TipProperty,new Binding(nameof(TextBlock.Text)){Source=_details});
        preview.Children.Add(Work("Marker übernehmen",ApplyMarkersAsync));preview.Children.Add(Work("Kurzen Basswechsel vorschlagen",SuggestMarkersAsync));
        preview.Children.Add(Work("Übergang rendern & vorhören",PreviewAsync));var stop=new Button {Content="Vorschau stoppen"};stop.Click+=(_,_)=>StopPreview();preview.Children.Add(stop);
        preview.Children.Add(Work("Vorschau Play/Pause",TogglePreviewAsync));
        bottom.Children.Add(preview);bottom.Children.Add(_details);Grid.SetRow(bottom,4);layout.Children.Add(bottom);
        if(browserQuery!=null)
        {
            var shell=new Grid {ColumnDefinitions=new("360,6,*")};
            _browserHost=BuildPlanningBrowser(browserQuery);shell.Children.Add(_browserHost);
            var splitter=new GridSplitter {ResizeDirection=GridResizeDirection.Columns,Background=Brush.Parse("#29313E")};Grid.SetColumn(splitter,1);shell.Children.Add(splitter);
            Grid.SetColumn(layout,2);shell.Children.Add(layout);Content=shell;
        }
        else Content=layout;
        WirePlanDrop();
    }
    private Button Work(string text,Func<Task> action) {var b=new Button {Content=text,Margin=new Thickness(3),Padding=new Thickness(9,6)};b.Click+=async(_,_)=>await Run(action);_workButtons.Add(b);return b;}
    private async Task Run(Func<Task> work)
    {
        if(_libraryLocked()){_status("Während des Traktor-Abgleichs ist die Set-Planung gesperrt.");return;}
        if(Busy){_status("Bitte den laufenden Planungsauftrag abwarten oder abbrechen.");return;}
        using var job=_job=new();foreach(var button in _workButtons)button.IsEnabled=false;
        _planCancel.IsVisible=true;
        _plans.IsEnabled=false;_table.IsEnabled=false;_a.View.IsEnabled=false;_b.View.IsEnabled=false;
        if(_browserHost!=null)_browserHost.IsEnabled=false;
        try {await work();}catch(OperationCanceledException){_status("Planungsauftrag abgebrochen.");}catch(Exception ex){_status(ex.Message);_details.Text=ex.Message;}
        finally {_ai.Stop();_job=null;_planCancel.IsVisible=false;foreach(var button in _workButtons)button.IsEnabled=true;_plans.IsEnabled=true;_table.IsEnabled=true;_a.View.IsEnabled=true;_b.View.IsEnabled=true;if(_browserHost!=null)_browserHost.IsEnabled=true;}
    }
    private CancellationToken Token=>_job?.Token??CancellationToken.None;
    public async Task InitializeAsync()
    {
        if(_initialized)return;
        _saved=JsonSerializer.Deserialize<List<SetPlan>>(await _state.ReadAsync(SetPlan.StateKey)??"[]")??[];
        _plan=_saved.FirstOrDefault()??new();_initialized=true;RefreshPicker();await LoadRowsAsync();
        if(_saved.Count==0)_plan.References=[new("https://www.youtube.com/watch?v=lqAxGUNf4ZQ",""),new("https://www.youtube.com/watch?v=P6zJFGMcWdY",""),new("https://www.youtube.com/watch?v=3b7hz1howpo","")];
        if(_browserPane!=null){await ReloadPlanningBrowserAsync();await _browserPane.ReloadAsync();}
    }
    private void RefreshPicker(){_rebuilding=true;_plans.ItemsSource=_saved.ToArray();_plans.SelectedItem=_saved.FirstOrDefault(p=>p.Id==_plan.Id);_name.Text=_plan.Name;_rebuilding=false;}
    private async Task SaveCurrentAsync()
    {
        if(!_initialized)return;_plan.Name=string.IsNullOrWhiteSpace(_name.Text)?"Neues Set":_name.Text.Trim();
        if(_plan.Name.Length>160)_plan.Name=_plan.Name[..160];
        if(_libraryLocked())throw new InvalidOperationException("Während des Traktor-Abgleichs kann der Set-Plan nicht gespeichert werden.");
        _saved.RemoveAll(p=>p.Id==_plan.Id);_saved.Add(_plan);await _state.WriteAsync(SetPlan.StateKey,JsonSerializer.Serialize(_saved));
    }
    public async Task SaveAsync(){await SaveCurrentAsync();RefreshPicker();_status("Set-Plan gespeichert. Traktor-Cues bleiben unverändert.");}
    private async Task NewAsync(){await SaveCurrentAsync();StopPreview();_plan=new();RefreshPicker();await LoadRowsAsync();}
    private async Task LoadRowsAsync()
    {
        StopPreview();var index=Math.Max(0,_table.SelectedIndex);var tracks=(await _tracks.GetByIdsAsync(_plan.Entries.Select(e=>e.TrackId).Distinct().ToArray(),Token)).ToDictionary(t=>t.Id);
        _rebuilding=true;_rows.Clear();
        for(var i=0;i<_plan.Entries.Count;i++)
        {
            var entry=_plan.Entries[i];tracks.TryGetValue(entry.TrackId,out var track);
            var next=i+1<_plan.Entries.Count?tracks.GetValueOrDefault(_plan.Entries[i+1].TrackId):null;
            _rows.Add(new(i+1,entry,track,(track!=null&&next!=null?ClubSetPlanner.Assess(track,next,Options()).Explanation:"—")+(entry.Note.Length>0?" · "+entry.Note:"")));
        }
        _table.SelectedIndex=_rows.Count>0?Math.Min(index,_rows.Count-1):-1;_rebuilding=false;await LoadDecksAsync();
        _details.Text=$"{_rows.Count} Tracks · ungekürzte Laufzeit {TimeSpan.FromMilliseconds(_rows.Sum(r=>(long)(r.Track?.DurationMs??0))).TotalMinutes:0} min. Tracks links auswählen und ins Set ziehen oder doppelklicken. Vorschläge verwenden die Quelle und Suche im Browser links.";
    }
    private SetPlanningOptions Options()=>new((int)(_minutes.Value??60),(double)(_step.Value??4),_openFormat.IsChecked==true);
    private Task AppendSelectionAsync()=>InsertBrowserTracksAsync(_browserTracks==null?_selection():_browserTracks.SelectedItems.OfType<Track>().ToArray(),_plan.Entries.Count);
    private async Task RemoveAsync(){if(_table.SelectedItem is not PlanRow row)return;_plan.Entries.Remove(row.Entry);await SaveCurrentAsync();await LoadRowsAsync();}
    private async Task MoveAsync(int delta){var i=_table.SelectedIndex;var j=i+delta;if(i<0||j<0||j>=_plan.Entries.Count)return;(_plan.Entries[i],_plan.Entries[j])=(_plan.Entries[j],_plan.Entries[i]);_table.SelectedIndex=j;await SaveCurrentAsync();await LoadRowsAsync();}
    private async Task GenerateAsync(bool ai)
    {
        if(ai&&!_ai.Installed)throw new InvalidOperationException("Bitte zuerst unter „KI & Referenzen“ die lokale KI einrichten.");
        if(ai&&string.IsNullOrWhiteSpace(_brief.Text)&&string.IsNullOrWhiteSpace(_journey.Text))throw new ArgumentException("Bitte ein musikalisches Thema oder eine Genre-Reise eingeben bzw. eine Vorlage wählen.");
        _status("Tracks der gewählten Quelle werden für den Set-Vorschlag eingelesen …");
        var pool=_browserPane==null?await _candidates(int.MaxValue,Token):await ReadPlanningBrowserCandidatesAsync(int.MaxValue,Token);
        if(pool.Count==0)throw new InvalidOperationException("Die aktive Browser-Quelle enthält keine passenden Tracks.");
        var selectionNote="";
        if(ai)
        {
            var total=pool.Count;var theme=(_brief.Text??"")+" "+_journey.Text;var ct=Token;var focus=_focusGenres.IsChecked==true;
            var focused=await Task.Run(()=>focus?SetCandidateSelection.FocusGenreFolders(pool,theme,ct):pool,ct);
            pool=await Task.Run(()=>SetCandidateSelection.ForAi(focused,theme,ct:ct),ct);
            selectionNote=$"Datenbank: {total:N0} Tracks; {(focus?$"Genre-/Ordner-Auswahl: {focused.Count:N0}; ":"")}{pool.Count:N0} KI-Kandidaten automatisch vorausgewählt. Bei Gleichstand variiert die Auswahl. Keine Audioanalyse. ";
            _status(selectionNote);
            if(pool.Count==0)throw new InvalidOperationException("Die Quelle enthält keine erreichbaren Tracks mit bekannter Dauer ohne Trash-Markierung.");
            ShowCandidates(pool,"Metadaten-Vorauswahl · die KI-Bewertung steht noch aus.");
        }
        IReadOnlyList<Track> ordered;string explanation;
        if(ai)
        {
            if(_online.IsChecked==true)
            {
                _status("Öffentliche Set-Beschreibungen werden gesucht (keine Audio-Downloads) …");
                try
                {
                    var research=new SetInspiration(Path.Combine(_directory,"research"));
                    foreach(var existing in _plan.References.Where(r=>r.Url.Length>0&&string.IsNullOrWhiteSpace(r.Tracklist)).ToArray())
                    {var fetched=await research.FindAsync(existing.Url,Token);_plan.References.Remove(existing);_plan.References.AddRange(fetched);}
                    var references=await research.FindAsync(_brief.Text??"",Token);foreach(var reference in references){_plan.References.RemoveAll(r=>r.Url==reference.Url);_plan.References.Add(reference);}
                }
                catch(OperationCanceledException){throw;}
                catch(Exception ex){_status("Online-Inspiration nicht verfügbar: "+ex.Message);_plan.References.Add(new("","Recherche fehlgeschlagen; keine externen Referenzen verwenden."));}
            }
            _status("Lokale KI prüft musikalisches Thema und Reihenfolge …");
            var candidateMap=pool.ToDictionary(t=>t.Id);
            var suggestion=await _ai.SuggestLargeAsync(pool,(_brief.Text??"")+"; Genre journey: "+_journey.Text+$"; Target full-track duration: {Options().Minutes} minutes; Max BPM step: {Options().MaxBpmStep}; Allow genre changes: {Options().AllowGenreChanges}",_plan.References,new Progress<string>(message=>{_candidateProgress.Text=message;_status(message);}),Token,
                ids=>{var chosen=ids.Select(id=>candidateMap[id]).ToArray();if(Dispatcher.UIThread.CheckAccess())ShowCandidates(chosen,"Bisherige KI-Vorauswahl · Reihenfolge noch vorläufig.");else Dispatcher.UIThread.Post(()=>ShowCandidates(chosen,"Bisherige KI-Vorauswahl · Reihenfolge noch vorläufig."));});
            var map=pool.ToDictionary(t=>t.Id);ordered=suggestion.Ids.Select(id=>map[id]).ToArray();explanation=suggestion.Explanation;
            // A language model cannot silently override hard tempo/genre constraints.
            for(var i=1;i<ordered.Count;i++){var fit=ClubSetPlanner.Assess(ordered[i-1],ordered[i],Options());if(!fit.Allowed)throw new InvalidOperationException("KI-Vorschlag verletzt deine Grenzen: "+fit.Explanation+" Bitte Thema/BPM-Limit anpassen.");}
            ShowCandidates(ordered,"Fertiger KI-Vorschlag · bitte vorhören.");
        }
        else {var options=Options();var ct=Token;ordered=await Task.Run(()=>ClubSetPlanner.Generate(pool,options,ct),ct);explanation="BPM-/Key-/Genre-Vorschlag. Themen und Genre-Reisen bitte mit der lokalen KI planen oder die Reihenfolge manuell anpassen.";}
        Token.ThrowIfCancellationRequested();
        if(ordered.Count==0){_details.Text=explanation;_status("Keine passenden Tracks für diesen Vorschlag gefunden. Der bestehende Plan bleibt erhalten.");return;}
        // Keep existing work as a separate saved plan; never replace its cue decisions.
        await SaveCurrentAsync();_plan=new() {Name=string.IsNullOrWhiteSpace(_brief.Text)?"Set-Vorschlag":_brief.Text.Trim(),References=_plan.References.ToList(),Entries=ordered.Select(SetPlanEntry.For).ToList()};
        RefreshPicker();await SaveCurrentAsync();RefreshPicker();await LoadRowsAsync();_details.Text=selectionNote+explanation+" · "+_details.Text;_status("Set-Vorschlag erstellt; bitte Übergänge vorhören.");
    }
    private async Task InstallAiAsync(){_status("Lokale KI wird heruntergeladen …");await _ai.InstallAsync(new Progress<string>(_status),Token);_status("Lokale KI bereit. Sie startet erst bei einem KI-Vorschlag.");}
    private Task LoadDecksAsync()
    {
        _wave?.Cancel();_wave=new();var ct=_wave.Token;var i=_table.SelectedIndex;
        _a.Set(i>=0&&i<_rows.Count?_rows[i]:null);_b.Set(i+1>0&&i+1<_rows.Count?_rows[i+1]:null);
        _=LoadDeckWavesAsync(ct);return Task.CompletedTask;
    }
    private async Task LoadDeckWavesAsync(CancellationToken ct)
    {try {await Task.WhenAll(LoadWaveAsync(_a,ct),LoadWaveAsync(_b,ct));}catch(OperationCanceledException){}catch(Exception ex){if(!ct.IsCancellationRequested)_status(ex.Message);}}
    private async Task LoadWaveAsync(Deck deck,CancellationToken ct)
    {
        if(deck.Row?.Track is not {} track)return;deck.Wave.IsLoading=true;
        try
        {
            var cues=_cues==null?[]:await _cues.GetForTrackAsync(track.Id,ct);ct.ThrowIfCancellationRequested();
            deck.Cues.Text="Traktor-Cues (unverändert): "+string.Join(" · ",cues.Where(c=>c.Source.StartsWith("imported:traktor")).Select(c=>$"{c.Label??c.Type} {c.PositionMs/1000.0:0.0}s"));
            deck.ImportedMarkers=cues.Where(c=>c.Source.StartsWith("imported:traktor")).Select(c=>new WaveformMarker(c.PositionMs/(double)Math.Max(1,track.DurationMs),"",0xFF8899AA)).ToArray();deck.UpdateMarkers();
            var wave=await _stripes.TryLoadAsync(track,ct)??await _cache.TryLoadAsync(track,ct);
            if(wave==null){wave=await _generator.GenerateAsync(await ResolveAsync(track,ct),ct);if(wave!=null)await _cache.SaveAsync(track,wave,ct);}
            ct.ThrowIfCancellationRequested();deck.Wave.Waveform=wave;
        }
        catch(OperationCanceledException){throw;}catch(Exception ex){if(!ct.IsCancellationRequested)deck.Cues.Text=ex.Message;}
        finally {if(!ct.IsCancellationRequested)deck.Wave.IsLoading=false;}
    }
    private async Task<string> ResolveAsync(Track track,CancellationToken ct){var root=(await _roots.GetAllAsync(ct)).FirstOrDefault(r=>r.Id==track.LibraryRootId)??throw new IOException("Bibliotheksordner fehlt.");return Cratebase.Server.LibraryServer.ContainedPath(root.Path,track.RelativePath);}
    private void MarkChanged()=>_details.Text="Marker geändert · Sekundenwerte mit „Marker übernehmen“ speichern. Gezogene Marker werden beim Loslassen gespeichert; Waveform-Klick spult die Wiedergabe.";
    private async Task ApplyMarkersAsync(){_a.Apply();_b.Apply();await SaveCurrentAsync();_status("Planungsmarker gespeichert. Keine Traktor-Datei verändert.");}
    private async Task SuggestMarkersAsync()
    {
        if(_a.Row?.Track is not {} track||_b.Row?.Track is not {} next)return;
        var beat=60000/(track.Bpm is >0?track.Bpm.Value:128);var end=Math.Min(track.DurationMs,Math.Max(1000,_a.Row.Entry.FadeOutMs));var duration=Math.Min(16*beat,Math.Min(120000,end));
        _a.Row.Entry.PlayMs=(long)Math.Max(0,end-duration);_a.Row.Entry.BassTradeMs=(long)Math.Max(_a.Row.Entry.PlayMs,end-4*beat);_a.Row.Entry.FadeOutMs=end;
        _b.Row.Entry.PlayMs=0;_b.Row.Entry.BassTradeMs=(long)Math.Min(next.DurationMs-1,12*60000/(next.Bpm is >0?next.Bpm.Value:128));
        await SaveCurrentAsync();await LoadDecksAsync();_status("16-Beat-Vorschlag: Basswechsel vier Beats vor Ende. Kein erkannter Beatgrid; bei Bedarf manuell korrigieren.");
    }
    private async Task PreviewAsync()
    {
        if(_a.Row?.Track is not {} a||_b.Row?.Track is not {} b)throw new InvalidOperationException("Einen Track mit Nachfolger auswählen.");
        await ApplyMarkersAsync();StopPreview();Directory.CreateDirectory(_directory);var path=Path.Combine(_directory,"transition-preview.wav");
        _status("Übergang wird mit Tempoanpassung und kurzem Basswechsel gerendert …");
        await new TransitionRenderer(_ffmpeg()).RenderAsync(await ResolveAsync(a,Token),await ResolveAsync(b,Token),a,b,_a.Row.Entry,_b.Row.Entry,path,Token);
        Token.ThrowIfCancellationRequested();_pauseLibrary();_preview=_audioFactory();_preview.Volume=.8f;_preview.Load(path);_preview.Play();
        _status("Übergangsvorschau · Basswechsel bei 180 Hz; Tempo B an A angepasst. Kein samplegenauer Live-DJ-Mixer.");
    }
    public void StopPreview(){StopCandidateAudio();var audio=_preview;_preview=null;_auditionDeck=null;_auditionSeek=null;_auditionPaused=false;audio?.Stop();audio?.Dispose();_a?.Transport(false,false,0);_b?.Transport(false,false,0);}
    private async Task PublishAsync()
    {
        if(_plan.Entries.Count==0)return;await SaveCurrentAsync();var node=await _playlists.CreateAsync(_plan.Name,PlaylistNodeType.Playlist,null);
        try {await _playlists.AddTracksAsync(node.Id,_plan.Entries.Select(e=>e.TrackId).Distinct().ToArray(),Token);}catch{await _playlists.DeleteAsync(node.Id);throw;}
        await _playlistChanged();_status("Set-Reihenfolge als normale Playlist gespeichert. Planungsmarker bleiben separat in Cratebase.");
    }
    private async Task ReferencesAsync()
    {
        if(TopLevel.GetTopLevel(this) is not Window owner)return;
        var window=new Window {Title="Set-Inspiration · Referenzen und Tracklisten",Width=760,Height=680,WindowStartupLocation=WindowStartupLocation.CenterOwner};
        var panel=new StackPanel {Margin=new Thickness(18),Spacing=10};
        panel.Children.Add(new TextBlock {Text="Öffentliche Set-Links als Quellen speichern. Tracklisten aus Beschreibung/Kommentaren einfügen. Die KI erhält nur eingefügten Text; Links allein werden nicht als angehörte Sets ausgegeben.",TextWrapping=TextWrapping.Wrap});
        var links=new TextBox {AcceptsReturn=true,Height=90,Text=string.Join("\n",_plan.References.Select(r=>r.Url)),PlaceholderText="https://www.youtube.com/… · ein Link pro Zeile"};panel.Children.Add(links);
        var tracklist=new TextBox {AcceptsReturn=true,Height=220,Text=string.Join("\n",_plan.References.Select(r=>r.Tracklist).Where(t=>!string.IsNullOrWhiteSpace(t))),PlaceholderText="Artist – Titel, Zeitstempel, beobachtete Genre-/Energieentwicklung"};panel.Children.Add(tracklist);
        var fetch=new Button {Content="Beschreibungen der eingetragenen Links abrufen"};
        fetch.Click+=async(_,_)=>await Run(async()=>
        {
            var fetched=new List<SetReference>();
            foreach(var link in (links.Text??"").Split('\n',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries).Take(5))fetched.AddRange(await new SetInspiration(Path.Combine(_directory,"research")).FindAsync(link,Token));
            tracklist.Text=string.Join("\n\n",fetched.Select(r=>r.Url+"\n"+r.Tracklist));_status($"{fetched.Count} öffentliche Beschreibungen abgerufen. Mit „Referenzen speichern“ übernehmen.");
        });panel.Children.Add(fetch);
        var search=new Button {Content="Online nach passenden DJ-Tracklisten suchen"};search.Click+=(_,_)=>{var query=Uri.EscapeDataString((_brief.Text??"club DJ set")+" tracklist DJ set");System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://www.google.com/search?q="+query){UseShellExecute=true});};panel.Children.Add(search);
        var save=new Button {Content="Referenzen speichern"};save.Click+=async(_,_)=>
        {
            var urls=(links.Text??"").Split('\n',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries);
            if(urls.Any(url=>!Uri.TryCreate(url,UriKind.Absolute,out var uri)||uri.Scheme is not ("https" or "http"))){_status("Bitte gültige HTTP-/HTTPS-Links eintragen.");return;}
            _plan.References=urls.Select((url,i)=>new SetReference(url,i==0?tracklist.Text??"":"")).ToList();if(urls.Length==0&&!string.IsNullOrWhiteSpace(tracklist.Text))_plan.References.Add(new("",tracklist.Text));await SaveCurrentAsync();window.Close();
        };panel.Children.Add(save);window.Content=new ScrollViewer {Content=panel};await window.ShowDialog(owner);
    }
    public void Dispose(){_candidateLifetime.Cancel();_candidateWindow?.Close();_job?.Cancel();_wave?.Cancel();_browserPane?.Dispose();StopPreview();_ai.Dispose();}
    private sealed record PlanRow(int Number,SetPlanEntry Entry,Track? Track,string Assessment)
    {
        public string Artist=>Track?.DisplayArtist??"Gelöschter Track";public string Title=>Track?.DisplayTitle??$"ID {Entry.TrackId}";public string? Genre=>Track?.GenreRaw;public double? Bpm=>Track?.Bpm;public string? Key=>Track?.MusicalKey;
    }
    private sealed class Deck
    {
        public Border View {get;}
        public WaveformView Wave {get;}=new() {Height=55};
        public TextBlock Cues {get;}=new() {TextWrapping=TextWrapping.Wrap,FontSize=10,MaxHeight=28};
        private readonly TextBlock _title=new() {FontSize=14,TextTrimming=TextTrimming.CharacterEllipsis};
        private readonly Button _play;
        private readonly TextBlock _time=new() {FontSize=11,VerticalAlignment=VerticalAlignment.Center};
        private readonly NumericUpDown[] _markers=Enumerable.Range(0,3).Select(_=>new NumericUpDown {Minimum=0,Maximum=86400,Increment=.1m,FormatString="0.000",Width=110,ShowButtonSpinner=false}).ToArray();
        public PlanRow? Row {get;private set;}
        public IReadOnlyList<WaveformMarker> ImportedMarkers {get;set;}=[];
        public void UpdateMarkers()
        {
            var duration=(Row?.Track?.DurationMs??0)/1000.0;if(duration<=0){Wave.Markers=[];return;}
            var labels=new[]{"Start","Bass","Out"};var ids=new[]{"play","bass","out"};var colors=new uint[]{0xFF33E78C,0xFFFFB84D,0xFFFF657A};
            Wave.Markers=ImportedMarkers.Concat(_markers.Select((m,i)=>new WaveformMarker((double)(m.Value??0)/duration,labels[i],colors[i],true,ids[i]))).ToArray();
        }
        public Deck(string label,Action changed,Func<Deck,Task> play,Func<Deck,Task> restart,Action<Deck> stop,Action<Deck,double> seek,Func<Deck,Task> saveMarkers)
        {
            var panel=new StackPanel {Spacing=4};panel.Children.Add(new TextBlock {Text=label,Foreground=Brushes.LightGreen,FontSize=11});panel.Children.Add(_title);panel.Children.Add(Wave);
            var controls=new StackPanel {Orientation=Orientation.Horizontal,Spacing=5};
            _play=new Button {Name=label.StartsWith("A")?"SetDeckAPlay":"SetDeckBPlay",Content="▶ Vorhören",Padding=new Thickness(6,3)};_play.Click+=async(_,_)=>await play(this);controls.Children.Add(_play);
            var beginning=new Button {Content="↶ Start",Padding=new Thickness(6,3)};beginning.Click+=async(_,_)=>await restart(this);controls.Children.Add(beginning);
            var halt=new Button {Content="■",Padding=new Thickness(6,3)};halt.Click+=(_,_)=>stop(this);controls.Children.Add(halt);controls.Children.Add(_time);panel.Children.Add(controls);
            var markers=new WrapPanel();var names=new[]{"Transition-Start (s)","Basswechsel (s)","Fade-out (s)"};
            for(var i=0;i<3;i++){var col=new StackPanel {Margin=new Thickness(0,0,8,0)};col.Children.Add(new TextBlock {Text=names[i],FontSize=10});col.Children.Add(_markers[i]);markers.Children.Add(col);_markers[i].ValueChanged+=(_,_)=>{UpdateMarkers();changed();};}
            panel.Children.Add(markers);panel.Children.Add(Cues);Wave.SeekCommand=new RelayCommand<double>(progress=>seek(this,progress));
            Cues.Bind(ToolTip.TipProperty,new Binding(nameof(TextBlock.Text)){Source=Cues});
            Wave.MarkerDragged+=(id,progress)=>
            {
                if(Row?.Track is not {DurationMs:>0} track)return;
                var index=id switch {"play"=>0,"bass"=>1,_=>2};var seconds=(decimal)(progress*track.DurationMs/1000);
                var low=index==0?0:(_markers[index-1].Value??0)+(index==2?.001m:0);
                var high=index==2?track.DurationMs/1000m:(_markers[index+1].Value??0)-(index==1?.001m:0);
                _markers[index].Value=Math.Clamp(seconds,low,Math.Max(low,high));
            };
            Wave.MarkerDragCompleted+=async(_,_)=>await saveMarkers(this);
            View=new Border {Child=panel,Padding=new Thickness(12),Background=Brush.Parse("#11151B")};
        }
        public void Set(PlanRow? row){Row=row;_title.Text=row==null?"Kein Track":row.Artist+" · "+row.Title;Wave.Waveform=null;Wave.IsLoading=false;Cues.Text="";ImportedMarkers=[];_markers[0].Value=(row?.Entry.PlayMs??0)/1000m;_markers[1].Value=(row?.Entry.BassTradeMs??0)/1000m;_markers[2].Value=(row?.Entry.FadeOutMs??0)/1000m;UpdateMarkers();Transport(false,false,0);}
        public void Transport(bool active,bool paused,double seconds){if(!active)Wave.Progress=0;_play.IsEnabled=Row?.Track!=null;_play.Content=active?(paused?"▶ Fortsetzen":"⏸ Pause"):"▶ Vorhören";_time.Text=$"{TimeSpan.FromSeconds(Math.Max(0,seconds)):mm\\:ss} / {TimeSpan.FromMilliseconds(Row?.Track?.DurationMs??0):mm\\:ss}";}
        public void Apply(){if(Row?.Track is not {} track)return;var entry=new SetPlanEntry {TrackId=track.Id,PlayMs=(long)((_markers[0].Value??0)*1000),BassTradeMs=(long)((_markers[1].Value??0)*1000),FadeOutMs=(long)((_markers[2].Value??0)*1000)};entry.Validate(track);Row.Entry.PlayMs=entry.PlayMs;Row.Entry.BassTradeMs=entry.BassTradeMs;Row.Entry.FadeOutMs=entry.FadeOutMs;}
    }
}
