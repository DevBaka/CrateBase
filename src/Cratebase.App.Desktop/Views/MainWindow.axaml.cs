using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Cratebase.App.Desktop.ViewModels;
using Cratebase.Core.Models;

namespace Cratebase.App.Desktop.Views;

public partial class MainWindow : Window
{
    // Eigene In-Process-Datenformate für Drag&Drop auf die Favoriten-Slots (s. OnFavoriteSlotDrop) -
    // eine Playlist aus dem Baum oder ein Track aus der Liste tragen jeweils nur ihre Id, der Slot löst
    // den Rest über das ViewModel auf (AssignPlaylistToSlot/AddTrackToFavoriteSlotAsync). In-Process
    // reicht, weil hier nie über Prozessgrenzen (andere App) gezogen wird. DataFormat&lt;T&gt; verlangt
    // einen Referenztyp, deshalb Id als String statt long (Roundtrip über long.Parse beim Lesen).
    private static readonly DataFormat<string> PlaylistDragFormat = DataFormat.CreateInProcessFormat<string>("application/x-cratebase-playlist-id");
    private static readonly DataFormat<string> TrackDragFormat = DataFormat.CreateInProcessFormat<string>("application/x-cratebase-track-id");

    // Nur EIN Startpunkt/EventArgs-Paar gleichzeitig nötig - TreeView und DataGrids feuern
    // PointerPressed/PointerMoved nie gleichzeitig füreinander (jeweils eigene, sich nicht
    // überlappende Controls).
    private Point? _dragStartPoint;
    private PointerPressedEventArgs? _dragPressedArgs;

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

