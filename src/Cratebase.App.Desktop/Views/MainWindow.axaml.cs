using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Cratebase.App.Desktop.ViewModels;

namespace Cratebase.App.Desktop.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Bewusst im Code-behind statt im ViewModel: der Ordnerauswahl-Dialog braucht
    /// IStorageProvider vom TopLevel-Fenster, ein reiner Avalonia-UI-Concern. Das ViewModel
    /// bekommt danach nur noch den fertigen Pfad über ScanFolderCommand.
    /// </summary>
    private async void OnAddFolderClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var topLevel = GetTopLevel(this);
        if (topLevel is null || DataContext is not MainViewModel viewModel)
        {
            return;
        }

        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Musik-Ordner auswählen",
            AllowMultiple = false,
        });

        var folder = folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
        if (folder is not null)
        {
            await viewModel.ScanFolderCommand.ExecuteAsync(folder);
        }
    }

    private async void OnImportTraktorClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var topLevel = GetTopLevel(this);
        if (topLevel is null || DataContext is not MainViewModel viewModel)
        {
            return;
        }

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Traktor collection.nml auswählen",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("Traktor Collection") { Patterns = ["*.nml"] }],
        });

        var file = files.Count > 0 ? files[0].TryGetLocalPath() : null;
        if (file is not null)
        {
            await viewModel.ImportTraktorCommand.ExecuteAsync(file);
        }
    }

    private void OnTrackGridDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
        {
            viewModel.PlaySelectedTrackCommand.Execute(null);
        }
    }

    /// <summary>Doppelklick im Datei-Browser lädt den Song direkt (statt nur über den ▶-Button je Zeile).</summary>
    private void OnFolderFileDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is MainViewModel { SelectedFolderFile: { } entry } viewModel)
        {
            viewModel.PlayFolderFileCommand.Execute(entry);
        }
    }

    private async void OnAddQuickAccessClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var topLevel = GetTopLevel(this);
        if (topLevel is null || DataContext is not MainViewModel viewModel)
        {
            return;
        }

        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Schnellzugriff-Ordner auswählen",
            AllowMultiple = false,
        });

        var folder = folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
        if (folder is not null)
        {
            await viewModel.AddQuickAccessCommand.ExecuteAsync(folder);
        }
    }
}
