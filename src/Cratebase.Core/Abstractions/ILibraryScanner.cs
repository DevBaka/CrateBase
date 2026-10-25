using Cratebase.Core.Models;

namespace Cratebase.Core.Abstractions;

public sealed record ScanProgress(int FilesSeen, int FilesAdded, int FilesUpdated, int FilesUnchanged, int FilesFailed, string? CurrentRelativePath)
{
    public static ScanProgress Empty { get; } = new(0, 0, 0, 0, 0, null);
}

/// <summary>
/// Rekursiver, inkrementeller Scan eines LibraryRoot (s. ARCHITECTURE.md 4.B):
/// unveränderte Dateien (gleiche Größe + mtime wie in der DB) werden übersprungen,
/// nicht mehr vorhandene Dateien als "missing" markiert statt sofort gelöscht.
/// Implementiert in Cratebase.Scanning, orchestriert Tagging + Data, kennt selbst kein SQL.
/// </summary>
public interface ILibraryScanner
{
    Task ScanAsync(LibraryRoot root, IProgress<ScanProgress>? progress, CancellationToken ct = default);
    Task ScanFolderAsync(LibraryRoot root, string absoluteFolder, IProgress<ScanProgress>? progress, CancellationToken ct = default);
}
