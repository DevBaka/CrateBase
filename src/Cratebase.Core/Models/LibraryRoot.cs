namespace Cratebase.Core.Models;

/// <summary>
/// Ein überwachtes Wurzelverzeichnis. Mehrere Roots sind von Anfang an vorgesehen
/// (z.B. mehrere Laufwerke), auch wenn das MVP nur eines gleichzeitig scannt.
/// </summary>
public sealed class LibraryRoot
{
    public long Id { get; set; }
    public string Path { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public bool WatchEnabled { get; set; } = true;
    public DateTime? LastScannedAtUtc { get; set; }
}
