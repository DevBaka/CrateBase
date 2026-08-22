using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Cratebase.App.Desktop.ViewModels;

/// <summary>
/// Ein Ordner im Datei-Browser (Dateisystem-Baum unter einem LibraryRoot, unabhängig davon, ob
/// die Dateien darin schon gescannt sind - Kern des Features: sichtbar machen, was auf der
/// Platte liegt, nicht nur, was schon in der DB steht).
///
/// Lazy geladen (Kinder erst beim ersten Aufklappen), sonst wäre ein Baum über eine ~100k-Track-
/// Sammlung sofort ein Full-Filesystem-Walk beim Start. Zwei-Phasen-Trick: bei Konstruktion nur
/// billig prüfen, OB es Unterordner gibt (ein Directory.EnumerateDirectories().Any(), bricht nach
/// dem ersten Treffer ab) und ggf. einen Platzhalter-Knoten einhängen, damit der Expand-Pfeil
/// erscheint; die echten Kinder kommen erst beim tatsächlichen Aufklappen.
/// </summary>
public sealed partial class FileSystemNodeViewModel : ObservableObject
{
    private const string PlaceholderName = "…";
    private bool _childrenLoaded;

    public FileSystemNodeViewModel(string name, string fullPath, long libraryRootId, bool isPlaceholder = false)
    {
        Name = name;
        FullPath = fullPath;
        LibraryRootId = libraryRootId;
        IsPlaceholder = isPlaceholder;

        if (!isPlaceholder && HasSubdirectories(fullPath))
        {
            Children.Add(new FileSystemNodeViewModel(PlaceholderName, fullPath, libraryRootId, isPlaceholder: true));
        }
    }

    public string Name { get; }
    public string FullPath { get; }
    public long LibraryRootId { get; }
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
            Children.Add(new FileSystemNodeViewModel(Path.GetFileName(dir), dir, LibraryRootId));
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
