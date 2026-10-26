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
using Avalonia.VisualTree;
using Cratebase.App.Desktop.ViewModels;
using Cratebase.Core.Abstractions;
using Cratebase.Core.Models;

namespace Cratebase.App.Desktop.Views;

public sealed partial class SetPlanningView
{
    private readonly ILibraryQuery? _browserQuery;
    private readonly Func<LibraryTarget,CancellationToken,Task>? _prepareBrowserSource;
    private readonly Func<string,CancellationToken,Task>? _indexFolder;
    private TrackPaneViewModel? _browserPane;
    private Control? _browserHost;
    private DataGrid? _browserTracks;
    private readonly ObservableCollection<PlannerBrowserNode> _browserSources=[];
    private static readonly DataFormat<PlanningDrag> PlanningDragFormat=DataFormat.CreateInProcessFormat<PlanningDrag>("application/x-cratebase-set-planning");
    private sealed record PlanningDrag(Track[] Tracks,SetPlanEntry[] Entries,SetPlanningView? Owner);
    private sealed class PlannerBrowserNode(string name,LibraryTarget? target=null)
    {
        public string Name {get;}=name;
        public LibraryTarget? Target {get;}=target;
        public bool Loaded {get;set;}
        public ObservableCollection<PlannerBrowserNode> Children {get;}=[];
    }

    private Control BuildPlanningBrowser(ILibraryQuery query)
    {
        var pane=_browserPane=new TrackPaneViewModel(query) {ReportError=_status};
        var panel=new Grid {RowDefinitions=new("Auto,160,5,Auto,Auto,*,Auto"),RowSpacing=5,Margin=new Thickness(8)};
        var tools=new WrapPanel();tools.Children.Add(Work("Quellen aktualisieren",ReloadPlanningBrowserAsync));tools.Children.Add(Work("Ordner wählen …",ChoosePlanningFolderAsync));panel.Children.Add(tools);
        if(_indexFolder!=null)tools.Children.Add(Work("Musikordner indexieren …",IndexPlanningFolderAsync));
        var tree=new TreeView {Name="SetPlanningSources",ItemsSource=_browserSources};
        tree.ItemTemplate=new FuncTreeDataTemplate<PlannerBrowserNode>((node,_)=>new TextBlock {Text=node?.Name,TextTrimming=TextTrimming.CharacterEllipsis},node=>node.Children);
        tree.SelectionChanged+=async(_,_)=>{if(tree.SelectedItem is PlannerBrowserNode {Target:{} target})await Run(()=>OpenPlanningSourceAsync(target));};
        tree.AddHandler(TreeViewItem.ExpandedEvent,async(_,e)=>
        {
            if(e.Source is not TreeViewItem {DataContext:PlannerBrowserNode {Target:{Kind:LibraryTargetKind.Directory}} node}||node.Loaded)return;
            try
            {
                var folders=await Task.Run(()=>Directory.GetDirectories(node.Target!.Path!).Order(StringComparer.OrdinalIgnoreCase).ToArray());
                node.Children.Clear();foreach(var folder in folders)node.Children.Add(PlanningFolder(folder));node.Loaded=true;
            }
            catch(Exception ex){_status("Ordner konnte nicht geöffnet werden: "+ex.Message);}
        });
        Grid.SetRow(tree,1);panel.Children.Add(tree);
        var splitter=new GridSplitter {ResizeDirection=GridResizeDirection.Rows,HorizontalAlignment=HorizontalAlignment.Stretch,Background=Brush.Parse("#29313E")};Grid.SetRow(splitter,2);panel.Children.Add(splitter);
        var title=new TextBlock {FontWeight=FontWeight.SemiBold,TextTrimming=TextTrimming.CharacterEllipsis};title.Bind(TextBlock.TextProperty,new Binding(nameof(pane.Title)){Source=pane});Grid.SetRow(title,3);panel.Children.Add(title);
        var search=new TextBox {Name="SetPlanningSearch",PlaceholderText="In dieser Quelle suchen …"};search.Bind(TextBox.TextProperty,new Binding(nameof(pane.Search)){Source=pane,Mode=BindingMode.TwoWay});Grid.SetRow(search,4);panel.Children.Add(search);
        var tracks=_browserTracks=new DataGrid {Name="SetPlanningBrowserTracks",ItemsSource=pane.Tracks,AutoGenerateColumns=false,IsReadOnly=true,SelectionMode=DataGridSelectionMode.Extended,CanUserSortColumns=false,RowHeight=26,ColumnHeaderHeight=28};
        foreach(var column in new[]{("Artist","DisplayArtist",120d),("Titel","DisplayTitle",210d),("BPM","Bpm",65d),("Key","MusicalKey",60d),("Genre","GenreRaw",120d)})
            tracks.Columns.Add(new DataGridTextColumn {Header=column.Item1,Binding=new Binding(column.Item2),Width=new DataGridLength(column.Item3)});
        tracks.DoubleTapped+=async(_,_)=>{if(tracks.SelectedItem is Track track)await Run(()=>InsertBrowserTracksAsync([track],_plan.Entries.Count));};
        WirePlanningDrag(tracks,()=>new(tracks.SelectedItems.OfType<Track>().ToArray(),[],null));Grid.SetRow(tracks,5);panel.Children.Add(tracks);
        var footer=new StackPanel {Spacing=4};var summary=new TextBlock {FontSize=10,TextWrapping=TextWrapping.Wrap};summary.Bind(TextBlock.TextProperty,new Binding(nameof(pane.Summary)){Source=pane});footer.Children.Add(summary);
        footer.Children.Add(Work("Weitere Tracks laden",()=>pane.ReloadAsync(false)));
        footer.Children.Add(Work("Auswahl anhängen",AppendSelectionAsync));
        footer.Children.Add(new TextBlock {Text="Mehrfachauswahl → ins Set ziehen. Doppelklick hängt an. Im Set per Drag & Drop umsortieren.",FontSize=10,TextWrapping=TextWrapping.Wrap,Foreground=Brushes.LightGray});Grid.SetRow(footer,6);panel.Children.Add(footer);
        return panel;
    }

