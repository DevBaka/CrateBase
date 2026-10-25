using Avalonia.Platform.Storage;

namespace Cratebase.App.Desktop.Views;

public sealed partial class WorkspaceWindow
{
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
