using Avalonia.Input.Platform;
using Avalonia.Input;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Cratebase.App.Desktop.ViewModels;
using Cratebase.Audio;
namespace Cratebase.App.Desktop.Views;
public partial class SetRecorderView : UserControl
{
    public Func<Task>? SelectFfmpeg { get; set; }
    public SetRecorderView() => AvaloniaXamlLoader.Load(this);
    private async void OnSelectFfmpegClick(object? sender, RoutedEventArgs e)
    { if (SelectFfmpeg != null) await Run(SelectFfmpeg); }
    private async Task Run(Func<Task> action)
    {
        try { await action(); }
        catch(Exception ex) { if(DataContext is MainViewModel vm) vm.StatusMessage="Set Recorder: "+ex.Message; }
    }
    private async void OnStartRecordingClick(object? sender,RoutedEventArgs e) => await Run(OnStartRecordingClickAsync);
    private async void OnSelectAutoDetectFileClick(object? sender,RoutedEventArgs e) => await Run(OnSelectAutoDetectFileClickAsync);
    private async void OnCopyTracklistClick(object? sender,RoutedEventArgs e) => await Run(OnCopyTracklistClickAsync);
    private async void OnSaveTracklistClick(object? sender,RoutedEventArgs e) => await Run(OnSaveTracklistClickAsync);
    private async Task OnStartRecordingClickAsync()
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null || DataContext is not MainViewModel viewModel)
        {
            return;
        }

        var extension = FfmpegAudioRecorder.GetFileExtension(viewModel.SelectedRecordingFormat);
        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Set-Aufnahme speichern unter",
            SuggestedFileName = $"Set-{DateTime.Now:yyyy-MM-dd_HH-mm}.{extension}",
            DefaultExtension = extension,
            FileTypeChoices = [new FilePickerFileType(extension.ToUpperInvariant()) { Patterns = [$"*.{extension}"] }],
        });

        var path = file?.TryGetLocalPath();
        if (path is not null)
        {
            await viewModel.StartRecordingCommand.ExecuteAsync(path);
        }
    }

    private async Task OnSelectAutoDetectFileClickAsync()
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null || DataContext is not MainViewModel viewModel)
        {
            return;
        }

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Now-Playing-/History-Datei auswählen",
            AllowMultiple = false,
        });

        var path = files.Count > 0 ? files[0].TryGetLocalPath() : null;
        if (path is not null)
        {
            viewModel.AutoDetectFilePath = path;
        }
    }

    private async Task OnCopyTracklistClickAsync()
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.Clipboard is null || DataContext is not MainViewModel viewModel)
        {
            return;
        }

        await topLevel.Clipboard.SetTextAsync(viewModel.TracklistPreview);
    }

    private async Task OnSaveTracklistClickAsync()
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null || DataContext is not MainViewModel viewModel)
        {
            return;
        }

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Tracklist speichern unter",
            SuggestedFileName = "Tracklist.txt",
            DefaultExtension = "txt",
            FileTypeChoices = [new FilePickerFileType("Textdatei") { Patterns = ["*.txt"] }],
        });

        var path = file?.TryGetLocalPath();
        if (path is not null)
        {
            await File.WriteAllTextAsync(path, viewModel.TracklistPreview);
        }
    }

    /// <summary>Artist/Titel eines Tracklist-Markers verloren den Fokus - Vorschau neu erzeugen
    /// (dieselbe LostFocus-Konvention wie OnEditFieldLostFocus/OnMarkerFieldLostFocus im Tag-Editor).</summary>
    private void OnMarkerFieldLostFocus(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel && viewModel.RefreshTracklistPreviewCommand.CanExecute(null))
        {
            viewModel.RefreshTracklistPreviewCommand.Execute(null);
        }
    }
}




