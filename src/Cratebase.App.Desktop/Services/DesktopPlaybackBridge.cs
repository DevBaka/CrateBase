using Avalonia.Threading;
using Cratebase.App.Desktop.ViewModels;
using Cratebase.Core.Abstractions;
using Cratebase.Core.Models;

namespace Cratebase.App.Desktop.Services;

public sealed class DesktopPlaybackBridge(WorkspacePlayer player):IRemotePlayback
{
    private readonly SemaphoreSlim _gate=new(1,1);
    public async Task<RemotePlaybackState> ReadAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return await Dispatcher.UIThread.InvokeAsync(()=>
        {
            var start=Math.Min(Math.Max(0,player.QueueIndex-25),Math.Max(0,player.Queue.Count-250));var queue=player.Queue.Skip(start).Take(250).Select(t=>t.Id).ToArray();
            return new RemotePlaybackState(player.CurrentTrack?.Id,player.PlaybackStatus==PlaybackState.Playing,(long)(player.Position*1000),(long)(player.Duration*1000),player.Volume,queue,player.QueueIndex-start);
        });
    }
    public async Task LoadAsync(IReadOnlyList<Track> queue,int index,long positionMs,bool playing,CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var ready=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            System.ComponentModel.PropertyChangedEventHandler handler=(_,e)=>{if(e.PropertyName==nameof(player.CurrentTrack))ready.TrySetResult();};
            Task? load=null;
            await Dispatcher.UIThread.InvokeAsync(()=>{player.PropertyChanged+=handler;load=player.PlayAsync(queue[index],queue);});
            try
            {
                var completed=await Task.WhenAny(ready.Task,load!).WaitAsync(TimeSpan.FromSeconds(20),ct);
                if(!ready.Task.IsCompletedSuccessfully)throw new IOException("Desktop konnte keinen Track laden. Dateiverfügbarkeit prüfen.");
                await Dispatcher.UIThread.InvokeAsync(()=>{player.SeekToMilliseconds(positionMs);if(!playing)player.PauseCommand.Execute(null);});
            }
            finally{await Dispatcher.UIThread.InvokeAsync(()=>player.PropertyChanged-=handler);}
        }
        finally{_gate.Release();}
    }
    public async Task CommandAsync(RemotePlaybackCommand command,CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try{await Dispatcher.UIThread.InvokeAsync(()=>
        {
            if(command.ExpectedTrackId is {} expected&&player.CurrentTrack?.Id!=expected)throw new ArgumentException("Der Desktop-Track hat sich inzwischen geändert.");
            switch(command.Action)
            {
                case "play":player.PlayCommand.Execute(null);break;
                case "pause":player.PauseCommand.Execute(null);break;
                case "toggle":player.ToggleCommand.Execute(null);break;
                case "next":player.NextCommand.Execute(null);break;
                case "previous":player.PreviousCommand.Execute(null);break;
                case "seek":player.SeekToMilliseconds(command.PositionMs??0);break;
                case "volume":player.Volume=command.Volume??player.Volume;break;
                default:throw new ArgumentException("Unbekannter Player-Befehl.");
            }
        });}finally{_gate.Release();}
    }
}
