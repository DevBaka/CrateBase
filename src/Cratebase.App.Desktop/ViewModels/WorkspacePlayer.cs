using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Avalonia.Threading;
using Cratebase.Core.Models;
using Cratebase.Core.Abstractions;

namespace Cratebase.App.Desktop.ViewModels;

public sealed partial class WorkspacePlayer : ObservableObject, IDisposable
{
    private readonly IAudioPlayer _audio;
    private readonly ILibraryRootRepository _roots;
    private readonly IWaveformGenerator _generator;
    private readonly IWaveformCache _cache;
    private readonly ITrackWaveformSource _stripes;
    private readonly ITrackRepository? _tracks;
    private IReadOnlyList<Track> _queue = [];
    private int _queueIndex=-1;
    public IReadOnlyList<Track> Queue=>_queue;
    public int QueueIndex=>_queueIndex;
    public void SeekToMilliseconds(long value)
    {
        var max=CurrentTrack?.DurationMs>0?CurrentTrack.DurationMs:Math.Max(0,Duration*1000);
        var position=Math.Clamp((double)value,0,max);_audio.Seek(TimeSpan.FromMilliseconds(position));Position=position/1000;
    }
    private CancellationTokenSource? _waveformRequest;
    public Track? CurrentTrack { get; private set; }
    [ObservableProperty] public partial PlaybackState PlaybackStatus { get; set; }
    public bool CanNext => _queueIndex>=0&&_queueIndex<_queue.Count-1;
    public bool CanPrevious => _queueIndex>0;
    public Action<string>? ReportError { get; set; }
    public event Action<Track>? AvailabilityChanged;
    [ObservableProperty] public partial string Title { get; set; } = "Track zum Vorhören doppelklicken";
    [ObservableProperty] public partial string Artist { get; set; } = "";
    [ObservableProperty] public partial double Position { get; set; }
    [ObservableProperty] public partial double Duration { get; set; }
    [ObservableProperty] public partial double Volume { get; set; } = 80;
    [ObservableProperty] public partial WaveformData? Waveform { get; set; }
    [ObservableProperty] public partial bool IsWaveformLoading { get; set; }
    [ObservableProperty] public partial string WaveformStatus { get; set; } = "Waveform erscheint beim Laden eines Tracks";
    public string Time => $"{TimeSpan.FromSeconds(Math.Max(0,Position)):mm\\:ss} / {TimeSpan.FromSeconds(Math.Max(0,Duration)):mm\\:ss}";
    public double Progress => Duration > 0 ? Position / Duration : 0;
    public WorkspacePlayer(IAudioPlayer audio, ILibraryRootRepository roots, IWaveformGenerator generator,IWaveformCache cache,ITrackWaveformSource stripes,ITrackRepository? tracks=null)
    {
        _audio=audio; _roots=roots; _generator=generator; _cache=cache; _stripes=stripes;
        _tracks=tracks;
        audio.Volume=.8f;
        audio.PositionChanged += PositionChanged; audio.PlaybackEnded += PlaybackEnded;
    }
    private void PositionChanged(object? sender,EventArgs e) => Dispatcher.UIThread.Post(() => { PlaybackStatus=_audio.State; Position=_audio.Position.TotalSeconds; Duration=_audio.Duration.TotalSeconds; OnPropertyChanged(nameof(Time)); OnPropertyChanged(nameof(Progress)); });
    private void PlaybackEnded(object? sender,EventArgs e) => Dispatcher.UIThread.Post(() => {PlaybackStatus=_audio.State;NextCommand.Execute(null);});
    partial void OnVolumeChanged(double value) => _audio.Volume=(float)(value/100);
    public Task PlayAsync(Track track,IReadOnlyList<Track> queue)
    {
        var snapshot=queue.ToArray();var index=Array.FindIndex(snapshot,t=>t.Id==track.Id);
        if(index<0) {snapshot=[track,..snapshot];index=0;}
        return PlayQueueAsync(snapshot,index,1);
    }
    private async Task SetAvailabilityAsync(Track track,bool reachable,CancellationToken ct)
    {
        if(reachable&&track.MissingSinceUtc==null)return;
        var missing=reachable?(DateTime?)null:track.MissingSinceUtc??DateTime.UtcNow;
        try {if(_tracks!=null)await _tracks.SetFileAvailabilityAsync(track.Id,reachable,ct);}
        catch(OperationCanceledException){throw;}
        catch(Exception ex){ReportError?.Invoke("Dateistatus konnte nicht gespeichert werden: "+ex.Message);}
        track.MissingSinceUtc=missing;
        AvailabilityChanged?.Invoke(track);
    }
    private async Task PlayQueueAsync(Track[] queue,int start,int direction)
    {
        _waveformRequest?.Cancel();
        using var request=new CancellationTokenSource(); _waveformRequest=request;
        try
        {
            var roots=await _roots.GetAllAsync(request.Token);
            request.Token.ThrowIfCancellationRequested();
            _queue=queue;
            Track? track=null;string? path=null;
            for(var index=start;index>=0&&index<queue.Length;index+=direction)
            {
                request.Token.ThrowIfCancellationRequested();
                var candidate=queue[index];_queueIndex=index;
                OnPropertyChanged(nameof(CanNext));OnPropertyChanged(nameof(CanPrevious));
                try
                {
                    var root=roots.FirstOrDefault(r=>r.Id==candidate.LibraryRootId);
                    if(root==null)throw new DirectoryNotFoundException("Bibliotheksordner ist nicht erreichbar.");
                    var candidatePath=Path.Combine(root.Path,candidate.RelativePath);
                    // Probe actual read access as well as existence; do not alter the source file.
                    await Task.Run(()=> {using var stream=new FileStream(candidatePath,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);},request.Token).WaitAsync(request.Token);
                    request.Token.ThrowIfCancellationRequested();
                    _audio.Load(candidatePath);_audio.Play();
                    await SetAvailabilityAsync(candidate,true,request.Token);
                    request.Token.ThrowIfCancellationRequested();
                    track=candidate;path=candidatePath;break;
                }
                catch(Exception ex) when(ex is IOException or UnauthorizedAccessException)
                {
                    await SetAvailabilityAsync(candidate,false,request.Token);
                    ReportError?.Invoke($"Datei nicht erreichbar · übersprungen: {candidate.DisplayTitle}");
                }
            }
            if(track==null)
            {
                PlaybackStatus=_audio.State;
                ReportError?.Invoke("Keine weiteren erreichbaren Dateien in dieser Wiedergabeliste. Nicht erreichbare Tracks wurden markiert.");
                return;
            }
            request.Token.ThrowIfCancellationRequested();
            CurrentTrack=track; Title=track.DisplayTitle; Artist=track.DisplayArtist; Waveform=null;
            PlaybackStatus=_audio.State;
            OnPropertyChanged(nameof(CurrentTrack)); OnPropertyChanged(nameof(CanNext)); OnPropertyChanged(nameof(CanPrevious));
            IsWaveformLoading=true; WaveformStatus="Traktor-STRIPE wird gesucht …";
            WaveformData? waveform=null;
            try { waveform=await _stripes.TryLoadAsync(track,request.Token); }
            catch(IOException) { } catch(UnauthorizedAccessException) { } catch(System.Xml.XmlException) { }
            request.Token.ThrowIfCancellationRequested();
            var info=new FileInfo(path!);
            var fingerprint=new Track { Id=track.Id,LibraryRootId=track.LibraryRootId,RelativePath=track.RelativePath,FileSizeBytes=info.Length,FileModifiedAtUtc=info.LastWriteTimeUtc };
            if(waveform?.HasData!=true)
            {
                waveform=await _cache.TryLoadAsync(fingerprint,request.Token);
                if(waveform?.HasData==true) WaveformStatus="RGB-Waveform · Cratebase-Cache";
                else
                {
                    WaveformStatus="RGB-Waveform wird generiert …";
                    waveform=await _generator.GenerateAsync(path!,request.Token);
                    request.Token.ThrowIfCancellationRequested();
                    if(waveform?.HasData==true)
                    {
                        Waveform=waveform; WaveformStatus="RGB-Waveform · Cratebase";
                        try { await _cache.SaveAsync(fingerprint,waveform,request.Token); }
                        catch(IOException ex) { ReportError?.Invoke("Waveform sichtbar, Cache konnte nicht gespeichert werden: "+ex.Message); }
                        catch(UnauthorizedAccessException ex) { ReportError?.Invoke("Waveform sichtbar, Cache konnte nicht gespeichert werden: "+ex.Message); }
                    }
                }
            }
            else WaveformStatus="Traktor-STRIPE · schreibgeschützt geladen";
            request.Token.ThrowIfCancellationRequested();
            Waveform=waveform;
            if(waveform?.HasData!=true) WaveformStatus="Keine Waveform verfügbar";
        }
        catch(OperationCanceledException) { }
        catch(Exception ex)
        {
            if(ReferenceEquals(_waveformRequest,request)) { WaveformStatus="Waveform nicht verfügbar: "+ex.Message; ReportError?.Invoke(ex.Message); }
        }
        finally { if(ReferenceEquals(_waveformRequest,request)) { IsWaveformLoading=false; _waveformRequest=null; } }
    }
    [RelayCommand] private void Toggle() { if(_audio.State==PlaybackState.Playing) Pause(); else Play(); }
    [RelayCommand] private void Play() { if(CurrentTrack!=null) _audio.Play(); PlaybackStatus=_audio.State; }
    [RelayCommand] private void Pause() { _audio.Pause(); PlaybackStatus=_audio.State; }
    [RelayCommand] private void Stop() { _waveformRequest?.Cancel();_audio.Stop(); PlaybackStatus=_audio.State; }
    [RelayCommand(AllowConcurrentExecutions=true)] private async Task NextAsync() => await StepAsync(1);
    [RelayCommand(AllowConcurrentExecutions=true)] private async Task PreviousAsync() => await StepAsync(-1);
    private async Task StepAsync(int step)
    {
        var index=_queueIndex+step;
        if(index>=0 && index<_queue.Count) await PlayQueueAsync(_queue.ToArray(),index,step);
    }
    [RelayCommand] private void Seek(double fraction) => _audio.Seek(TimeSpan.FromSeconds(Math.Clamp(fraction,0,1)*Duration));
    public void Dispose() { _waveformRequest?.Cancel(); _audio.PositionChanged-=PositionChanged; _audio.PlaybackEnded-=PlaybackEnded; }
}
