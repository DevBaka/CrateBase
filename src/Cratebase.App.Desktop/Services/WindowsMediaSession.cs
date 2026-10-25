using Avalonia.Threading;
using Cratebase.App.Desktop.ViewModels;
using Cratebase.Core.Abstractions;
#if WINDOWS
using Windows.Media;
using System.ComponentModel;
#endif

namespace Cratebase.App.Desktop.Services;

/// <summary>Connects the existing LibVLC player to the Windows media session for its HWND.</summary>
public sealed class WindowsMediaSession : IDisposable
{
    public static void RegisterApplication()
    {
#if WINDOWS
        SetCurrentProcessExplicitAppUserModelID("Cratebase");
#endif
    }
#if WINDOWS
    [System.Runtime.InteropServices.DllImport("shell32.dll",CharSet=System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern int SetCurrentProcessExplicitAppUserModelID(string id);
    private readonly WorkspacePlayer _player;
    private readonly SystemMediaTransportControls _controls;
    private bool _disposed;
    private TrackIdentity? _metadata;
    private long _lastSecond=-1;
    private bool _tagMode;
    private readonly Func<string,Task>? _toggleTag;
    private sealed record TrackIdentity(long Id,string Title,string Artist,string Album);
    public WindowsMediaSession(IntPtr hwnd,WorkspacePlayer player,Func<string,Task>? toggleTag=null)
    {
        _player=player;
        _toggleTag=toggleTag;
        _controls=SystemMediaTransportControlsInterop.GetForWindow(hwnd);
        _controls.IsPlayEnabled=true; _controls.IsPauseEnabled=true; _controls.IsStopEnabled=true;
        _controls.ButtonPressed+=ButtonPressed;
        _controls.PlaybackPositionChangeRequested+=PositionRequested;
        _player.PropertyChanged+=PlayerChanged;
        Update();
    }
    public void SetTagMode(bool enabled) { _tagMode=enabled;Update(); }
    private void PlayerChanged(object? sender,PropertyChangedEventArgs args)
    {
        if(args.PropertyName is nameof(WorkspacePlayer.CurrentTrack) or nameof(WorkspacePlayer.PlaybackStatus) or nameof(WorkspacePlayer.Position) or nameof(WorkspacePlayer.Duration) or nameof(WorkspacePlayer.CanNext) or nameof(WorkspacePlayer.CanPrevious))
            Update();
    }
    private void Update()
    {
        if(_disposed) return;
        try
        {
            var track=_player.CurrentTrack;
            _controls.IsEnabled=track!=null;
            _controls.IsNextEnabled=_tagMode?track!=null:_player.CanNext; _controls.IsPreviousEnabled=_tagMode?track!=null:_player.CanPrevious;
            _controls.PlaybackStatus=_player.PlaybackStatus switch { PlaybackState.Playing=>MediaPlaybackStatus.Playing,PlaybackState.Paused=>MediaPlaybackStatus.Paused,_=>MediaPlaybackStatus.Stopped };
            var metadata=track==null?null:new TrackIdentity(track.Id,track.DisplayTitle,track.DisplayArtist,track.Album??"");
            if(metadata!=_metadata)
            {
                _metadata=metadata; _lastSecond=-1;
                var display=_controls.DisplayUpdater;
                display.ClearAll(); display.Type=MediaPlaybackType.Music;
                if(metadata!=null) { display.MusicProperties.Title=metadata.Title; display.MusicProperties.Artist=metadata.Artist; display.MusicProperties.AlbumTitle=metadata.Album; }
                display.Update();
            }
            var second=(long)Math.Max(0,_player.Position);
            if(track!=null && second!=_lastSecond)
            {
                _lastSecond=second;
                var end=TimeSpan.FromSeconds(Math.Max(0,_player.Duration));
                _controls.UpdateTimelineProperties(new SystemMediaTransportControlsTimelineProperties { StartTime=TimeSpan.Zero,MinSeekTime=TimeSpan.Zero,EndTime=end,MaxSeekTime=end,Position=TimeSpan.FromSeconds(Math.Clamp(_player.Position,0,Math.Max(0,_player.Duration))) });
            }
        }
        catch(Exception ex) { _player.ReportError?.Invoke("Windows-Mediensteuerung: "+ex.Message); }
    }
    private void ButtonPressed(SystemMediaTransportControls sender,SystemMediaTransportControlsButtonPressedEventArgs args)
        => Dispatcher.UIThread.Post(async ()=>
        {
            if(_disposed) return;
            try
            {
                if(_tagMode && _player.CurrentTrack!=null && _toggleTag!=null)
                {
                    var field=args.Button switch {SystemMediaTransportControlsButton.Previous=>"club",SystemMediaTransportControlsButton.Play or SystemMediaTransportControlsButton.Pause=>"extended",SystemMediaTransportControlsButton.Next=>"intro",_=>null};
                    if(field!=null) {await _toggleTag(field);return;}
                }
                switch(args.Button)
                {
                    case SystemMediaTransportControlsButton.Play: _player.PlayCommand.Execute(null); break;
                    case SystemMediaTransportControlsButton.Pause: _player.PauseCommand.Execute(null); break;
                    case SystemMediaTransportControlsButton.Stop: _player.StopCommand.Execute(null); break;
                    case SystemMediaTransportControlsButton.Next: await _player.NextCommand.ExecuteAsync(null); break;
                    case SystemMediaTransportControlsButton.Previous: await _player.PreviousCommand.ExecuteAsync(null); break;
                }
            }
            catch(Exception ex) { _player.ReportError?.Invoke("Medientaste: "+ex.Message); }
        });
    private void PositionRequested(SystemMediaTransportControls sender,PlaybackPositionChangeRequestedEventArgs args)
        => Dispatcher.UIThread.Post(()=> { if(!_disposed && _player.Duration>0) _player.SeekCommand.Execute(args.RequestedPlaybackPosition.TotalSeconds/_player.Duration); });
    public void Dispose()
    {
        if(_disposed) return;
        _disposed=true; _player.PropertyChanged-=PlayerChanged;
        _controls.ButtonPressed-=ButtonPressed; _controls.PlaybackPositionChangeRequested-=PositionRequested;
        _controls.IsEnabled=false; _controls.PlaybackStatus=MediaPlaybackStatus.Closed; _controls.DisplayUpdater.ClearAll(); _controls.DisplayUpdater.Update();
    }
#else
    public WindowsMediaSession(IntPtr hwnd,WorkspacePlayer player,Func<string,Task>? toggleTag=null) { }
    public void SetTagMode(bool enabled) { }
    public void Dispose() { }
#endif
}
