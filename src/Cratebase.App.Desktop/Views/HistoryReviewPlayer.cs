using System.ComponentModel;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using Cratebase.Analysis;
using Cratebase.App.Desktop.Controls;
using Cratebase.Audio;
using Cratebase.Core.Abstractions;
using Cratebase.Core.Models;

namespace Cratebase.App.Desktop.Views;

/// <summary>One playback session shared by the docked and detached review views.</summary>
public sealed class HistoryReviewPlayer : IDisposable
{
    private readonly Window _owner;
    private readonly Action _pauseLibrary;
    private readonly Action<string> _status;
    private readonly Func<IAudioPlayer>? _audioFactory;
    private readonly string _directory,_ffmpeg;
    private readonly CancellationTokenSource _lifetime=new();
    private CancellationTokenSource? _waveJob;
    private readonly ReviewVideoSurface _surface=new();
    private readonly Border _dockHost=new(){Background=Brushes.Black,MinHeight=120};
    private Border? _popupHost;
    private Window? _popup;
    private readonly List<Transport> _transports=[];
    private Transport? _popupTransport;
    private IAudioPlayer? _audio;
    private IReadOnlyList<HistoryAudioMatch> _matches=[];
    private string _path="";
    private double _duration,_position,_volume=.8;
    private double? _pendingSeek;
    private bool _seekIssued,_paused,_showVideo,_video,_disposed;
    private int _epoch;
    private WaveformData? _waveform;
    private HistoryAudioMatch? _selected;
    public Control Controls {get;}
    public Control VideoPanel {get;}
    public event Action<bool>? DockVisibilityChanged;
    public IntPtr VideoHandle=>_surface.VideoHandle;
    public bool IsDetached=>_popup!=null;
    public PlaybackState State=>_audio?.State??PlaybackState.Stopped;
    public double PositionSeconds=>_position;
    public bool HasVideoOutput=>_audio is LibVlcAudioPlayer vlc&&vlc.HasVideoOutput;

