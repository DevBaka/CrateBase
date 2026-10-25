using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Cratebase.Core.Models;

namespace Cratebase.App.Desktop.Views;

public sealed partial class WorkspaceWindow
{
    private async Task SetFlag(IReadOnlyCollection<long> ids,string field,bool value)
    {
        if(ids.Count==0)return;
        if(_syncActive) { Status("Während des Abgleichs sind Änderungen gesperrt.");return; }
        await _query.SetFlagAsync(ids,field,value);
        _updatingClub=true;
        try
        {
            foreach(var track in _panes.SelectMany(p=>p.Tracks).Concat(_player.CurrentTrack==null?Array.Empty<Track>():[_player.CurrentTrack]).Where(t=>ids.Contains(t.Id)))
                switch(field) { case "club":track.ClubReady=value;break;case "extended":track.Extended=value;break;case "intro":track.Intro=value;break;case "trash":track.Trash=value;break; }
        }
        finally { _updatingClub=false; }
        foreach(var pane in _panes.Where(p=>p.Sort==field||p.Options.Filters.Any(f=>f.Field==field)||p.SearchField==field))await pane.ReloadAsync();
        if(field=="club")await RefreshClub();
    }

    private void ApplyColumns(int index)
    {
        var options=_panes[index].Options;
        var visible=options.VisibleColumns.Where(_columns[index].ContainsKey).Distinct().ToList();
        if(visible.Count==0)visible.Add("title");options.VisibleColumns=visible;
        var restoring=_restoring;_restoring=true;
        try
        {
            foreach(var pair in _columns[index])pair.Value.IsVisible=visible.Contains(pair.Key);
            for(var i=0;i<visible.Count;i++)_columns[index][visible[i]].DisplayIndex=i;
        }
        finally { _restoring=restoring; }
    }

    private async Task ChooseColumns(int index)
    {
        var dialog=Dialog("Spalten auswählen · "+(index==0?"Oben":"Unten"),520,650);
        var layout=new Grid {RowDefinitions=new("Auto,*,Auto"),Margin=new Thickness(18)};
        layout.Children.Add(new TextBlock {Text="Angezeigte Metadaten wählen. Spalten lassen sich im Browser am Kopf verschieben und per Klick sortieren.",TextWrapping=TextWrapping.Wrap});
        var list=new StackPanel {Spacing=5,Margin=new Thickness(0,12)};
        var choices=new Dictionary<string,CheckBox>();
        foreach(var field in TrackMetadata.Fields)
        {
            var checkbox=new CheckBox {Content=field.Label,IsChecked=_panes[index].Options.VisibleColumns.Contains(field.Key)};
            choices[field.Key]=checkbox;list.Children.Add(checkbox);
        }
        var scroll=new ScrollViewer {Content=list};Grid.SetRow(scroll,1);layout.Children.Add(scroll);
        var buttons=Row();Grid.SetRow(buttons,2);
        buttons.Children.Add(Button("Übernehmen",async ()=>
        {
            var selected=choices.Where(p=>p.Value.IsChecked==true).Select(p=>p.Key).ToHashSet();
            if(selected.Count==0) {Status("Mindestens eine Spalte auswählen.");return;}
            _panes[index].Options.VisibleColumns=_panes[index].Options.VisibleColumns.Where(selected.Contains).Concat(TrackMetadata.Fields.Select(f=>f.Key).Where(k=>selected.Contains(k)&&!_panes[index].Options.VisibleColumns.Contains(k))).ToList();
            ApplyColumns(index);await SaveSettings();dialog.Close();
        }));
        buttons.Children.Add(Button("Standard",()=> {foreach(var field in TrackMetadata.Fields)choices[field.Key].IsChecked=field.DefaultVisible;return Task.CompletedTask;}));
        buttons.Children.Add(Button("Abbrechen",()=> {dialog.Close();return Task.CompletedTask;}));layout.Children.Add(buttons);dialog.Content=layout;await dialog.ShowDialog(this);
    }