    private async void OnExportTraktorClick(object? sender, RoutedEventArgs e)
    {
        var topLevel = GetTopLevel(this);
        if (topLevel is null || DataContext is not MainViewModel viewModel)
        {
            return;
        }

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Playlisten als NML exportieren",
            SuggestedFileName = "Cratebase-Export.nml",
            DefaultExtension = "nml",
            FileTypeChoices = [new FilePickerFileType("Traktor NML") { Patterns = ["*.nml"] }],
        });

        var path = file?.TryGetLocalPath();
        if (path is not null)
        {
            await viewModel.ExportToTraktorCommand.ExecuteAsync(path);
        }
    }

    private void OnTrackGridDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
        {
            viewModel.PlaySelectedTrackCommand.Execute(null);
        }
    }

    /// <summary>ContextMenu-Einträge der Favoriten-Slots (s. MainWindow.axaml) - Click-Event statt
    /// Command-Binding, weil ein ContextMenu als eigener Popup-Zweig kein zuverlässiges $parent[...]-
    /// Binding zurück zum ViewModel erlaubt (s. Kommentar dort). DataContext des MenuItem ist der Slot
    /// (normale Vererbung aus dem ContextMenu-Besitzer), this.DataContext ist das Fenster-ViewModel.</summary>
    private void OnAssignPlaylistToSlotClick(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { DataContext: FavoriteSlotViewModel slot } && DataContext is MainViewModel viewModel)
        {
            viewModel.AssignSelectedPlaylistToSlotCommand.Execute(slot.Index);
        }
    }

    private void OnClearFavoriteSlotClick(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { DataContext: FavoriteSlotViewModel slot } && DataContext is MainViewModel viewModel)
        {
            viewModel.ClearFavoriteSlotCommand.Execute(slot.Index);
        }
    }

    /// <summary>Speichert die Edit-Felder, sobald eines davon den Fokus verliert (Tab, Klick auf ein
    /// anderes Feld, Klick auf einen anderen Track) - kein separater "Speichern"-Button nötig. Im
    /// Code-behind statt per Binding im ViewModel, weil LostFocus als reines UI-Ereignis dort hingehört.</summary>
    private void OnEditFieldLostFocus(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel && viewModel.SaveEditedMetadataCommand.CanExecute(null))
        {
            viewModel.SaveEditedMetadataCommand.Execute(null);
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

    // --- Drag&Drop: Playlist aus dem Baum bzw. Track aus der Liste auf einen Favoriten-Slot ziehen ---
    // Avalonia-Drag&Drop startet aus einer laufenden Pointer-Geste heraus (kein reines XAML/Binding-
    // Konzept), deshalb hier im Code-behind statt im ViewModel - genau wie die Ordnerauswahl-Dialoge
    // oben ein reiner UI-Concern. Avalonia 12 nutzt die neuere DataTransfer/DataFormat-API (DataObject
    // ist deprecated) - CreateInProcessFormat reicht hier völlig, die Daten wandern nie außerhalb des
    // Prozesses (kein Drag zu/von einer anderen Anwendung nötig).

    private void OnPlaylistTreePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _dragStartPoint = e.GetPosition(null);
        _dragPressedArgs = e;
    }

    private async void OnPlaylistTreePointerMoved(object? sender, PointerEventArgs e)
    {
        if (TryBeginDragGesture(e, sender as Visual) is not { } pressedArgs)
        {
            return;
        }

        if (FindDataContext<PlaylistTreeItemViewModel>(e.Source as StyledElement) is not { IsPlaylist: true, Node: { } node })
        {
            return; // Ordner/"Bibliothek"-Wurzel lassen sich nicht auf einen Slot ziehen, nur echte Playlisten
        }

        var transfer = new DataTransfer();
        transfer.Add(DataTransferItem.Create(PlaylistDragFormat, node.Id.ToString(CultureInfo.InvariantCulture)));
        await DragDrop.DoDragDropAsync(pressedArgs, transfer, DragDropEffects.Copy);
    }

    private void OnTrackGridPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _dragStartPoint = e.GetPosition(null);
        _dragPressedArgs = e;
    }

    private async void OnTrackGridPointerMoved(object? sender, PointerEventArgs e)
    {
        if (TryBeginDragGesture(e, sender as Visual) is not { } pressedArgs)
        {
            return;
        }

        if (FindDataContext<Track>(e.Source as StyledElement) is not { IsSynthetic: false } track)
        {
            return; // noch nicht gescannte Platzhalter-Zeilen (s. Track.IsSynthetic) lassen sich nicht ziehen
        }

        var transfer = new DataTransfer();
        transfer.Add(DataTransferItem.Create(TrackDragFormat, track.Id.ToString(CultureInfo.InvariantCulture)));
        await DragDrop.DoDragDropAsync(pressedArgs, transfer, DragDropEffects.Copy);
    }

    /// <summary>Gemeinsame Geste-Erkennung für beide Drag-Quellen: erst ab ein paar Pixeln Bewegung bei
    /// weiterhin gedrückter linker Maustaste als echten Drag werten, sonst würde jeder normale Klick
    /// (Auswahl, Doppelklick zum Abspielen, Baum auf-/zuklappen) sofort einen Drag auslösen. Liefert die
    /// beim ursprünglichen PointerPressed gemerkten EventArgs zurück (DoDragDropAsync verlangt explizit
    /// PointerPressedEventArgs, nicht die laufenden PointerEventArgs von PointerMoved) und feuert pro
    /// Geste nur einmal (setzt die Merker danach zurück).</summary>
    private PointerPressedEventArgs? TryBeginDragGesture(PointerEventArgs e, Visual? relativeTo)
    {
        if (_dragStartPoint is not { } start || _dragPressedArgs is not { } pressed || !e.GetCurrentPoint(relativeTo).Properties.IsLeftButtonPressed)
        {
            return null;
        }

        var current = e.GetPosition(null);
        if (Math.Abs(current.X - start.X) < 4 && Math.Abs(current.Y - start.Y) < 4)
        {
            return null;
        }

        _dragStartPoint = null;
        _dragPressedArgs = null;
        return pressed;
    }

    private static T? FindDataContext<T>(StyledElement? element)
        where T : class
    {
        while (element is not null)
        {
            if (element.DataContext is T match)
            {
                return match;
            }

            element = element.Parent as StyledElement;
        }

        return null;
    }

    private void OnFavoriteSlotDragEnter(object? sender, DragEventArgs e)
    {
        if (sender is not Border border)
        {
            return;
        }

        if (IsAcceptableFavoriteSlotDrag(e))
        {
            border.BorderBrush = this.FindResource("AccentGreenBrush") as IBrush;
            border.BorderThickness = new Thickness(2);
            e.DragEffects = DragDropEffects.Copy;
        }
        else
        {
            e.DragEffects = DragDropEffects.None;
        }
    }

    private void OnFavoriteSlotDragOver(object? sender, DragEventArgs e) =>
        e.DragEffects = IsAcceptableFavoriteSlotDrag(e) ? DragDropEffects.Copy : DragDropEffects.None;

    private void OnFavoriteSlotDragLeave(object? sender, RoutedEventArgs e) => ResetFavoriteSlotDragVisual(sender);

    private async void OnFavoriteSlotDrop(object? sender, DragEventArgs e)
    {
        ResetFavoriteSlotDragVisual(sender);

        if (sender is not Control { DataContext: FavoriteSlotViewModel slot } || DataContext is not MainViewModel viewModel)
        {
            return;
        }

        if (e.DataTransfer.TryGetValue(PlaylistDragFormat) is { } playlistIdText && long.TryParse(playlistIdText, CultureInfo.InvariantCulture, out var playlistId))
        {
            viewModel.AssignPlaylistToSlot(playlistId, slot.Index);
        }
        else if (e.DataTransfer.TryGetValue(TrackDragFormat) is { } trackIdText && long.TryParse(trackIdText, CultureInfo.InvariantCulture, out var trackId))
        {
            await viewModel.AddTrackToFavoriteSlotAsync(trackId, slot.Index);
        }
    }

    private static bool IsAcceptableFavoriteSlotDrag(DragEventArgs e) =>
        e.DataTransfer.Contains(PlaylistDragFormat) || e.DataTransfer.Contains(TrackDragFormat);

    private void ResetFavoriteSlotDragVisual(object? sender)
    {
        if (sender is not Border border)
        {
            return;
        }

        border.BorderBrush = this.FindResource("BorderBrush1") as IBrush;
        border.BorderThickness = new Thickness(1);
    }
}
