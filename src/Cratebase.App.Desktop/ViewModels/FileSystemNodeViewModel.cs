using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Cratebase.App.Desktop.ViewModels;

/// <summary>
/// Ein Ordner im Sidebar-Baum, egal ob unter einer schon gescannten Bibliothek (LibraryRoot) oder
/// einem noch nicht gescannten Schnellzugriff-Ordner - kennt selbst keine LibraryRootId mehr,
/// welche DB-Bibliothek (falls überhaupt) zu einem Pfad gehört, ermittelt MainViewModel bei
/// Bedarf per Pfad-Präfix-Abgleich (s. FindLibraryRootForPath). Das macht diesen Knoten für
/// beliebige Schnellzugriff-Pfade wiederverwendbar, ohne dass sie vorher als LibraryRoot
/// registriert sein müssen.
///
/// Lazy geladen (Kinder erst beim ersten Aufklappen) - sonst wäre ein Baum über eine ~100k-Track-
/// Sammlung sofort ein Full-Filesystem-Walk beim Start. Zwei-Phasen-Trick: bei Konstruktion nur
/// billig prüfen, OB es Unterordner gibt (Directory.EnumerateDirectories().Any(), bricht nach dem
/// ersten Treffer ab) und ggf. einen Platzhalter-Knoten einhängen, damit der Expand-Pfeil
/// erscheint; die echten Kinder kommen erst beim tatsächlichen Aufklappen.
/// </summary>
public sealed partial class FileSystemNodeViewModel : ObservableObject
{
    private const string PlaceholderName = "…";
    private bool _childrenLoaded;

    public FileSystemNodeViewModel(string name, string fullPath, bool isPlaceholder = false)
    {
        Name = name;
        FullPath = fullPath;
        IsPlaceholder = isPlaceholder;

        if (!isPlaceholder && HasSubdirectories(fullPath))
        {
            Children.Add(new FileSystemNodeViewModel(PlaceholderName, fullPath, isPlaceholder: true));
        }
    }

    public string Name { get; }
    public string FullPath { get; }
    public bool IsPlaceholder { get; }

    public ObservableCollection<FileSystemNodeViewModel> Children { get; } = [];

    [ObservableProperty]
    public partial bool IsExpanded { get; set; }

    partial void OnIsExpandedChanged(bool value)
    {
        if (value && !_childrenLoaded && !IsPlaceholder)
        {
            _childrenLoaded = true;
            _ = LoadChildrenAsync();
        }
    }

    private async Task LoadChildrenAsync()
    {
        var directories = await Task.Run(() => SafeEnumerateDirectories(FullPath));
        Children.Clear(); // entfernt den Platzhalter
        foreach (var dir in directories)
        {
            Children.Add(new FileSystemNodeViewModel(Path.GetFileName(dir), dir));
        }
    }

    private static bool HasSubdirectories(string path)
    {
        try
        {
            return Directory.EnumerateDirectories(path).Any();
        }
        catch (Exception)
        {
            return false; // kein Zugriff o.ä. - dann eben kein Expand-Pfeil
        }
    }

    private static List<string> SafeEnumerateDirectories(string path)
    {
        try
        {
            return Directory.EnumerateDirectories(path)
                .OrderBy(d => d, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (Exception)
        {
            return [];
        }
    }
}