    private sealed record OperatorChoice(MetadataOperator Value)
    { public override string ToString()=>TrackMetadata.Label(Value); }
    private async Task EditFilters(int index)
    {
        var vm=_panes[index];var dialog=Dialog("Metadatenfilter · "+(index==0?"Oben":"Unten"),850,540);
        var layout=new Grid {RowDefinitions=new("Auto,*,Auto,Auto"),Margin=new Thickness(18)};
        layout.Children.Add(new TextBlock {Text="Alle Regeln müssen zutreffen (UND). Beispiel: Format = mp3, Bitrate ≥ 320, BPM ≥ 125. Extended und Intro: Ja / Nein. Datum: JJJJ-MM-TT.",TextWrapping=TextWrapping.Wrap});
        var rules=new StackPanel {Spacing=8,Margin=new Thickness(0,14)};
        var editors=new List<Func<MetadataFilter>>();var error=new TextBlock {Foreground=Brushes.Orange,TextWrapping=TextWrapping.Wrap};Grid.SetRow(error,2);layout.Children.Add(error);
        void Add(MetadataFilter initial)
        {
            var row=new Grid {ColumnDefinitions=new("210,220,*,Auto"),ColumnSpacing=8};
            var field=new ComboBox {ItemsSource=TrackMetadata.Fields,SelectedItem=TrackMetadata.Get(initial.Field),HorizontalAlignment=HorizontalAlignment.Stretch};
            var comparison=new ComboBox {HorizontalAlignment=HorizontalAlignment.Stretch};var value=new TextBox {Text=initial.Value,PlaceholderText="Wert"};
            void Operators(MetadataOperator preferred)
            {var choices=TrackMetadata.Operators(((MetadataField)field.SelectedItem!).Kind).Select(o=>new OperatorChoice(o)).ToArray();comparison.ItemsSource=choices;comparison.SelectedItem=choices.FirstOrDefault(c=>c.Value==preferred)??choices[0];}
            Operators(initial.Operator);
            field.SelectionChanged+=(_,_)=>Operators(MetadataOperator.Equals);
            comparison.SelectionChanged+=(_,_)=>value.IsVisible=(comparison.SelectedItem as OperatorChoice)?.Value is not (MetadataOperator.IsEmpty or MetadataOperator.IsNotEmpty);
            value.IsVisible=initial.Operator is not (MetadataOperator.IsEmpty or MetadataOperator.IsNotEmpty);
            Func<MetadataFilter> read=()=>new(((MetadataField)field.SelectedItem!).Key,((OperatorChoice)comparison.SelectedItem!).Value,value.Text??"");
            editors.Add(read);var remove=Button("×",()=> {editors.Remove(read);rules.Children.Remove(row);return Task.CompletedTask;});
            row.Children.Add(field);Grid.SetColumn(comparison,1);row.Children.Add(comparison);Grid.SetColumn(value,2);row.Children.Add(value);Grid.SetColumn(remove,3);row.Children.Add(remove);rules.Children.Add(row);
        }
        foreach(var filter in vm.Options.Filters)Add(filter);
        var scroll=new ScrollViewer {Content=rules};Grid.SetRow(scroll,1);layout.Children.Add(scroll);
        var buttons=Row();Grid.SetRow(buttons,3);
        buttons.Children.Add(Button("+ Regel",()=> {Add(new("genre",MetadataOperator.Contains));return Task.CompletedTask;}));
        buttons.Children.Add(Button("Anwenden",()=>
        {
            try {vm.SetFilters(editors.Select(read=>read()).ToArray());dialog.Close();}catch(ArgumentException ex){error.Text=ex.Message;}return Task.CompletedTask;
        }));
        buttons.Children.Add(Button("Alle Filter löschen",()=> {vm.SetFilters([]);dialog.Close();return Task.CompletedTask;}));
        buttons.Children.Add(Button("Abbrechen",()=> {dialog.Close();return Task.CompletedTask;}));layout.Children.Add(buttons);dialog.Content=layout;await dialog.ShowDialog(this);
    }

