using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Cratebase.Core.Abstractions;
using Cratebase.Core.Models;

namespace Cratebase.App.Desktop.Views;

public sealed partial class SetPlanningView
{
    private readonly ObservableCollection<Track> _candidateTracks=[];
    private readonly Button _candidateListButton=new() {Name="SetCandidateList",Content="KI-Vorauswahl (0) ▾",VerticalAlignment=VerticalAlignment.Top};
    private readonly TextBlock _candidateProgress=new() {Text="Noch kein KI-Vorschlag gestartet.",TextWrapping=TextWrapping.Wrap};
    private readonly TextBlock _candidateStage=new() {TextWrapping=TextWrapping.Wrap};
    private Window? _candidateWindow;
    private DataGrid? _candidateTable;
    private IAudioPlayer? _candidateAudio;
    private long? _candidatePlayingId;
    private int _candidateAuditionVersion;
    private readonly CancellationTokenSource _candidateLifetime=new();

    private void ShowCandidates(IReadOnlyList<Track> tracks,string stage)
    {
        var selected=(_candidateTable?.SelectedItem as Track)?.Id;
        _candidateTracks.Clear();foreach(var track in tracks)_candidateTracks.Add(track);
        _candidateListButton.Content=$"KI-Vorauswahl ({tracks.Count}) ▾";_candidateStage.Text=stage;
        if(_candidateTable!=null)_candidateTable.SelectedItem=tracks.FirstOrDefault(t=>t.Id==selected)??tracks.FirstOrDefault();
    }
    private void ShowCandidateList()
    {
        if(_candidateWindow!=null){_candidateWindow.Activate();return;}
        if(TopLevel.GetTopLevel(this) is not Window owner)return;
        var window=_candidateWindow=new Window {Title="KI-Vorauswahl · vorläufige Tracks vorhören",Width=900,Height=520,WindowStartupLocation=WindowStartupLocation.CenterOwner};
        var panel=new Grid {Margin=new Thickness(12),RowDefinitions=new("Auto,Auto,*,Auto"),RowSpacing=8};
        panel.Children.Add(_candidateStage);Grid.SetRow(_candidateProgress,1);panel.Children.Add(_candidateProgress);
        var table=_candidateTable=new DataGrid {Name="SetCandidateTracks",ItemsSource=_candidateTracks,AutoGenerateColumns=false,IsReadOnly=true,SelectionMode=DataGridSelectionMode.Single,RowHeight=27};
        foreach(var c in new[]{("Artist","DisplayArtist",180d),("Titel","DisplayTitle",280d),("Genre","GenreRaw",140d),("BPM","Bpm",65d),("Key","MusicalKey",60d)})
            table.Columns.Add(new DataGridTextColumn {Header=c.Item1,Binding=new Binding(c.Item2),Width=new DataGridLength(c.Item3)});
        table.SelectedItem=_candidateTracks.FirstOrDefault();table.DoubleTapped+=async(_,_)=>await AuditionCandidateAsync();Grid.SetRow(table,2);panel.Children.Add(table);
        var controls=new StackPanel {Orientation=Orientation.Horizontal,Spacing=8};
        var play=new Button {Name="SetCandidatePlay",Content="▶ / ⏸ Vorhören"};play.Click+=async(_,_)=>await AuditionCandidateAsync();controls.Children.Add(play);
        var stop=new Button {Content="■ Stop"};stop.Click+=(_,_)=>StopCandidateAudio();controls.Children.Add(stop);
        var seek=new Slider {Minimum=0,Maximum=100,Width=220};ToolTip.SetTip(seek,"Position im vorgehörten Track");
        seek.PropertyChanged+=(_,e)=>{if(e.Property==Slider.ValueProperty&&_candidateAudio is {} audio&&audio.Duration>TimeSpan.Zero)audio.Seek(TimeSpan.FromMilliseconds(audio.Duration.TotalMilliseconds*seek.Value/100));};controls.Children.Add(seek);
        controls.Children.Add(new TextBlock {Text="Vorauswahl ist noch kein fertiges Set.",VerticalAlignment=VerticalAlignment.Center});
        Grid.SetRow(controls,3);panel.Children.Add(controls);window.Content=panel;
        window.Closed+=(_,_)=>{StopCandidateAudio();panel.Children.Clear();_candidateTable=null;_candidateWindow=null;};window.Show(owner);
    }
    private async Task AuditionCandidateAsync()
    {
        if(_libraryLocked()||_candidateLifetime.IsCancellationRequested)return;
        try
        {
            if(_candidateTable?.SelectedItem is not Track track)return;
            if(_candidatePlayingId==track.Id&&_candidateAudio is {} current)
            {if(current.State==PlaybackState.Playing)current.Pause();else current.Play();return;}
            var version=++_candidateAuditionVersion;var window=_candidateWindow;
            var path=await ResolveAsync(track,_candidateLifetime.Token);_candidateLifetime.Token.ThrowIfCancellationRequested();
            if(version!=_candidateAuditionVersion||window!=_candidateWindow)return;
            if(!File.Exists(path))throw new IOException("Datei zum Vorhören nicht erreichbar: "+track.DisplayTitle);
            StopPreview();_pauseLibrary();var audio=_candidateAudio=_audioFactory();_candidatePlayingId=track.Id;
            audio.PlaybackEnded+=(_,_)=>Dispatcher.UIThread.Post(()=>{if(ReferenceEquals(_candidateAudio,audio))StopCandidateAudio();});
            audio.Volume=.8f;audio.Load(path);audio.Play();
        }
        catch(OperationCanceledException){}
        catch(Exception ex){StopCandidateAudio();_status(ex.Message);}
    }
    private void StopCandidateAudio()
    {++_candidateAuditionVersion;var audio=_candidateAudio;_candidateAudio=null;_candidatePlayingId=null;audio?.Stop();audio?.Dispose();}
}
