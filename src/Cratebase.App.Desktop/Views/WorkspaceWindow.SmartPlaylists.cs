using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Cratebase.Core.Models;

namespace Cratebase.App.Desktop.Views;

public sealed partial class WorkspaceWindow
{
    private async Task<List<SmartPlaylist>> ReadSmartPlaylists() =>
        JsonSerializer.Deserialize<List<SmartPlaylist>>(await _state.ReadAsync(SmartPlaylist.StateKey) ?? "[]") ?? [];

    private async Task EditSmartPlaylist(string? id = null)
    {
        if(_syncActive)throw new InvalidOperationException("Während des Traktor-Abgleichs sind Playlist-Änderungen gesperrt.");
        var all = await ReadSmartPlaylists();
        var existing = all.FirstOrDefault(p => p.Id == id);
        var pane = _panes[_active];
        var initial = existing ?? new SmartPlaylist(Guid.NewGuid().ToString(), "Neue Smart-Playlist",
            pane.Source.Kind == LibraryTargetKind.SmartPlaylist ? new(LibraryTargetKind.Collection,"Collection") : pane.Source,
            pane.Options.Filters.ToArray(), Sort: pane.Sort, Descending: pane.Descending);
        var dialog = Dialog("Smarte Playlist · dynamische Regeln", 900, 650);
        var layout = new Grid { RowDefinitions = new("Auto,Auto,Auto,*,Auto,Auto"), Margin = new Thickness(18), RowSpacing=10 };
        var name = new TextBox { Text=initial.Name,PlaceholderText="Name" }; layout.Children.Add(name);
        var sources = new List<LibraryTarget> { new(LibraryTargetKind.Collection,"Gesamte Collection"),new(LibraryTargetKind.ClubCollection,"Club Collection"),new(LibraryTargetKind.TraktorCollection,"Traktor Collection") };
        sources.AddRange((await _playlists.GetAllNodesAsync()).Select(n=>new LibraryTarget(n.NodeType==PlaylistNodeType.Playlist?LibraryTargetKind.Playlist:LibraryTargetKind.PlaylistFolder,n.Name,n.Id)));
        if(!sources.Contains(initial.Source))sources.Add(initial.Source);
        var source = new ComboBox {ItemsSource=sources,SelectedItem=initial.Source,HorizontalAlignment=HorizontalAlignment.Stretch,
            ItemTemplate=new Avalonia.Controls.Templates.FuncDataTemplate<LibraryTarget>((t,_)=>new TextBlock {Text=t?.Name})};
        Grid.SetRow(source,1);layout.Children.Add(source);
        var any = new CheckBox {Content="Mindestens eine Regel trifft zu (ODER); sonst müssen alle zutreffen (UND)",IsChecked=initial.MatchAny};
        Grid.SetRow(any,2);layout.Children.Add(any);
        var rules=new StackPanel {Spacing=8};var readers=new List<Func<MetadataFilter>>();
        void Add(MetadataFilter rule)
        {
            var row=new Grid {ColumnDefinitions=new("205,220,*,Auto"),ColumnSpacing=8};
            var field=new ComboBox {ItemsSource=TrackMetadata.Fields,SelectedItem=TrackMetadata.Get(rule.Field)};
            var op=new ComboBox();var value=new TextBox {Text=rule.Value,PlaceholderText="Wert; Tags: Ja / Nein"};
            void Fill(MetadataOperator preferred) {var choices=TrackMetadata.Operators(((MetadataField)field.SelectedItem!).Kind).Select(o=>new OperatorChoice(o)).ToArray();op.ItemsSource=choices;op.SelectedItem=choices.FirstOrDefault(o=>o.Value==preferred)??choices[0];}
            Fill(rule.Operator);field.SelectionChanged+=(_,_)=>Fill(MetadataOperator.Equals);
            void Visibility()=>value.IsVisible=(op.SelectedItem as OperatorChoice)?.Value is not (MetadataOperator.IsEmpty or MetadataOperator.IsNotEmpty);
            op.SelectionChanged+=(_,_)=>Visibility();Visibility();
            Func<MetadataFilter> read=()=>new(((MetadataField)field.SelectedItem!).Key,((OperatorChoice)op.SelectedItem!).Value,value.Text??"");readers.Add(read);
            row.Children.Add(field);Grid.SetColumn(op,1);row.Children.Add(op);Grid.SetColumn(value,2);row.Children.Add(value);
            var remove=Button("×",()=>{readers.Remove(read);rules.Children.Remove(row);return Task.CompletedTask;});Grid.SetColumn(remove,3);row.Children.Add(remove);rules.Children.Add(row);
        }
        foreach(var rule in initial.Rules)Add(rule);
        var scroll=new ScrollViewer {Content=rules};Grid.SetRow(scroll,3);layout.Children.Add(scroll);
        var error=new TextBlock {Foreground=Brushes.Orange,TextWrapping=TextWrapping.Wrap};Grid.SetRow(error,4);layout.Children.Add(error);
        var buttons=Row();Grid.SetRow(buttons,5);layout.Children.Add(buttons);
        buttons.Children.Add(Button("+ Regel",()=>{Add(new("genre",MetadataOperator.Contains));return Task.CompletedTask;}));
        buttons.Children.Add(Button("Vorschau",async ()=>
        {
            try {var definition=Read();var totals=await _query.TotalsAsync(new(definition.Source,SmartRules:definition.Rules,SmartMatchAny:definition.MatchAny));error.Text=$"{totals.Count:N0} passende Tracks · {TimeSpan.FromMilliseconds(totals.DurationMs).TotalHours:N1} Stunden";}
            catch(Exception ex){error.Text=ex.Message;}
        }));
        SmartPlaylist Read() {var definition=initial with {Name=(name.Text??"").Trim(),Source=(LibraryTarget)source.SelectedItem!,Rules=readers.Select(r=>r()).ToArray(),MatchAny=any.IsChecked==true};definition.Validate();return definition;}
        buttons.Children.Add(Button("Speichern",async ()=>
        {
            try {var definition=Read();all.RemoveAll(p=>p.Id==definition.Id);all.Add(definition);await _state.WriteAsync(SmartPlaylist.StateKey,JsonSerializer.Serialize(all));await ReloadBrowsers();await Open(definition.Target,_active);dialog.Close();}
            catch(Exception ex){error.Text=ex.Message;}
        }));
        buttons.Children.Add(Button("Abbrechen",()=>{dialog.Close();return Task.CompletedTask;}));dialog.Content=layout;await dialog.ShowDialog(this);
    }

