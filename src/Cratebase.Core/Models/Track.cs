namespace Cratebase.Core.Models;

/// <summary>
/// Ein einzelner Track in der Bibliothek. Entspricht der `track`-Tabelle
/// (siehe ARCHITECTURE.md Abschnitt 2.2 – für dieses MVP auf die Felder
/// reduziert, die Scan/Player/Import tatsächlich brauchen; BPM/Key/Rating
/// sind schon vorgesehen, da Tag-Editor und Smart Playlists sie später
/// direkt nutzen).
/// </summary>
public sealed class Track
{
    public long Id { get; set; }
    public long LibraryRootId { get; set; }

    /// <summary>Pfad relativ zum LibraryRoot, nicht absolut – der Root kann verschoben werden.</summary>
    public string RelativePath { get; set; } = string.Empty;

    public long FileSizeBytes { get; set; }
    public DateTime FileModifiedAtUtc { get; set; }
    public string Format { get; set; } = string.Empty;
    public int DurationMs { get; set; }
    public int? BitrateKbps { get; set; }
    public int? SampleRateHz { get; set; }
    public int? Channels { get; set; }

    public string? Title { get; set; }
    public string? Artist { get; set; }
    public string? Album { get; set; }
    public string? AlbumArtist { get; set; }
    public int? TrackNumber { get; set; }
    public int? Year { get; set; }
    public string? GenreRaw { get; set; }
    public string? Comment { get; set; }

    public double? Bpm { get; set; }
    public string? MusicalKey { get; set; }
    public int Rating { get; set; }
    public int PlayCount { get; set; }
    public DateTime? LastPlayedAtUtc { get; set; }

    public DateTime DateAddedUtc { get; set; }
    public DateTime DateModifiedUtc { get; set; }

    /// <summary>Gesetzt, sobald ein Rescan die Datei nicht mehr findet. Null = vorhanden.</summary>
    public DateTime? MissingSinceUtc { get; set; }

    public TimeSpan Duration => TimeSpan.FromMilliseconds(DurationMs);

    /// <summary>Anzeigetitel mit Fallback auf den Dateinamen, falls kein Titel-Tag vorhanden ist.</summary>
    public string DisplayTitle => string.IsNullOrWhiteSpace(Title)
        ? System.IO.Path.GetFileNameWithoutExtension(RelativePath)
        : Title!;

    public string DisplayArtist => string.IsNullOrWhiteSpace(Artist) ? "Unbekannt" : Artist!;

    public string DisplayDuration => Duration.ToString(Duration.TotalHours >= 1 ? @"h\:mm\:ss" : @"m\:ss");
}
