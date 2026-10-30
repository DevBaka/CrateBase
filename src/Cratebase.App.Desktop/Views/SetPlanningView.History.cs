using System.Collections.ObjectModel;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Platform.Storage;
using Cratebase.Core.Models;
using Cratebase.Import;
namespace Cratebase.App.Desktop.Views;

public sealed partial class SetPlanningView
{
    private sealed class HistoryChoice(HistorySession session)
    {
        public bool Selected {get;set;}
        public HistorySession Session {get;}=session;
        public string Name=>Session.Name;
        public string Folder=>Path.GetDirectoryName(Session.Path)??"";
    }
    private sealed class HistoryPreview(HistoryMergeRow row)
    {
        public bool Include {get;set;}=true;
        public HistoryMergeRow Row {get;}=row;
        public string Session=>Row.Source.Session;
        public string Artist=>Row.Source.Track.DisplayArtist;
        public string Title=>Row.Source.Track.DisplayTitle;
        public double? Bpm=>Row.Source.Track.Bpm;
        public string Review=>Row.Review;
    }
    private async Task HistoriesAsync()
    {
        if(TopLevel.GetTopLevel(this) is not Window owner)return;
        var folders=HistorySessionCatalog.DefaultFolders().ToList();
        if(await _state.ReadAsync("history-folders-v1",Token) is {} stored)folders.AddRange(JsonSerializer.Deserialize<List<string>>(stored)??[]);
        var choices=new ObservableCollection<HistoryChoice>();var preview=new ObservableCollection<HistoryPreview>();
        var window=new Window {Title="Session-Histories · ansehen und Sets zusammenführen",Width=1150,Height=780,MinWidth=760,MinHeight=500,WindowStartupLocation=WindowStartupLocation.CenterOwner};
        var layout=new Grid {RowDefinitions=new("Auto,*,Auto,2*,Auto"),RowSpacing=8,Margin=new Thickness(12)};
        var tools=new WrapPanel();layout.Children.Add(tools);var actions=new WrapPanel();Grid.SetRow(actions,2);layout.Children.Add(actions);
        var status=new TextBlock {TextWrapping=Avalonia.Media.TextWrapping.Wrap};Grid.SetRow(status,4);layout.Children.Add(status);
        var sessions=new DataGrid {ItemsSource=choices,AutoGenerateColumns=false,CanUserResizeColumns=true,SelectionMode=DataGridSelectionMode.Single};
        sessions.Columns.Add(new DataGridCheckBoxColumn {Header="Wählen",Binding=new Binding("Selected"){Mode=BindingMode.TwoWay},Width=new DataGridLength(65)});
        foreach(var c in new[]{("Session","Name",350d),("History-Ordner","Folder",650d)})sessions.Columns.Add(new DataGridTextColumn {Header=c.Item1,Binding=new Binding(c.Item2),IsReadOnly=true,Width=new DataGridLength(c.Item3)});
        Grid.SetRow(sessions,1);layout.Children.Add(sessions);
        var match=new CheckBox {Content="Session-Grenzen passend anordnen",IsChecked=true};var unique=new CheckBox {Content="Doppelte Dateien entfernen",IsChecked=true};actions.Children.Add(match);actions.Children.Add(unique);
        var table=new DataGrid {ItemsSource=preview,AutoGenerateColumns=false,CanUserResizeColumns=true};
        table.Columns.Add(new DataGridCheckBoxColumn {Header="Behalten",Binding=new Binding("Include"){Mode=BindingMode.TwoWay},Width=new DataGridLength(75)});
        foreach(var c in new[]{("Session","Session",200d),("Artist","Artist",150d),("Titel","Title",270d),("BPM","Bpm",65d),("Prüfen / vorhören","Review",480d)})table.Columns.Add(new DataGridTextColumn {Header=c.Item1,Binding=new Binding(c.Item2),IsReadOnly=true,Width=new DataGridLength(c.Item3)});
        Grid.SetRow(table,3);layout.Children.Add(table);window.Content=layout;
        var busy=false;
        async Task Execute(Func<Task> action)
        {if(busy)return;busy=true;tools.IsEnabled=false;actions.IsEnabled=false;try{await action();}catch(Exception ex){status.Text=ex.Message;}finally{busy=false;tools.IsEnabled=true;actions.IsEnabled=true;}}
        async Task Reload()
        {var found=await Task.Run(()=>HistorySessionCatalog.Discover(folders));choices.Clear();preview.Clear();foreach(var s in found)choices.Add(new(s));status.Text=$"{found.Count} Histories · Originaldateien bleiben unverändert.";}
        Button Button(string text,Func<Task> action)
        {var b=new Button {Content=text,Margin=new Thickness(3)};b.Click+=async(_,_)=>await Execute(action);return b;}
        tools.Children.Add(Button("Aktualisieren",Reload));
        tools.Children.Add(Button("History-Ordner hinzufügen …",async()=>
        {
            var selected=await window.StorageProvider.OpenFolderPickerAsync(new(){Title="Traktor History-Ordner",AllowMultiple=true});
            foreach(var folder in selected)if(folder.TryGetLocalPath() is {} path)folders.Add(path);
            await _state.WriteAsync("history-folders-v1",JsonSerializer.Serialize(folders.Distinct(StringComparer.OrdinalIgnoreCase)),Token);await Reload();
        }));
        tools.Children.Add(Button("Alle auswählen",()=>{foreach(var row in choices)row.Selected=true;sessions.ItemsSource=null;sessions.ItemsSource=choices;return Task.CompletedTask;}));
        tools.Children.Add(Button("Session ansehen",async()=>
        {
            if(sessions.SelectedItem is not HistoryChoice choice)return;var entries=await Task.Run(()=>HistorySessionCatalog.Read(choice.Session));preview.Clear();
            foreach(var row in HistorySessionCatalog.Merge([entries],false,false,Options()))preview.Add(new(row));status.Text=$"{entries.Count} Einträge · Reihenfolge und Wiederholungen bleiben erhalten.";
        }));
        actions.Children.Add(Button("Auswahl zusammenführen",async()=>
        {
            var selected=choices.Where(c=>c.Selected).Select(c=>c.Session).OrderBy(s=>s.ModifiedUtc).ToArray();if(selected.Length==0){status.Text="Bitte Histories über die Checkboxen auswählen.";return;}
            var matching=match.IsChecked==true;var dedup=unique.IsChecked==true;var options=Options();
            var result=await Task.Run(()=>HistorySessionCatalog.Merge(selected.Select(HistorySessionCatalog.Read).ToArray(),matching,dedup,options));
            preview.Clear();foreach(var row in result)preview.Add(new(row));status.Text=$"{selected.Length} Sessions · {result.Count} Tracks · {result.Count(r=>r.Review.Length>0)} Prüfhinweise. Grenzbewertung anhand Metadaten; Reihenfolge innerhalb der Sessions bleibt erhalten.";
        }));
        actions.Children.Add(Button("Behaltene Tracks im Set-Planer öffnen",async()=>
        {
            var selected=preview.Where(p=>p.Include).ToArray();if(selected.Length==0)return;if(_libraryLocked())throw new InvalidOperationException("Bibliothek ist gerade gesperrt.");
            await SaveCurrentAsync();var plan=new SetPlan {Name="History-Set "+DateTime.Now.ToString("yyyy-MM-dd HH:mm")};var roots=await _roots.GetAllAsync(Token);
            foreach(var row in selected)
            {
                var path=row.Row.Source.FullPath;var root=roots.OrderByDescending(r=>r.Path.Length).FirstOrDefault(r=>path.StartsWith(r.Path.TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))??await _roots.AddOrGetAsync(Path.GetDirectoryName(path)!,"History-Import",Token);
                var relative=Path.GetRelativePath(root.Path,path).Replace('\\','/');var track=await _tracks.FindByPathAsync(root.Id,relative,Token);
                if(track==null){track=row.Row.Source.Track;track.LibraryRootId=root.Id;track.RelativePath=relative;if(!File.Exists(path))track.MissingSinceUtc=DateTime.UtcNow;track.Id=await _tracks.UpsertAsync(track,Token);}
                var entry=SetPlanEntry.For(track);entry.Note="History: "+row.Session+(row.Review.Length>0?" · Prüfen: "+row.Review:"");plan.Entries.Add(entry);
            }
            _plan=plan;RefreshPicker();await SaveCurrentAsync();RefreshPicker();await LoadRowsAsync();window.Close();_status("History-Set geöffnet. Markierte Tracks und Übergänge vorhören; danach als Playlist speichern.");
        }));
        window.Opened+=async(_,_)=>await Execute(Reload);await window.ShowDialog(owner);
    }
}
