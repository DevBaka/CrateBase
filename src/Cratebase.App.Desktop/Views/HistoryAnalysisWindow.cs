using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using Cratebase.Analysis;
using Cratebase.Audio;
using Cratebase.Core.Abstractions;
using Cratebase.Core.Models;
using Cratebase.Import;

namespace Cratebase.App.Desktop.Views;

public sealed class HistoryAnalysisWindow : Window
{
    private readonly HistoryAudioAnalyzer _analyzer;
    private readonly Func<IReadOnlyList<SetTracklistMarker>,Task> _apply;
    private readonly Action _pauseLibrary;
    private readonly TextBox _history=new(){Name="HistoryNmlPath",IsReadOnly=true,PlaceholderText="Traktor-History (.nml)"};
    private readonly TextBox _recording=new(){Name="HistoryRecordingPath",IsReadOnly=true,PlaceholderText="Aufnahme (.mp4, .mkv, .wav, …)"};
    private readonly TextBlock _status=new(){Name="HistoryAnalysisStatus",TextWrapping=TextWrapping.Wrap};
    private readonly TextBlock _issues=new(){TextWrapping=TextWrapping.Wrap};
    private readonly TextBox _unmatched=new(){IsReadOnly=true,AcceptsReturn=true,Height=100};
    private readonly TextBlock _chapterHint=new(){TextWrapping=TextWrapping.Wrap,FontSize=11};
    private readonly CheckBox _mashups=new(){Content="Längere Überlagerungen als mögliche Live-Mashups kennzeichnen"};
    private readonly CheckBox _compact=new(){Content="YouTube-Kurzfassung (bis 4.500 UTF-8-Bytes; vollständige Titel im Bericht)",IsChecked=true};
    private readonly CheckBox _showUncertain=new(){Name="HistoryShowUncertain",Content="Unsichere Treffer anzeigen"};
    private readonly DataGrid _matches=new(){Name="HistoryMatches",AutoGenerateColumns=false,IsReadOnly=false,CanUserResizeColumns=true,RowHeight=28};
    private readonly TextBox _preview=new(){IsReadOnly=true,AcceptsReturn=true,Height=120,FontFamily=FontFamily.Parse("Consolas")};
    private readonly CancellationTokenSource _lifetime=new();
    private CancellationTokenSource? _job;
    private readonly HistoryReviewPlayer _review;
    private HistoryAudioReport? _report;
    private readonly List<Button> _jobButtons=[];
    private readonly string _directory;
    public HistoryAnalysisWindow(string directory,string ffmpeg,Func<IReadOnlyList<SetTracklistMarker>,Task> apply,Action pauseLibrary)
    {
        _directory=directory;_analyzer=new(directory,ffmpeg);_apply=apply;_pauseLibrary=pauseLibrary;
        _review=new(this,directory,ffmpeg,pauseLibrary,message=>_status.Text=message);
        Title="History + Aufnahme · YouTube-Tracklist";Width=1280;Height=900;MinWidth=900;MinHeight=740;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        var panel=new Grid{Margin=new Thickness(16),RowDefinitions=new("Auto,Auto,Auto,*,Auto,Auto,Auto"),RowSpacing=8};
        var inputs=new StackPanel{Spacing=6};
        inputs.Children.Add(new TextBlock{Text="History liefert Kandidaten; die Einsatzzeiten werden auf der ersten Audiospur der Aufnahme gesucht. Ergebnisse bitte vor Veröffentlichung prüfen.",TextWrapping=TextWrapping.Wrap});
        inputs.Children.Add(FileRow(_history,"History auswählen …",["*.nml"]));inputs.Children.Add(FileRow(_recording,"Aufnahme auswählen …",["*.mp4","*.mkv","*.mov","*.wav","*.flac","*.mp3","*.m4a"]));panel.Children.Add(inputs);
        var actions=new WrapPanel();actions.Children.Add(Button("Audio-Abgleich starten",AnalyzeAsync));
        var cancel=new Button{Content="Abbrechen"};cancel.Click+=(_,_)=>_job?.Cancel();actions.Children.Add(cancel);
        actions.Children.Add(Button("Bericht öffnen …",OpenReportAsync));Grid.SetRow(actions,1);panel.Children.Add(actions);
        actions.Children.Add(Button("Nur starke Treffer auswählen",()=>{if(_report!=null){foreach(var match in _report.Matches)match.Include=match.Confidence=="high";RefreshRows();}return Task.CompletedTask;}));
        actions.Children.Add(Button("Alle abwählen",()=>{if(_report!=null){foreach(var match in _report.Matches)match.Include=false;RefreshRows();}return Task.CompletedTask;}));
        _showUncertain.IsCheckedChanged+=(_,_)=>RefreshRows();actions.Children.Add(_showUncertain);
        Grid.SetRow(_status,2);panel.Children.Add(_status);
        _matches.Columns.Add(new DataGridCheckBoxColumn{Header="✓",Binding=new Binding("Include"),Width=new DataGridLength(42)});
        _matches.Columns.Add(new DataGridTextColumn{Header="Zeit",Binding=new Binding("Timestamp"),Width=new DataGridLength(120),MinWidth=110,IsReadOnly=true});
        _matches.Columns.Add(new DataGridTextColumn{Header="Treffer",Binding=new Binding("EvidenceCount"),Width=new DataGridLength(70),IsReadOnly=true});
        foreach(var c in new[]{("Start (Sekunden, editierbar)","Start",165d),("Ende (Sek.)","End",90d),("Artist","Artist",170d),("Titel","Title",310d),("Prüfstatus","Assessment",230d),("Belegte Sekunden","SupportSeconds",105d)})
            _matches.Columns.Add(new DataGridTextColumn{Header=c.Item1,Binding=new Binding(c.Item2),Width=new DataGridLength(c.Item3),IsReadOnly=c.Item2!="Start"});
        _matches.CellEditEnded+=(_,_)=>RefreshPreview();_matches.DoubleTapped+=async(_,_)=>await Guard(PreviewAsync);
        _matches.SelectionChanged+=(_,_)=>_review.Select(_matches.SelectedItem as HistoryAudioMatch);
        var body=new Grid{ColumnDefinitions=new("*,0,0"),ColumnSpacing=0};body.Children.Add(_matches);
        var splitter=new GridSplitter{ResizeDirection=GridResizeDirection.Columns,Background=Brush.Parse("#29313E"),IsVisible=false};Grid.SetColumn(splitter,1);body.Children.Add(splitter);
        Grid.SetColumn(_review.VideoPanel,2);body.Children.Add(_review.VideoPanel);_review.VideoPanel.IsVisible=false;
        _review.DockVisibilityChanged+=visible=>{body.ColumnDefinitions[1].Width=new GridLength(visible?6:0);body.ColumnDefinitions[2].Width=new GridLength(visible?380:0);splitter.IsVisible=visible;_review.VideoPanel.IsVisible=visible;};
        Grid.SetRow(body,3);panel.Children.Add(body);
        Grid.SetRow(_review.Controls,4);panel.Children.Add(_review.Controls);
        var review=new StackPanel{Spacing=5};review.Children.Add(_issues);review.Children.Add(new Expander{Header="Nicht nachgewiesene Tracks / Analysefehler",Content=_unmatched,HorizontalAlignment=HorizontalAlignment.Stretch});
        var export=new StackPanel{Spacing=5};export.Children.Add(_mashups);_mashups.IsCheckedChanged+=(_,_)=>RefreshPreview();export.Children.Add(_compact);_compact.IsCheckedChanged+=(_,_)=>RefreshPreview();export.Children.Add(_preview);export.Children.Add(_chapterHint);
        review.Children.Add(new Expander{Header="YouTube-Export · Vorschau und Optionen",Content=export,HorizontalAlignment=HorizontalAlignment.Stretch});Grid.SetRow(review,5);panel.Children.Add(review);
        var output=new WrapPanel();output.Children.Add(Button("Treffer in Aufnahme vorhören",PreviewAsync));var stop=new Button{Content="■ Stop"};stop.Click+=(_,_)=>StopAudio();output.Children.Add(stop);
        output.Children.Add(Button("YouTube-Text kopieren",async()=>{RefreshPreview();if(Clipboard!=null)await Clipboard.SetTextAsync(_preview.Text??"");}));
        output.Children.Add(Button("Text + Prüfbericht speichern …",SaveAsync));
        output.Children.Add(Button("Thumbnail & Titel …",()=>{var artwork=new SetArtworkWindow(Path.GetDirectoryName(_directory)!,ffmpeg,_recording.Text??"",_preview.Text??"");artwork.Show(this);return Task.CompletedTask;}));
        output.Children.Add(Button("Auswahl in Recorder-Tracklist übernehmen",async()=>{if(_report==null)return;ValidateSelection();await _apply(_report.Markers(_mashups.IsChecked==true));_status.Text="Auswahl übernommen. Bestehende Recorder-Marker wurden beibehalten.";}));Grid.SetRow(output,6);panel.Children.Add(output);
        Content=panel;
        Closed+=(_,_)=>{_lifetime.Cancel();_job?.Cancel();_review.Dispose();};
    }
    private Control FileRow(TextBox text,string label,string[] extensions)
    {
        var row=new Grid{ColumnDefinitions=new("*,Auto"),ColumnSpacing=8};row.Children.Add(text);
        var pick=Button(label,async()=>{var chosen=await StorageProvider.OpenFilePickerAsync(new(){Title=label,AllowMultiple=false,FileTypeFilter=[new(label){Patterns=extensions}]});if(chosen.FirstOrDefault()?.TryGetLocalPath() is {} path)text.Text=path;});
        Grid.SetColumn(pick,1);row.Children.Add(pick);return row;
    }
    private Button Button(string label,Func<Task> action)
    {var button=new Button{Content=label,Margin=new Thickness(3)};button.Click+=async(_,_)=>await Guard(action);_jobButtons.Add(button);return button;}
    private async Task Guard(Func<Task> action)
    {
        if(_job!=null)return;
        using var job=_job=CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);foreach(var button in _jobButtons)button.IsEnabled=false;
        try{await action();}catch(OperationCanceledException){_status.Text="Auftrag abgebrochen. Vorherige Ergebnisse bleiben erhalten.";}catch(Exception ex){_status.Text=ex.Message;}
        finally{_job=null;foreach(var button in _jobButtons)button.IsEnabled=true;}
    }
    private async Task AnalyzeAsync()
    {
        // Avalonia controls belong to the UI thread. Only immutable input values
        // may cross into the background NML reader.
        var historyPath=_history.Text;var recordingPath=_recording.Text;
        if(string.IsNullOrWhiteSpace(historyPath)||string.IsNullOrWhiteSpace(recordingPath))throw new InvalidOperationException("Bitte History und Aufnahme auswählen.");
        var tracks=await Task.Run(()=>TraktorHistoryReader.Read(historyPath),_job!.Token);
        _status.Text=$"{tracks.Count} History-Kandidaten. Audio-Abgleich läuft lokal; je nach Set-Länge dauert dies mehrere Minuten.";
        var report=await _analyzer.AnalyzeAsync(recordingPath,tracks,new Progress<string>(message=>_status.Text=message),_job.Token);
        ApplyReport(report);
    }
    public void ApplyReport(HistoryAudioReport report)
    {
        var tracks=(report.RawMatches.Count>0?report.RawMatches:report.Matches).Select(m=>new HistoryAudioTrack(m.Id,m.Path,m.Artist,m.Title)).Concat(report.Unmatched).Concat(report.Missing).DistinctBy(t=>t.Path,StringComparer.OrdinalIgnoreCase).ToArray();
        report.ConsolidateMatches();report.RefreshEvidence(tracks);
        _report=report;_recording.Text=report.Recording;RefreshRows();
        _review.Configure(report.Recording,report.Duration,report.Matches);
        _issues.Text=$"{report.Matches.Count} Song-Einsätze aus {report.RawMatches.Count} Einzel-Treffern · {report.Unmatched.Count} ohne starken Nachweis · {report.Missing.Count} Dateien fehlen · {report.Errors.Count} Fehler. Vor Veröffentlichung prüfen.";
        ToolTip.SetTip(_issues,string.Join("\n",report.Unmatched.Select(t=>t.Artist+" – "+t.Title)));
        _unmatched.Text=string.Join("\n",report.Unmatched.Select(t=>"Nicht nachgewiesen: "+t.Artist+" – "+t.Title).Concat(report.Errors.Select(e=>"Fehler: "+e.Track.Title+" · "+e.Error)));
        _status.Text="Analyse fertig. Startzeiten sind früheste belegte Audio-Treffer, keine garantierten Fader-Einsätze. Auswahl und Zeitstempel vor Veröffentlichung prüfen.";RefreshPreview();
    }
    private void RefreshPreview(){if(_report!=null){_preview.Text=_compact.IsChecked==true?_report.CompactYouTube(_mashups.IsChecked==true):_report.YouTube(_mashups.IsChecked==true);var bytes=System.Text.Encoding.UTF8.GetByteCount(_preview.Text);_chapterHint.Text=_report.ChapterHint()+$" · {bytes:N0} UTF-8-Bytes"+(bytes>5000?" · Für eine einzelne YouTube-Beschreibung zu lang.":"");}}
    private void RefreshRows(){_matches.ItemsSource=null;_matches.ItemsSource=_report?.Matches.Where(m=>_showUncertain.IsChecked==true||m.Confidence=="high"||m.Include).ToArray();RefreshPreview();}
    private void ValidateSelection()
    {if(_report!=null&&_report.Matches.Any(m=>m.Include&&(!double.IsFinite(m.Start)||m.Start<0||m.Start>=m.End||m.Start>=_report.Duration)))throw new InvalidOperationException("Bitte die Startzeit der ausgewählten Treffer prüfen: nicht negativ, vor Abschnittsende und innerhalb der Aufnahme.");}
    private async Task OpenReportAsync()
    {var files=await StorageProvider.OpenFilePickerAsync(new(){Title="Audio-Prüfbericht öffnen",FileTypeFilter=[new("JSON"){Patterns=["*.json"]}]});if(files.FirstOrDefault()?.TryGetLocalPath() is {} path)ApplyReport(JsonSerializer.Deserialize<HistoryAudioReport>(await File.ReadAllTextAsync(path,_job!.Token),new JsonSerializerOptions(JsonSerializerDefaults.Web))??throw new IOException("Bericht ist leer."));}
    private async Task PreviewAsync()
    {
        if(_report==null||_matches.SelectedItem is not HistoryAudioMatch match)return;
        _review.Select(match);await _review.PlayAtAsync(Math.Max(0,match.Start-3));
    }
    private async Task SaveAsync()
    {
        if(_report==null)return;ValidateSelection();RefreshPreview();var file=await StorageProvider.SaveFilePickerAsync(new(){Title="YouTube-Tracklist und Prüfbericht speichern",SuggestedFileName=Path.GetFileNameWithoutExtension(_report.Recording)+"-Tracklist.txt",DefaultExtension="txt"});
        if(file?.TryGetLocalPath() is not {} path)return;
        await File.WriteAllTextAsync(path,_preview.Text??"",_job!.Token);
        await File.WriteAllTextAsync(path+".full.txt",_report.YouTube(_mashups.IsChecked==true),_job.Token);
        await File.WriteAllTextAsync(path+".report.json",JsonSerializer.Serialize(_report,new JsonSerializerOptions(JsonSerializerDefaults.Web){WriteIndented=true}),_job.Token);
        _status.Text="YouTube-Text und vollständiger Prüfbericht gespeichert.";
    }
    public void StopPlayback()=>StopAudio();
    private void StopAudio()=>_review.Stop();
}
