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

    private void OnTrackGridDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
        {
            viewModel.PlaySelectedTrackCommand.Execute(null);
        }
    }
}
