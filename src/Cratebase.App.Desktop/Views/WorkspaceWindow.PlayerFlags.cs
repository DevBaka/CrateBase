using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Cratebase.Core.Models;
using Track=Cratebase.Core.Models.Track;

namespace Cratebase.App.Desktop.Views;

public sealed partial class WorkspaceWindow
{
    private readonly Dictionary<string,ToggleButton> _playerFlags=new();
    private Track? _observedPlayerTrack;
    private readonly SemaphoreSlim _playerFlagGate=new(1,1);
    private void AddPlayerFlags(Panel transport)
    {
        foreach(var flag in new[]{("club","Club"),("extended","Extended"),("intro","Intro"),("trash","Trash")})
        {
            var button=new ToggleButton {Content=flag.Item2,Padding=new Thickness(9,7),IsEnabled=false};
            button.Click+=async (_,_)=>await Safe(()=>TogglePlayerFlag(flag.Item1));
            ToolTip.SetTip(button,flag.Item2+" für den geladenen Player-Track umschalten");
            _playerFlags.Add(flag.Item1,button);transport.Children.Add(button);
        }
        _player.PropertyChanged+=(_,e)=>
        {
            if(e.PropertyName!=nameof(_player.CurrentTrack))return;
            if(_observedPlayerTrack!=null)_observedPlayerTrack.PropertyChanged-=PlayerTrackFlagsChanged;
            _observedPlayerTrack=_player.CurrentTrack;
            if(_observedPlayerTrack!=null)_observedPlayerTrack.PropertyChanged+=PlayerTrackFlagsChanged;
            RefreshPlayerFlags();
        };
        RefreshPlayerFlags();
    }
    private void PlayerTrackFlagsChanged(object? sender,System.ComponentModel.PropertyChangedEventArgs e)=>RefreshPlayerFlags();
    private void RefreshPlayerFlags()
    {
        foreach(var pair in _playerFlags)
        {
            var track=_player.CurrentTrack;
            var value=track!=null && (pair.Key switch {"club"=>track.ClubReady,"extended"=>track.Extended,"trash"=>track.Trash,_=>track.Intro});
            pair.Value.IsEnabled=track!=null;pair.Value.IsChecked=value;
            pair.Value.Background=value?Accent:Surface;pair.Value.Foreground=value?Brushes.Black:Brushes.White;
        }
    }
    private async Task TogglePlayerFlag(string field)
    {
        var loaded=_player.CurrentTrack;
        await _playerFlagGate.WaitAsync();
        try
        {
            if(loaded==null) {Status("Kein Track im Player geladen.");return;}
            var track=await _tracks.GetByIdAsync(loaded.Id);
            if(track==null) {Status("Der geladene Track ist nicht mehr in der Collection.");return;}
            var value=field switch {"club"=>track.ClubReady,"extended"=>track.Extended,"intro"=>track.Intro,"trash"=>track.Trash,_=>throw new ArgumentException("Unbekannte Markierung.")};
            await SetFlag([track.Id],field,!value);
        }
        finally {RefreshPlayerFlags();_playerFlagGate.Release();}
    }
}
