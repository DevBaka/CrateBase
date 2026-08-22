namespace Cratebase.App.Desktop.ViewModels;

/// <summary>Eine Datei im aktuell im Datei-Browser ausgewählten Ordner - unabhängig davon, ob sie schon gescannt ist.</summary>
public sealed class FileBrowserEntryViewModel(string name, string fullPath, string relativePath, double sizeMb, bool isScanned)
{
    public string Name { get; } = name;
    public string FullPath { get; } = fullPath;
    public string RelativePath { get; } = relativePath;
    public double SizeMb { get; } = sizeMb;
    public bool IsScanned { get; } = isScanned;
    public string ScannedLabel => IsScanned ? "✓ gescannt" : "– nicht gescannt";
}
