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

    /// <summary>Laufwerksbuchstabe, unter dem Traktor diesen Root kennt (z.B. "M:") - null, solange
    /// noch kein Traktor-Import erfolgreich einen Track unter diesem Root zuordnen konnte. Zusammen
    /// mit <see cref="TraktorDirPrefix"/> automatisch gelernt (s. TraktorPathResolver), für den
    /// NML-Export genutzt (s. ARCHITECTURE.md Abschnitt 20).</summary>
    public string? TraktorVolume { get; set; }

    /// <summary>Pfad-Segmente, mit denen Traktor selbst auf diesen Root zeigt, in Traktors eigener
    /// "/:"-Notation (z.B. "Musik/:Musik") - der Teil des Traktor-Pfads, der beim Import als mit
    /// diesem Root überlappend erkannt wurde.</summary>
    public string? TraktorDirPrefix { get; set; }
}
