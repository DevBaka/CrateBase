using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Cratebase.Core.Abstractions;
using Cratebase.Core.Models;

namespace Cratebase.App.Desktop.ViewModels;

public sealed class TrackPaneOptions
{
    public List<string> VisibleColumns { get; set; }=TrackMetadata.Fields.Where(f=>f.DefaultVisible).Select(f=>f.Key).ToList();
    public string Sort { get; set; }="default";
    public bool Descending { get; set; }
    public string SearchField { get; set; }="all";
    public List<MetadataFilter> Filters { get; set; }=new();
}

public sealed partial class TrackPaneViewModel(ILibraryQuery query) : ObservableObject, IDisposable
{
    public ObservableCollection<Track> Tracks { get; } = new();
    [ObservableProperty] public partial string Title { get; set; } = "Collection";
    [ObservableProperty] public partial string Search { get; set; } = "";
    [ObservableProperty] public partial string Summary { get; set; } = "";
    [ObservableProperty] public partial bool Busy { get; set; }
    public LibraryTarget Source { get; private set; } = new(LibraryTargetKind.Collection,"Collection");
    [ObservableProperty] public partial string Sort { get; set; } = "default";
    [ObservableProperty] public partial bool Descending { get; set; }
    [ObservableProperty] public partial string SearchField { get; set; }="all";
    public TrackPaneOptions Options { get; private set; }=new();
    public Action? ConfigurationChanged { get; set; }
    public string FilterLabel=>Options.Filters.Count==0?"Filter":"Filter ("+Options.Filters.Count+")";
    private bool _configuring;
    partial void OnSortChanged(string value) { Options.Sort=value; Changed(); }
    partial void OnDescendingChanged(bool value) { Options.Descending=value; Changed(); }
    partial void OnSearchFieldChanged(string value) { Options.SearchField=value; Changed(); }
    private void Changed() { if(_configuring)return; ConfigurationChanged?.Invoke();_=ReloadAsync(); }
    public void Configure(TrackPaneOptions options)
    {
        _configuring=true;
        try { Options=options; Sort=options.Sort; Descending=options.Descending;SearchField=options.SearchField;OnPropertyChanged(nameof(FilterLabel)); }
        finally { _configuring=false; }
    }
    public void SetFilters(IEnumerable<MetadataFilter> filters)
    {
        var list=filters.ToList();foreach(var filter in list) TrackMetadata.Validate(filter);
        Options.Filters=list;OnPropertyChanged(nameof(FilterLabel));Changed();
    }
    public int Total { get; private set; }
    public Action<string>? ReportError { get; set; }
    private CancellationTokenSource? _request;
    partial void OnSearchChanged(string value) => _ = ReloadAsync(true,true);
    public async Task OpenAsync(LibraryTarget target)
    {
        // A search in the previous source must not hide an entire newly opened playlist.
        if(Source!=target) Search="";
        Source = target; Title = target.Name;
        await ReloadAsync();
    }
    public async Task ReloadAsync(bool reset = true, bool debounce = false)
    {
        _request?.Cancel();
        var request = _request = new();
        Busy = true;
        try
        {
            if(debounce) await Task.Delay(180,request.Token);
            var options = new TrackQuery(Source,Search,Sort,Descending,500,reset ? 0 : Tracks.Count,Options.Filters,SearchField);
            var rows = await Task.Run(() => query.QueryAsync(options,request.Token),request.Token);
            var totals = await query.TotalsAsync(options,request.Token);
            request.Token.ThrowIfCancellationRequested();
            if (reset) Tracks.Clear();
            foreach(var track in rows) Tracks.Add(track);
            Total = totals.Count;
            Summary = $"{Tracks.Count:N0} / {Total:N0} Tracks · {TimeSpan.FromMilliseconds(totals.DurationMs).TotalHours:N1} h · {totals.Bytes/1_000_000_000.0:N2} GB" + (totals.UnknownSizes > 0 ? $" · {totals.UnknownSizes:N0} Größen unbekannt" : "");
        }
        catch(OperationCanceledException) { }
        catch(Exception ex) { ReportError?.Invoke(ex.Message); }
        finally { if(ReferenceEquals(_request,request)) { Busy = false; _request=null; } request.Dispose(); }
    }
    public void Dispose() { _request?.Cancel(); }
}