    public HistoryReviewPlayer(Window owner,string directory,string ffmpeg,Action pauseLibrary,Action<string> status,Func<IAudioPlayer>? audioFactory=null)
    {
        _owner=owner;_directory=directory;_ffmpeg=ffmpeg;_pauseLibrary=pauseLibrary;_status=status;_audioFactory=audioFactory;
        _surface.HandleChanged+=handle=>{if(_audio is LibVlcAudioPlayer vlc&&OperatingSystem.IsWindows())vlc.VideoWindowHandle=handle;};
        _dockHost.Child=_surface;
        var dock=new Grid{RowDefinitions=new("Auto,*"),Margin=new Thickness(6,0,0,0),RowSpacing=6};
        var header=new WrapPanel();header.Children.Add(new TextBlock{Text="Video · Aufnahme",VerticalAlignment=VerticalAlignment.Center});
        header.Children.Add(ActionButton("↗ Pop-out",()=>{DetachVideo();return Task.CompletedTask;}));
        header.Children.Add(ActionButton("Einklappen",()=>{SetVideoVisible(false);return Task.CompletedTask;}));dock.Children.Add(header);
        Grid.SetRow(_dockHost,1);dock.Children.Add(_dockHost);VideoPanel=dock;
        Controls=BuildTransport(false).Panel;Refresh();
    }
    public void Configure(string path,double duration,IReadOnlyList<HistoryAudioMatch> matches)
    {
        foreach(var match in _matches)match.PropertyChanged-=MarkerChanged;
        _matches=matches;foreach(var match in _matches)match.PropertyChanged+=MarkerChanged;
        if(!string.Equals(_path,path,StringComparison.OrdinalIgnoreCase))
        {
            Stop();_waveJob?.Cancel();_waveform=null;_path=path;_duration=Math.Max(0,duration);
            _video=new[]{".mp4",".mkv",".mov",".m4v",".webm",".avi"}.Contains(Path.GetExtension(path).ToLowerInvariant());
            SetVideoVisible(_video&&File.Exists(path));
            if(!_video&&File.Exists(path)){_waveJob=CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);_ = LoadWaveformAsync(path,_waveJob.Token);}
        }
        else _duration=Math.Max(0,duration);
        Refresh();
    }
    private void MarkerChanged(object? sender,PropertyChangedEventArgs args)
    {if(args.PropertyName is nameof(HistoryAudioMatch.Start) or nameof(HistoryAudioMatch.Include))Refresh();}
    public void Select(HistoryAudioMatch? match){_selected=match;Refresh();}
    private Transport BuildTransport(bool popup)
    {
        var panel=new StackPanel{Spacing=5};var wave=new WaveformView{Name=popup?"HistoryPopupTimeline":"HistoryReviewTimeline",Height=65,HorizontalAlignment=HorizontalAlignment.Stretch};
        wave.SeekCommand=new AsyncRelayCommand<double>(progress=>TryAsync(()=>SeekAsync(progress*_duration)));
        wave.PointerMoved+=(_,e)=>
        {
            var seconds=Math.Clamp(e.GetPosition(wave).X/Math.Max(1,wave.Bounds.Width),0,1)*_duration;
            var marker=_matches.Where(m=>m.Start<=seconds).OrderByDescending(m=>m.Start).FirstOrDefault();
            ToolTip.SetTip(wave,HistoryAudioReport.Timestamp(seconds)+(marker==null?"":" · "+marker.Artist+" – "+marker.Title));
        };panel.Children.Add(wave);
        var row=new WrapPanel();var play=ActionButton("▶ Play / Pause",ToggleAsync);play.Name=popup?"HistoryPopupPlay":"HistoryReviewPlay";row.Children.Add(play);
        var stop=ActionButton("■ Stop",()=>{Stop();return Task.CompletedTask;});stop.Name="HistoryReviewStop";row.Children.Add(stop);
        row.Children.Add(ActionButton("⏮ Marker",()=>JumpMarkerAsync(-1)));row.Children.Add(ActionButton("−5 s",()=>SeekAsync(Math.Max(0,_position-5))));
        row.Children.Add(ActionButton("+5 s",()=>SeekAsync(_position+5)));row.Children.Add(ActionButton("Marker ⏭",()=>JumpMarkerAsync(1)));
        var clock=new TextBlock{VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(8,0),FontFamily=FontFamily.Parse("Consolas")};row.Children.Add(clock);
        var volume=new Slider{Minimum=0,Maximum=100,Value=_volume*100,Width=90};ToolTip.SetTip(volume,"Lautstärke");
        volume.PropertyChanged+=(_,e)=>{if(e.Property==Slider.ValueProperty){_volume=volume.Value/100;if(_audio!=null&&_pendingSeek==null)_audio.Volume=(float)_volume;}};row.Children.Add(volume);
        var video=ActionButton("Video ein-/ausklappen",()=>{SetVideoVisible(!_showVideo);return Task.CompletedTask;});row.Children.Add(video);
        panel.Children.Add(row);var current=new TextBlock{TextTrimming=TextTrimming.CharacterEllipsis};panel.Children.Add(current);
        var view=new Transport(panel,wave,clock,current,play,video);_transports.Add(view);return view;
    }
    private Button ActionButton(string label,Func<Task> action)
    {var button=new Button{Content=label,Margin=new Thickness(2),Padding=new Thickness(7,4)};button.Click+=async(_,_)=>await TryAsync(action);return button;}
    private async Task TryAsync(Func<Task> action)
    {try{if(!_disposed)await action();}catch(OperationCanceledException){}catch(Exception ex){if(!_disposed)_status("Review-Player: "+ex.Message);}}
    public async Task PlayAtAsync(double seconds)
    {
        if(_disposed)return;
        if(!File.Exists(_path))throw new FileNotFoundException("Aufnahme nicht erreichbar.",_path);
        if(_audio!=null){SetSeek(seconds);_audio.Play();_paused=false;Refresh();return;}
        var epoch=++_epoch;var path=_path;
        if(_video&&OperatingSystem.IsWindows()&&_audioFactory==null)
        {
            SetVideoVisible(true);
            using var timeout=CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);timeout.CancelAfter(TimeSpan.FromSeconds(5));
            try{await _surface.WaitForHandleAsync(timeout.Token);}catch(OperationCanceledException) when(!_lifetime.IsCancellationRequested){throw new IOException("Video-Ausgabe konnte nicht geöffnet werden. Video erneut ausklappen.");}
        }
        if(_disposed||epoch!=_epoch||path!=_path)return;
        _pauseLibrary();var audio=_audio=_audioFactory?.Invoke()??new LibVlcAudioPlayer(_video&&OperatingSystem.IsWindows());
        if(audio is LibVlcAudioPlayer vlc&&_video&&OperatingSystem.IsWindows())vlc.VideoWindowHandle=_surface.VideoHandle;
        audio.PositionChanged+=(_,_)=>Dispatcher.UIThread.Post(()=>OnPosition(audio));
        audio.PlaybackEnded+=(_,_)=>Dispatcher.UIThread.Post(()=>{if(ReferenceEquals(_audio,audio)){_paused=true;Refresh();}});
        _pendingSeek=seconds>0?Math.Clamp(seconds,0,Math.Max(0,_duration-.1)):null;_seekIssued=false;
        audio.Volume=_pendingSeek==null?(float)_volume:0;audio.Load(path);audio.Play();_paused=false;
        _status("Vorhören: "+Path.GetFileName(path)+" · durchgehende Wiedergabe; Timeline und Marker anklicken zum Springen.");Refresh();
    }
    private void OnPosition(IAudioPlayer audio)
    {
        if(_disposed||!ReferenceEquals(audio,_audio))return;
        if(audio.Duration.TotalSeconds>0)_duration=audio.Duration.TotalSeconds;
        if(_pendingSeek is {} target&&audio.Duration>TimeSpan.Zero)
        {
            if(!_seekIssued){_seekIssued=true;audio.Seek(TimeSpan.FromSeconds(target));return;}
            if(Math.Abs(audio.Position.TotalSeconds-target)<2){_pendingSeek=null;audio.Volume=(float)_volume;}
        }
        _position=audio.Position.TotalSeconds;Refresh();
    }
    private void SetSeek(double seconds)
    {
        seconds=Math.Clamp(seconds,0,Math.Max(0,_duration-.1));_position=seconds;
        if(_audio!=null)
        {
            if(_audio.Duration>TimeSpan.Zero){_pendingSeek=null;_audio.Seek(TimeSpan.FromSeconds(seconds));_audio.Volume=(float)_volume;}
            else{_pendingSeek=seconds;_seekIssued=false;_audio.Volume=0;}
        }
        Refresh();
    }
    public async Task SeekAsync(double seconds)
    {if(_audio==null)await PlayAtAsync(seconds);else SetSeek(seconds);}
    public async Task ToggleAsync()
    {
        if(_audio==null){await PlayAtAsync(_position);return;}
        if(_paused||_audio.State!=PlaybackState.Playing){_audio.Play();_paused=false;}else{_audio.Pause();_paused=true;}Refresh();
    }
    private Task JumpMarkerAsync(int direction)
    {
        var times=_matches.Where(m=>m.Include).Select(m=>m.Start).Distinct().Order().ToArray();
        var target=direction<0?times.Where(t=>t<_position-1).DefaultIfEmpty(0).Last():times.Where(t=>t>_position+1).DefaultIfEmpty(_position).First();
        return SeekAsync(target);
    }
    public void Stop()
    {
        ++_epoch;var audio=_audio;_audio=null;_pendingSeek=null;_seekIssued=false;_paused=false;_position=0;
        audio?.Stop();audio?.Dispose();Refresh();
    }
    private void Refresh()
    {
        var ordered=_matches.Where(m=>m.Include||m.Confidence=="high").OrderBy(m=>m.Start).ToArray();
        var current=_selected??ordered.LastOrDefault(m=>m.Start<=_position&&m.End>_position);
        var markers=ordered.Select(m=>new WaveformMarker(Math.Clamp(m.Start/Math.Max(1,_duration),0,1),ReferenceEquals(m,_selected)?"Auswahl":"",ReferenceEquals(m,_selected)?0xFFFFDA6Au:m.Confidence=="high"?0xFF39D58Au:0xFFF4A958u)).ToArray();
        foreach(var view in _transports)
        {
            view.Wave.Waveform=_waveform;view.Wave.Markers=markers;view.Wave.Progress=_position/Math.Max(1,_duration);
            view.Clock.Text=HistoryAudioReport.Timestamp(_position)+" / "+HistoryAudioReport.Timestamp(_duration);
            view.Current.Text=current==null?"Timeline anklicken · grün: Audio-Treffer · gelb: ausgewählter Einsatz":current.Artist+" – "+current.Title;
            view.Play.Content=_audio!=null&&!_paused&&_audio.State==PlaybackState.Playing?"⏸ Pause":"▶ Play";view.Video.IsEnabled=_video;
        }
    }
    public void SetVideoVisible(bool visible)
    {
        _showVideo=visible&&_video;
        if(!_showVideo&&_popup!=null)_popup.Close();
        DockVisibilityChanged?.Invoke(_showVideo&&_popup==null);
    }
    public void DetachVideo()
    {
        if(!_video||_disposed)return;
        if(_popup!=null){_popup.Activate();return;}
        _showVideo=true;_dockHost.Child=null;
        var popup=_popup=new Window{Title="Cratebase · Review-Video",Width=1000,Height=700,MinWidth=640,MinHeight=420,WindowStartupLocation=WindowStartupLocation.CenterOwner};
        var layout=new Grid{RowDefinitions=new("*,Auto"),Margin=new Thickness(8),RowSpacing=8};
        _popupHost=new Border{Background=Brushes.Black,Child=_surface};layout.Children.Add(_popupHost);
        _popupTransport=BuildTransport(true);var transport=new StackPanel{Spacing=5};
        transport.Children.Add(ActionButton("↙ Wieder rechts andocken",()=>{popup.Close();return Task.CompletedTask;}));transport.Children.Add(_popupTransport.Panel);Grid.SetRow(transport,1);layout.Children.Add(transport);popup.Content=layout;
        popup.Closed+=(_,_)=>
        {
            _popupHost!.Child=null;_popupHost=null;_popup=null;
            if(_popupTransport!=null)_transports.Remove(_popupTransport);_popupTransport=null;
            if(!_disposed)_dockHost.Child=_surface;
            DockVisibilityChanged?.Invoke(!_disposed&&_showVideo);Refresh();
        };
        DockVisibilityChanged?.Invoke(false);popup.Show(_owner);Refresh();
    }
    public void RedockVideo()=>_popup?.Close();
    private async Task LoadWaveformAsync(string path,CancellationToken ct)
    {
        foreach(var view in _transports)view.Wave.IsLoading=true;
        try
        {
            var info=new FileInfo(path);var key=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"rgb-v1|{path}|{info.Length}|{info.LastWriteTimeUtc.Ticks}")));
            var folder=Path.Combine(_directory,"review-waveforms");var file=Path.Combine(folder,key+".json");
            WaveformData? data=null;
            if(File.Exists(file))try{data=JsonSerializer.Deserialize<WaveformData>(await File.ReadAllTextAsync(file,ct));}catch(JsonException){}
            data??=await new FfmpegWaveformGenerator(_ffmpeg).GenerateAsync(path,ct);
            ct.ThrowIfCancellationRequested();
            if(data!=null){Directory.CreateDirectory(folder);await File.WriteAllTextAsync(file,JsonSerializer.Serialize(data),ct);}
            if(!_disposed&&path==_path){_waveform=data;Refresh();}
        }
        catch(OperationCanceledException){}
        catch(Exception ex){if(!_disposed&&!ct.IsCancellationRequested)_status("Waveform nicht verfügbar; Wiedergabe und Timeline bleiben nutzbar: "+ex.Message);}
        finally{if(!_disposed&&path==_path)foreach(var view in _transports)view.Wave.IsLoading=false;}
    }
    public void Dispose()
    {
        if(_disposed)return;_disposed=true;_lifetime.Cancel();_waveJob?.Cancel();Stop();_popup?.Close();
        foreach(var match in _matches)match.PropertyChanged-=MarkerChanged;
    }
    private sealed record Transport(StackPanel Panel,WaveformView Wave,TextBlock Clock,TextBlock Current,Button Play,Button Video);
}
