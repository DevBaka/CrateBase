using Cratebase.Core.Abstractions;
using LibVLCSharp.Shared;

namespace Cratebase.Audio;

/// <summary>
/// IAudioPlayer-Implementierung über LibVLC (s. IAudioPlayer-Doku für die Begründung).
/// <see cref="global::LibVLCSharp.Shared.Core.Initialize"/> muss einmal pro Prozess aufgerufen
/// worden sein, bevor diese Klasse instanziiert wird (passiert in Cratebase.App.Desktop/Program.cs).
///
/// Wichtig für Aufrufer: <see cref="PlaybackEnded"/> und <see cref="PositionChanged"/> feuern auf
/// einem internen LibVLC-Thread, nicht auf dem UI-Thread – Handler müssen selbst zurück auf den
/// UI-Thread marshalen (z.B. via Avalonia Dispatcher.UIThread.Post).
/// </summary>
public sealed class LibVlcAudioPlayer : IAudioPlayer
{
    private readonly LibVLC _libVlc = new();
    private readonly MediaPlayer _mediaPlayer;
    private Media? _currentMedia;
    private bool _disposed;
    private readonly bool _enableVideo;

    public LibVlcAudioPlayer(bool enableVideo=false)
    {
        _enableVideo=enableVideo;
        _mediaPlayer = new MediaPlayer(_libVlc);
        _mediaPlayer.EnableMouseInput=false;_mediaPlayer.EnableKeyInput=false;
        _mediaPlayer.EndReached += (_, _) => PlaybackEnded?.Invoke(this, EventArgs.Empty);
        _mediaPlayer.TimeChanged += (_, _) => PositionChanged?.Invoke(this, EventArgs.Empty);
    }

    public PlaybackState State => _mediaPlayer.State switch
    {
        VLCState.Playing => PlaybackState.Playing,
        VLCState.Paused => PlaybackState.Paused,
        _ => PlaybackState.Stopped,
    };

    public TimeSpan Position => TimeSpan.FromMilliseconds(Math.Max(0, _mediaPlayer.Time));

    public TimeSpan Duration => TimeSpan.FromMilliseconds(Math.Max(0, _mediaPlayer.Length));
    public IntPtr VideoWindowHandle {get=>_mediaPlayer.Hwnd;set=>_mediaPlayer.Hwnd=value;}
    public bool HasVideoOutput=>_mediaPlayer.VoutCount>0;

    public float Volume
    {
        get => _mediaPlayer.Volume / 100f;
        set => _mediaPlayer.Volume = (int)Math.Clamp(value * 100f, 0f, 100f);
    }

    public event EventHandler? PlaybackEnded;
    public event EventHandler? PositionChanged;

    public void Load(string absoluteFilePath)
    {
        _mediaPlayer.Stop();
        _currentMedia?.Dispose();
        _currentMedia = new Media(_libVlc, absoluteFilePath, FromType.FromPath);
        if(!_enableVideo)_currentMedia.AddOption(":no-video");
        _mediaPlayer.Media = _currentMedia;
    }

    public void Play() => _mediaPlayer.Play();

    public void Pause() => _mediaPlayer.Pause();

    public void Stop() => _mediaPlayer.Stop();

    public void Seek(TimeSpan position) => _mediaPlayer.Time = (long)position.TotalMilliseconds;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _mediaPlayer.Dispose();
        _currentMedia?.Dispose();
        _libVlc.Dispose();
    }
}
