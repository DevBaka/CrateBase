using Avalonia.Platform.Storage;

namespace Cratebase.App.Desktop.Views;

public sealed partial class WorkspaceWindow
{
    private HistoryAnalysisWindow? _historyAnalysisWindow;
    private SetArtworkWindow? _artworkWindow;
    private Task OpenSetArtworkAsync()
    {
        if(_artworkWindow!=null){_artworkWindow.Activate();return Task.CompletedTask;}
        var data=Environment.GetEnvironmentVariable("CRATEBASE_DATA_DIR")??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Cratebase");
        var ffmpeg=_settings.RecorderFfmpegPath??Path.Combine(data,"tools","ffmpeg.exe");if(!File.Exists(ffmpeg))ffmpeg="ffmpeg";
        _artworkWindow=new(data,ffmpeg,tracklist:_recorder.TracklistPreview);_artworkWindow.Closed+=(_,_)=>_artworkWindow=null;_artworkWindow.Show(this);return Task.CompletedTask;
    }
    private Task OpenHistoryAnalysisAsync()
    {
        if(_recorder.IsRecording){Status("Die laufende Aufnahme zuerst abschließen.");return Task.CompletedTask;}
        if(_historyAnalysisWindow!=null){_historyAnalysisWindow.Activate();return Task.CompletedTask;}
        var data=Environment.GetEnvironmentVariable("CRATEBASE_DATA_DIR")??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Cratebase");
        var ffmpeg=_settings.RecorderFfmpegPath??Path.Combine(data,"tools","ffmpeg.exe");if(!File.Exists(ffmpeg))ffmpeg="ffmpeg";
        _historyAnalysisWindow=new(Path.Combine(data,"history-analysis"),ffmpeg,markers=>
        {
            if(_recorder.IsRecording)throw new InvalidOperationException("Eine laufende Aufnahme darf nicht durch einen History-Import verändert werden.");
            _recorder.MergeHistoryMarkers(markers);return Task.CompletedTask;
        },()=>{_planningView?.StopPreview();_player.PauseCommand.Execute(null);});
        _historyAnalysisWindow.Closed+=(_,_)=>_historyAnalysisWindow=null;_historyAnalysisWindow.Show(this);return Task.CompletedTask;
    }
    private async Task SelectRecorderFfmpeg()
    {
        if (_recorder.IsRecording) return;
        var files = await StorageProvider.OpenFilePickerAsync(new()
        {
            Title = "Vollständige FFmpeg-Version für den Set Recorder auswählen",
            AllowMultiple = false
        });
        if (files.FirstOrDefault()?.TryGetLocalPath() is not { } path) return;
        _settings.RecorderFfmpegPath = path;
        _audioRecorder.ExecutablePath = path;
        await SaveSettings();
        await _recorder.LoadRecordingDevicesCommand.ExecuteAsync(null);
    }
}