    private async Task ViewOptionsAsync()
    {
        var dialog=Dialog("Ansicht · UI-Skalierung",500,230);var content=new StackPanel {Margin=new Thickness(20),Spacing=16};
        content.Children.Add(new TextBlock {Text="Kleinere Skalierung zeigt mehr Tracks und Bedienelemente im Hauptfenster.",TextWrapping=TextWrapping.Wrap});
        var value=new TextBlock {Text=$"{_settings.UiScalePercent:0} %"};content.Children.Add(value);
        var scale=new Slider {Minimum=70,Maximum=125,TickFrequency=5,IsSnapToTickEnabled=true,Value=_settings.UiScalePercent};
        scale.PropertyChanged+=(_,e)=> {if(e.Property!=Slider.ValueProperty)return;_settings.UiScalePercent=scale.Value;_uiScale.ScaleX=_uiScale.ScaleY=scale.Value/100;value.Text=$"{scale.Value:0} %";};content.Children.Add(scale);
        var buttons=Row();buttons.Children.Add(Button("100 %",()=> {scale.Value=100;return Task.CompletedTask;}));buttons.Children.Add(Button("Fertig",()=> {dialog.Close();return Task.CompletedTask;}));content.Children.Add(buttons);dialog.Content=content;await dialog.ShowDialog(this);await SaveSettings();
    }

    private async Task MovePlaylist(LibraryTarget source,long? parent)
    {
        if(_syncActive)throw new InvalidOperationException("Während des Abgleichs kann keine Playlist verschoben werden.");
        await _playlists.MoveAsync(source.PlaylistId!.Value,parent);
        _revealPlaylist=source.PlaylistId;
        await ReloadBrowsers();await RefreshLists();await RefreshClub();Status("„"+source.Name+"“ verschoben.");
    }
    private sealed record GroupChoice(long? Id,string Name) {public override string ToString()=>Name;}
    private async Task MovePlaylistDialog(LibraryTarget source)
    {
        var nodes=await _playlists.GetAllNodesAsync();var excluded=new HashSet<long>{source.PlaylistId!.Value};
        while(true) {var added=0;foreach(var node in nodes.Where(n=>n.ParentId.HasValue&&excluded.Contains(n.ParentId.Value)))if(excluded.Add(node.Id))added++;if(added==0)break;}
        string PathOf(PlaylistNode node)
        {var parts=new List<string>{node.Name};var seen=new HashSet<long>{node.Id};while(node.ParentId is { } id&&seen.Add(id)&&nodes.FirstOrDefault(n=>n.Id==id) is { } parent){parts.Insert(0,parent.Name);node=parent;}return string.Join(" / ",parts);}
        var choices=new[]{new GroupChoice(null,"Hauptebene")}.Concat(nodes.Where(n=>n.NodeType==PlaylistNodeType.Folder&&!excluded.Contains(n.Id)).Select(n=>new GroupChoice(n.Id,PathOf(n)))).ToArray();
        var dialog=Dialog("Playlist/Gruppe verschieben",600,220);var panel=new StackPanel {Margin=new Thickness(20),Spacing=16};panel.Children.Add(new TextBlock {Text=source.Name});
        var picker=new ComboBox {ItemsSource=choices,SelectedIndex=0,HorizontalAlignment=HorizontalAlignment.Stretch};panel.Children.Add(picker);
        panel.Children.Add(Button("Verschieben",async ()=> {if(picker.SelectedItem is not GroupChoice choice)return;await MovePlaylist(source,choice.Id);dialog.Close();}));dialog.Content=panel;await dialog.ShowDialog(this);
    }
}
