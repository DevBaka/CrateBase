using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Cratebase.Analysis;
using Cratebase.Core.Models;

namespace Cratebase.App.Desktop.Views;
public sealed partial class WorkspaceWindow
{
    private async Task StemDialogAsync()
    {
        var dialog=Dialog("Traktor-Stems · lokale Hintergrundberechnung",700,380);
        var panel=new StackPanel {Margin=new Thickness(18),Spacing=12};
        panel.Children.Add(new TextBlock {Text="HTDemucs erzeugt Drums, Bass, Other und Vocals. Cratebase erstellt daraus eine separate .stem.mp4 mit Original-Mix. Originale und Traktor-Collection bleiben unverändert. Anschließend die Stem-Dateien in Traktor importieren und analysieren.",TextWrapping=TextWrapping.Wrap});
        panel.Children.Add(new TextBlock {Text="Erster Start: private Python-/PyTorch-CPU-Laufzeit plus Modell (ca. 400 MB Download). CPU-Berechnung, keine Live-Stems im laufenden Traktor-Deck. Traktor-4.5.1-Import muss mit einem Testtrack praktisch geprüft werden.",TextWrapping=TextWrapping.Wrap,Foreground=Brushes.Orange});
        var directory=new TextBox {Text=_settings.StemExportDirectory,PlaceholderText="Separater Zielordner für Stem-Dateien"};panel.Children.Add(directory);
        panel.Children.Add(Button("Zielordner wählen",async()=>{var folders=await StorageProvider.OpenFolderPickerAsync(new(){Title="Traktor-Stem-Ausgabe",AllowMultiple=false});if(folders.FirstOrDefault()?.TryGetLocalPath() is {} path)directory.Text=path;}));
        var scope=new ComboBox {ItemsSource=new[]{"Markierte Tracks der aktiven Liste","Gesamte aktive Quelle (Playlist/Gruppe/Ordner/Filter)"},SelectedIndex=0,HorizontalAlignment=HorizontalAlignment.Stretch};panel.Children.Add(scope);
        panel.Children.Add(Button("Stems erzeugen",async()=>
        {
            if(string.IsNullOrWhiteSpace(directory.Text))throw new ArgumentException("Bitte einen Zielordner angeben.");
            _settings.StemExportDirectory=Path.GetFullPath(directory.Text);await SaveSettings();
            var selected=scope.SelectedIndex==0?_tables[_active].SelectedItems.OfType<Track>().ToArray():null;
            if(selected is {Length:0}){Status("Bitte zuerst Tracks markieren oder die gesamte Quelle auswählen.");return;}
            dialog.Close();await GenerateStemsAsync(selected);
        }));dialog.Content=panel;await dialog.ShowDialog(this);
    }
    private async Task GenerateStemsAsync(Track[]? selection)
    {
        if(_job!=null||_syncActive)throw new InvalidOperationException("Bitte den laufenden Auftrag abwarten.");
        using var job=_job=new();_cancel.IsVisible=true;
        try
        {
            var tracks=selection??(await PlanningCandidatesAsync(5001,job.Token)).ToArray();
            if(tracks.Length>5000)throw new InvalidOperationException("Bitte die Quelle auf maximal 5.000 Tracks eingrenzen.");
            tracks=tracks.DistinctBy(t=>t.Id).ToArray();if(tracks.Length==0){Status("Keine Tracks für Stem-Erzeugung.");return;}
            Directory.CreateDirectory(_settings.StemExportDirectory);
            var data=Environment.GetEnvironmentVariable("CRATEBASE_DATA_DIR")??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Cratebase");
            var ffmpeg=_settings.RecorderFfmpegPath??(File.Exists(Path.Combine(data,"tools","ffmpeg.exe"))?Path.Combine(data,"tools","ffmpeg.exe"):"ffmpeg");
            var generator=new ManagedStemGenerator(Path.Combine(data,"stem-runtime"),ffmpeg);
            await generator.InstallAsync(new Progress<string>(Status),job.Token);
            var roots=(await _roots.GetAllAsync(job.Token)).ToDictionary(r=>r.Id);var errors=new List<string>();var completed=0;
            for(var i=0;i<tracks.Length;i++)
            {
                job.Token.ThrowIfCancellationRequested();var track=tracks[i];var prefix=$"Stems {i+1} / {tracks.Length} · ";
                try
                {
                    if(!roots.TryGetValue(track.LibraryRootId,out var root))throw new IOException("Bibliotheksordner fehlt.");
                    var source=Cratebase.Server.LibraryServer.ContainedPath(root.Path,track.RelativePath);if(!File.Exists(source))throw new FileNotFoundException("Datei nicht erreichbar.");
                    var name=string.Join("_",(track.DisplayArtist+" - "+track.DisplayTitle).Split(Path.GetInvalidFileNameChars(),StringSplitOptions.RemoveEmptyEntries)).Trim().TrimEnd('.');
                    if(name.Length>100)name=name[..100];
                    var destination=Path.Combine(_settings.StemExportDirectory,name+$" [{track.Id}].stem.mp4");
                    if(File.Exists(destination)){errors.Add(track.DisplayTitle+": Zieldatei existiert bereits (übersprungen).");continue;}
                    await generator.GenerateAsync(source,track,destination,new Progress<string>(s=>Status(prefix+s)),job.Token);completed++;
                }
                catch(OperationCanceledException){throw;}
                catch(Exception ex){errors.Add(track.DisplayTitle+": "+ex.Message);}
            }
            if(errors.Count>0)await File.WriteAllLinesAsync(Path.Combine(_settings.StemExportDirectory,"cratebase-stem-report-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+".txt"),errors,CancellationToken.None);
            Status($"{completed} Stem-Dateien erstellt · {errors.Count} Hinweise/Fehler. Ziel: {_settings.StemExportDirectory}");
        }
        finally {_job=null;_cancel.IsVisible=false;}
    }
}
