using Cratebase.Core.Models;
namespace Cratebase.App.Desktop.Views;
public sealed partial class WorkspaceWindow
{
    private async Task IndexPlanningFolderAsync(string path,CancellationToken ct)
    {
        if(_job!=null||_syncActive)throw new InvalidOperationException("Bitte den laufenden Bibliotheksauftrag zuerst abschließen.");
        using var job=CancellationTokenSource.CreateLinkedTokenSource(ct);_job=job;_cancel.IsVisible=true;
        try
        {
            var full=Path.GetFullPath(path).TrimEnd('\\','/');
            var root=(await _roots.GetAllAsync(ct)).Where(r=>full.Equals(Path.GetFullPath(r.Path).TrimEnd('\\','/'),StringComparison.OrdinalIgnoreCase)||full.StartsWith(Path.GetFullPath(r.Path).TrimEnd('\\','/')+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)).OrderByDescending(r=>r.Path.Length).FirstOrDefault()
                ??await _roots.AddOrGetAsync(path,Path.GetFileName(full),ct);
            var progress=new Progress<Cratebase.Core.Abstractions.ScanProgress>(p=>Status($"Indexierung: {p.FilesSeen:N0} Dateien · {p.FilesAdded:N0} neu · {p.FilesUpdated:N0} aktualisiert · {p.FilesFailed:N0} nicht lesbar · {p.CurrentRelativePath}"));
            await Task.Run(()=>_scanner.ScanFolderAsync(root,path,progress,job.Token),job.Token);
            await RefreshLists();await ReloadBrowsers();await RefreshClub();
        }
        finally{_job=null;_cancel.IsVisible=false;}
    }
    private Task PreparePlanningSourceAsync(LibraryTarget target,CancellationToken ct)
    {
        if(target.Kind!=LibraryTargetKind.Directory)return Task.CompletedTask;
        Status("Ordner im Set-Planer wird eingelesen …");
        return Task.Run(async()=>
        {
            foreach(var file in Directory.EnumerateFiles(target.Path!).Where(_tags.CanRead)){ct.ThrowIfCancellationRequested();await _actions.RegisterAsync(file,ct);}
        },ct);
    }
    private async Task<IReadOnlyList<Track>> PlanningCandidatesAsync(int maximum,CancellationToken ct)
    {
        var pane=_panes[_active];var source=pane.Source;var search=pane.Search;var sort=pane.Sort;var descending=pane.Descending;var filters=pane.Options.Filters.ToArray();var field=pane.SearchField;
        var tracks=new List<Track>();
        while(tracks.Count<maximum)
        {
            var page=await _query.QueryAsync(new(source,search,sort,descending,Math.Min(2000,maximum-tracks.Count),tracks.Count,filters,field),ct);
            tracks.AddRange(page);if(page.Count==0)break;
        }
        return tracks;
    }
}
