using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Cratebase.Import;

namespace Cratebase.App.Desktop.Views;

public sealed partial class WorkspaceWindow
{
    private sealed record ClubExportRequest(string Directory,bool SkipMissing);
    private async Task ExportClubMediaAsync()
    {
        if(_job!=null||_syncActive){Status("Ein Auftrag läuft bereits.");return;}
        if(_clubExporter==null){Status("Der Club-Dateiexport ist nicht initialisiert.");return;}
        _job=new();_syncActive=true;_cancel.IsVisible=true;
        try
        {
            Status("Club-Export vorbereiten: Genre-Tags, Quellordner und Dateigrößen prüfen …");
            var plan=await Task.Run(()=>_clubExporter.PlanAsync(_settings.ClubExportFolders,_job.Token));
            if(plan.Items.Count==0){Status("Die Club Collection ist leer. Zuerst Tracks als Club-tauglich markieren.");return;}
            var request=await ClubExportPreview(plan);
            if(request==null){Status("Club-Export abgebrochen.");return;}
            _settings.ClubExportDirectory=request.Directory;
            foreach(var group in plan.Groups)_settings.ClubExportFolders[group.Key]=group.Folder;
            await SaveSettings();
            var progress=new Progress<ClubExportProgress>(p=>Status($"Club-Export {p.Completed:N0}/{p.Total:N0} · {p.Bytes/1_000_000_000.0:N2}/{p.TotalBytes/1_000_000_000.0:N2} GB · {p.Message}"));
            var result=await Task.Run(()=>_clubExporter.ExportAsync(plan,request.Directory,request.SkipMissing,progress,_job.Token));
            Status($"Club-Export {(result.Skipped==0?"vollständig":"unvollständig")}: {result.Copied:N0} kopiert, {result.Reused:N0} wiederverwendet, {result.Skipped:N0} übersprungen. NML: {result.NmlPath}");
            await Choose("Club-Export abgeschlossen",$"Ziel: {request.Directory}\n\n{result.Copied:N0} Dateien kopiert · {result.Reused:N0} geprüft und wiederverwendet · {result.Skipped:N0} übersprungen\n{result.Bytes/1_000_000_000.0:N2} GB\n\nDie NML liegt im Hauptverzeichnis und verweist auf die exportierten Dateien. Details stehen in {ClubMediaExporter.ReportName}.\n\nBeim Verschieben auf einen anderen Rechner oder ein anderes Laufwerk muss Traktor die neuen Pfade ggf. über ‚Relocate‘ zuordnen.",["Schließen"]);
        }
        finally {_job.Dispose();_job=null;_syncActive=false;_cancel.IsVisible=false;}
    }
    private async Task<ClubExportRequest?> ClubExportPreview(ClubExportPlan plan)
    {
        var dialog=Dialog("Club Collection · Dateien und NML exportieren",1050,740);
        var layout=new Grid {RowDefinitions=new("Auto,Auto,Auto,*,Auto"),Margin=new Thickness(18)};
        layout.Children.Add(new TextBlock {Text=$"{plan.Items.Count:N0} Club-Tracks · {plan.Bytes/1_000_000_000.0:N2} GB · {plan.Missing:N0} Quelldateien nicht erreichbar · {plan.Groups.Count(g=>g.Warning!=null):N0} Genre-Zuordnungen prüfen",TextWrapping=TextWrapping.Wrap,FontWeight=FontWeight.Bold});
        var destination=new TextBox {Text=_settings.ClubExportDirectory,HorizontalAlignment=HorizontalAlignment.Stretch};
        var target=new Grid {ColumnDefinitions=new("*,Auto"),Margin=new Thickness(0,12)};target.Children.Add(destination);
        var browse=Button("Zielordner auswählen …",async ()=>
        {
            var folders=await StorageProvider.OpenFolderPickerAsync(new(){Title="Ziel für die komplette Club Collection auswählen",AllowMultiple=false});
            if(folders.FirstOrDefault()?.TryGetLocalPath() is {} folder)destination.Text=folder;
        });Grid.SetColumn(browse,1);target.Children.Add(browse);Grid.SetRow(target,1);layout.Children.Add(target);
        var explanation=new TextBlock {Text=$"Die NML ({ClubMediaExporter.NmlName}) liegt direkt im Zielordner. Audiodateien werden darunter nach Genre kopiert, z. B. Techno/Hard Techno. Herkunfts- und Jahresordner entfallen. Bekannte spezifische Genre-Ordner haben Vorrang; Widersprüche zum Tag stehen in der Vorschau. Zielordner lassen sich hier bearbeiten. Es wird nichts aus der Quellsammlung verschoben oder gelöscht. Bei erneuten Exporten werden vollständige Kopien geprüft und wiederverwendet; andere und ältere Dateien bleiben erhalten.",TextWrapping=TextWrapping.Wrap,Foreground=Muted,Margin=new Thickness(0,0,0,12)};Grid.SetRow(explanation,2);layout.Children.Add(explanation);
        var groups=new ListBox {ItemsSource=plan.Groups,ItemTemplate=new FuncDataTemplate<ClubExportGroup>((group,_)=>
        {
            if(group==null)return new TextBlock();
            var row=new StackPanel {Spacing=5,Margin=new Thickness(8)};
            var top=new Grid {ColumnDefinitions=new("*,85,1.1*")};
            top.Children.Add(new TextBlock {Text=group.Genre,TextWrapping=TextWrapping.Wrap,VerticalAlignment=VerticalAlignment.Center});
            var count=new TextBlock {Text=$"{group.Count:N0} Tracks",VerticalAlignment=VerticalAlignment.Center};Grid.SetColumn(count,1);top.Children.Add(count);
            var folder=new TextBox {Text=group.Folder,PlaceholderText="Genre/Untergenre",HorizontalAlignment=HorizontalAlignment.Stretch};folder.TextChanged+=(_,_)=>group.Folder=folder.Text??"";Grid.SetColumn(folder,2);top.Children.Add(folder);row.Children.Add(top);
            if(group.Warning!=null)row.Children.Add(new TextBlock {Text=group.Warning,Foreground=Brushes.Orange,TextWrapping=TextWrapping.Wrap});
            row.Children.Add(new TextBlock {Text=string.Join("\n",plan.Items.Where(i=>i.GroupKey==group.Key).Take(2).Select(i=>i.Source)),FontSize=11,Foreground=Muted,TextWrapping=TextWrapping.Wrap});
            return row;
        })};Grid.SetRow(groups,3);layout.Children.Add(groups);
        var footer=new StackPanel {Spacing=8,Margin=new Thickness(0,10,0,0)};
        var skip=new CheckBox {Content="Nicht erreichbare Dateien ausdrücklich überspringen (Export kann dadurch unvollständig sein)",IsChecked=false};footer.Children.Add(skip);
        var error=new TextBlock {Foreground=Brushes.Orange,TextWrapping=TextWrapping.Wrap};footer.Children.Add(error);
        var buttons=Row();buttons.Children.Add(Button("Export starten",()=>
        {
            try
            {
                if(plan.Missing>0&&skip.IsChecked!=true)throw new ArgumentException("Nicht erreichbare Dateien zuerst wieder verbinden oder das Überspringen ausdrücklich aktivieren.");
                foreach(var group in plan.Groups)group.Folder=ClubGenreLayout.ValidateFolder(group.Folder.Trim());
                if(string.IsNullOrWhiteSpace(destination.Text)||!Path.IsPathFullyQualified(destination.Text.Trim()))throw new ArgumentException("Bitte einen absoluten Zielordner angeben.");
                dialog.Close(new ClubExportRequest(Path.GetFullPath(destination.Text.Trim()),skip.IsChecked==true));
            }
            catch(Exception ex){error.Text=ex.Message;}
            return Task.CompletedTask;
        }));buttons.Children.Add(Button("Abbrechen",()=> {dialog.Close(null);return Task.CompletedTask;}));footer.Children.Add(buttons);Grid.SetRow(footer,4);layout.Children.Add(footer);
        dialog.Content=layout;return await dialog.ShowDialog<ClubExportRequest?>(this);
    }
}
