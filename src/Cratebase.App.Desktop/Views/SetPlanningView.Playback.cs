using Avalonia.Threading;
using Cratebase.Core.Abstractions;

namespace Cratebase.App.Desktop.Views;
public sealed partial class SetPlanningView
{
    private Task TogglePreviewAsync()
    {
        if(_auditionDeck!=null)return ToggleDeckAsync(_auditionDeck);
        if(_preview==null)return ToggleDeckAsync(_a);
        if(_preview.State==PlaybackState.Stopped){_preview.Play();_auditionPaused=false;return Task.CompletedTask;}
        if(_auditionPaused){_preview.Play();_auditionPaused=false;}else{_preview.Pause();_auditionPaused=true;}return Task.CompletedTask;
    }
    private Deck MakeDeck(string label)=>new(label,MarkChanged,
        deck=>Run(()=>ToggleDeckAsync(deck)),deck=>Run(()=>StartDeckAsync(deck,deck.Row?.Entry.PlayMs??0)),
        deck=>{if(_auditionDeck==deck)StopPreview();},
        (deck,progress)=>_ = Run(()=>SeekDeckAsync(deck,progress)),
        deck=>Run(async()=>{deck.Apply();await SaveCurrentAsync();_status("Transition-Marker gespeichert. Traktor-Cues bleiben unverändert.");}));

    private Task ToggleDeckAsync(Deck deck)
    {
        if(deck.Row?.Track==null)return Task.CompletedTask;
        if(_auditionDeck!=deck||_preview==null)return StartDeckAsync(deck,deck.Row.Entry.PlayMs);
        if(_auditionPaused){_preview.Play();_auditionPaused=false;}else{_preview.Pause();_auditionPaused=true;}
        deck.Transport(true,_auditionPaused,_preview.Position.TotalSeconds);return Task.CompletedTask;
    }
    private Task SeekDeckAsync(Deck deck,double progress)
    {
        if(deck.Row?.Track is not {} track)return Task.CompletedTask;
        var position=(long)(Math.Clamp(progress,0,1)*track.DurationMs);
        if(_auditionDeck!=deck||_preview==null)return StartDeckAsync(deck,position);
        _preview.Seek(TimeSpan.FromMilliseconds(position));deck.Wave.Progress=progress;deck.Transport(true,_auditionPaused,position/1000.0);return Task.CompletedTask;
    }
    private async Task StartDeckAsync(Deck deck,long position)
    {
        if(deck.Row?.Track is not {} track)return;
        var path=await ResolveAsync(track,Token);if(!File.Exists(path))throw new FileNotFoundException("Datei zum Vorhören nicht erreichbar: "+track.DisplayTitle);
        Token.ThrowIfCancellationRequested();StopPreview();_pauseLibrary();var audio=_preview=_audioFactory();_auditionDeck=deck;_auditionPaused=false;
        _auditionSeek=Math.Clamp(position,0,Math.Max(0,track.DurationMs-1));audio.Volume=_auditionSeek>0?0:.8f;
        audio.PositionChanged+=(_,_)=>Dispatcher.UIThread.Post(()=>
        {
            if(!ReferenceEquals(_preview,audio))return;
            if(_auditionSeek is {} seek&&audio.Duration>TimeSpan.Zero){_auditionSeek=null;audio.Seek(TimeSpan.FromMilliseconds(seek));audio.Volume=.8f;return;}
            deck.Wave.Progress=audio.Position.TotalMilliseconds/Math.Max(1,track.DurationMs);deck.Transport(true,_auditionPaused,audio.Position.TotalSeconds);
        });
        audio.PlaybackEnded+=(_,_)=>Dispatcher.UIThread.Post(()=>{if(ReferenceEquals(_preview,audio))StopPreview();});
        audio.Load(path);audio.Play();deck.Transport(true,false,position/1000.0);_status("Vorhören: "+track.DisplayArtist+" · "+track.DisplayTitle+" · Waveform-Klick zum Spulen; farbige Marker ziehen zum Bearbeiten.");
    }
}