    private static PlannerBrowserNode PlanningFolder(string path)
    {
        var name=Path.GetFileName(Path.TrimEndingDirectorySeparator(path));if(string.IsNullOrEmpty(name))name=path;
        var node=new PlannerBrowserNode(name,new(LibraryTargetKind.Directory,name,Path:path));node.Children.Add(new("Laden …"));return node;
    }
    private async Task ReloadPlanningBrowserAsync()
    {
        var nodes=await _playlists.GetAllNodesAsync(Token);var clubIds=(await _playlists.GetClubNodeIdsAsync(Token)).ToHashSet();
        var map=nodes.ToDictionary(n=>n.Id,n=>new PlannerBrowserNode(n.Name,new(n.NodeType==PlaylistNodeType.Folder?LibraryTargetKind.PlaylistFolder:LibraryTargetKind.Playlist,n.Name,n.Id)));
        _browserSources.Clear();
        _browserSources.Add(new("Gesamte Collection",new(LibraryTargetKind.Collection,"Gesamte Collection")));
        _browserSources.Add(new("Traktor Collection",new(LibraryTargetKind.TraktorCollection,"Traktor Collection")));
        var club=new PlannerBrowserNode("Club Collection",new(LibraryTargetKind.ClubCollection,"Club Collection"));club.Children.Add(new("Nicht einsortiert",new(LibraryTargetKind.ClubUnsorted,"Nicht einsortiert")));_browserSources.Add(club);
        foreach(var node in nodes)
        {
            var entry=map[node.Id];
            if(clubIds.Contains(node.Id)&&node.ParentId==null){foreach(var child in nodes.Where(n=>n.ParentId==node.Id))club.Children.Add(map[child.Id]);continue;}
            if(node.ParentId is {} parent&&map.TryGetValue(parent,out var group))group.Children.Add(entry);
            else _browserSources.Add(entry);
        }
        foreach(var smart in JsonSerializer.Deserialize<List<SmartPlaylist>>(await _state.ReadAsync(SmartPlaylist.StateKey,Token)??"[]")??[])_browserSources.Add(new("◇ "+smart.Name,smart.Target));
        var folders=new PlannerBrowserNode("Ordner & Laufwerke");
        var drives=await Task.Run(()=>DriveInfo.GetDrives().Where(d=>d.IsReady).Select(d=>d.RootDirectory.FullName).ToArray(),Token);
        foreach(var path in (await _roots.GetAllAsync(Token)).Select(r=>r.Path).Concat(drives).Distinct(StringComparer.OrdinalIgnoreCase))folders.Children.Add(PlanningFolder(path));
        _browserSources.Add(folders);
    }
    private async Task OpenPlanningSourceAsync(LibraryTarget target)
    {
        if(_prepareBrowserSource!=null)await _prepareBrowserSource(target,Token);
        await _browserPane!.OpenAsync(target);
    }
    private async Task ChoosePlanningFolderAsync()
    {
        var storage=TopLevel.GetTopLevel(this)?.StorageProvider;if(storage==null)return;
        var folders=await storage.OpenFolderPickerAsync(new(){Title="Musikordner im Set-Planer öffnen",AllowMultiple=false});
        if(folders.FirstOrDefault()?.TryGetLocalPath() is not {} path)return;
        _browserSources.Add(PlanningFolder(path));await OpenPlanningSourceAsync(new(LibraryTargetKind.Directory,Path.GetFileName(path),Path:path));
    }
    private async Task IndexPlanningFolderAsync()
    {
        var storage=TopLevel.GetTopLevel(this)?.StorageProvider;if(storage==null||_indexFolder==null)return;
        var selected=await storage.OpenFolderPickerAsync(new(){Title="Musikordner einschließlich Unterordner in die Datenbank einlesen",AllowMultiple=false});
        if(selected.FirstOrDefault()?.TryGetLocalPath() is not {} path)return;
        await _indexFolder(path,Token);await ReloadPlanningBrowserAsync();await _browserPane!.ReloadAsync();
        _status("Musikordner indexiert. Die Set-Suche verwendet die gespeicherten Metadaten ohne erneuten Audioscan.");
    }
    private async Task<IReadOnlyList<Track>> ReadPlanningBrowserCandidatesAsync(int maximum,CancellationToken ct)
    {
        var pane=_browserPane!;var request=new TrackQuery(pane.Source,pane.Search,pane.Sort,pane.Descending,Filters:pane.Options.Filters.ToArray(),SearchField:pane.SearchField);
        return await Task.Run(async()=>
        {
            var result=new List<Track>();
            while(result.Count<maximum)
            {
                ct.ThrowIfCancellationRequested();var count=Math.Min(2000,maximum-result.Count);
                var page=await _browserQuery!.QueryAsync(request with {Limit=count,Offset=result.Count},ct);result.AddRange(page);if(page.Count<count)break;
            }
            return (IReadOnlyList<Track>)result;
        },ct);
    }
    private async Task InsertBrowserTracksAsync(IReadOnlyList<Track> tracks,int index)
    {
        var ids=_plan.Entries.Select(e=>e.TrackId).ToHashSet();var added=tracks.Where(t=>ids.Add(t.Id)).Select(SetPlanEntry.For).ToArray();
        if(added.Length==0){_status("Keine neuen Tracks ausgewählt; bereits enthaltene Tracks werden nicht doppelt hinzugefügt.");return;}
        _plan.Entries.InsertRange(Math.Clamp(index,0,_plan.Entries.Count),added);await SaveCurrentAsync();await LoadRowsAsync();
        _rebuilding=true;_table.SelectedIndex=Math.Clamp(index,0,_rows.Count-1);_rebuilding=false;await LoadDecksAsync();_status($"{added.Length} Tracks ins Set eingefügt.");
    }
    private void WirePlanningDrag(DataGrid table,Func<PlanningDrag> payload)
    {
        PointerPressedEventArgs? press=null;Point start=default;
        table.AddHandler(PointerPressedEvent,(_,e)=>
        {
            var hit=table.InputHitTest(e.GetPosition(table)) as Control;
            if(hit is not DataGridRow&&hit?.GetVisualAncestors().OfType<DataGridRow>().Any()!=true){press=null;return;}
            if(e.GetCurrentPoint(table).Properties.IsLeftButtonPressed){press=e;start=e.GetPosition(table);}
        },RoutingStrategies.Tunnel);
        table.PointerReleased+=(_,_)=>press=null;
        table.PointerMoved+=async(_,e)=>
        {
            if(press==null||Busy||!e.GetCurrentPoint(table).Properties.IsLeftButtonPressed)return;
            var point=e.GetPosition(table);if(Math.Abs(point.X-start.X)+Math.Abs(point.Y-start.Y)<8)return;
            var gesture=press;press=null;var data=payload();if(data.Tracks.Length==0&&data.Entries.Length==0)return;
            var transfer=new DataTransfer();transfer.Add(DataTransferItem.Create(PlanningDragFormat,data));await DragDrop.DoDragDropAsync(gesture,transfer,data.Owner==this?DragDropEffects.Move:DragDropEffects.Copy);
        };
    }
    private void WirePlanDrop()
    {
        WirePlanningDrag(_table,()=>new([],(_table.SelectedItem is PlanRow row)?[row.Entry]:[],this));
        DragDrop.SetAllowDrop(_table,true);
        _table.AddHandler(DragDrop.DragOverEvent,(_,e)=>{var data=e.DataTransfer.TryGetValue(PlanningDragFormat);e.DragEffects=!Busy&&!_libraryLocked()&&data!=null?(data.Owner==this?DragDropEffects.Move:DragDropEffects.Copy):DragDropEffects.None;e.Handled=true;});
        _table.AddHandler(DragDrop.DropEvent,async(_,e)=>
        {
            e.Handled=true;var data=e.DataTransfer.TryGetValue(PlanningDragFormat);if(data==null)return;
            var index=_plan.Entries.Count;
            var hit=_table.InputHitTest(e.GetPosition(_table)) as Control;
            var target=hit as DataGridRow??hit?.GetVisualAncestors().OfType<DataGridRow>().FirstOrDefault();
            if(target?.DataContext is PlanRow row)index=_plan.Entries.IndexOf(row.Entry)+(e.GetPosition(target).Y>target.Bounds.Height/2?1:0);
            await Run(()=>ApplyPlanningDropAsync(data,index));
        });
    }
    private async Task ApplyPlanningDropAsync(PlanningDrag drag,int index)
    {
        if(drag.Owner!=this){await InsertBrowserTracksAsync(drag.Tracks,index);return;}
        var entries=drag.Entries.Where(_plan.Entries.Contains).ToArray();if(entries.Length==0)return;
        var before=_plan.Entries.Take(index).Count(entries.Contains);foreach(var entry in entries)_plan.Entries.Remove(entry);
        index=Math.Clamp(index-before,0,_plan.Entries.Count);_plan.Entries.InsertRange(index,entries);await SaveCurrentAsync();await LoadRowsAsync();
        _rebuilding=true;_table.SelectedIndex=index;_rebuilding=false;await LoadDecksAsync();_status("Set-Reihenfolge aktualisiert; Marker bleiben erhalten.");
    }
}