    private async Task DeleteSmartPlaylist(LibraryTarget target)
    {
        if(_syncActive)throw new InvalidOperationException("Während des Traktor-Abgleichs sind Playlist-Änderungen gesperrt.");
        if(await Choose("Smarte Playlist löschen?","Nur die Regeln werden entfernt. Tracks und normale Playlisten bleiben erhalten.",["Löschen","Abbrechen"])!=0)return;
        var all=await ReadSmartPlaylists();all.RemoveAll(p=>p.Id==target.Path);await _state.WriteAsync(SmartPlaylist.StateKey,JsonSerializer.Serialize(all));
        foreach(var pane in _panes.Where(p=>p.Source.Kind==LibraryTargetKind.SmartPlaylist&&p.Source.Path==target.Path))await pane.OpenAsync(new(LibraryTargetKind.Collection,"Collection"));await ReloadBrowsers();
    }

    private async Task MaterializeSmartPlaylist(LibraryTarget target)
    {
        if(_syncActive)throw new InvalidOperationException("Während des Traktor-Abgleichs sind Playlist-Änderungen gesperrt.");
        var name=await Prompt("Als normale Playlist speichern",target.Name+" · Snapshot");if(string.IsNullOrWhiteSpace(name))return;
        var definition=(await ReadSmartPlaylists()).Single(p=>p.Id==target.Path);
        var ids=new List<long>();
        for(var offset=0;;offset+=2000){var page=await _query.QueryAsync(new(target,Descending:definition.Descending,Limit:2000,Offset:offset));ids.AddRange(page.Select(t=>t.Id));if(page.Count<2000)break;}
        if(ids.Count==0){Status("Keine passenden Tracks. Keine leere Playlist angelegt.");return;}
        var playlist=await _playlists.CreateAsync(name.Trim(),PlaylistNodeType.Playlist,null);
        try {await _playlists.AddTracksAsync(playlist.Id,ids);} catch {await _playlists.DeleteAsync(playlist.Id);throw;}
        _revealPlaylist=playlist.Id;await ReloadBrowsers();Status($"{ids.Count:N0} Tracks als normale Playlist gespeichert. Diese kann als NML exportiert werden.");
    }
}
