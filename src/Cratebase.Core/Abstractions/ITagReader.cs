namespace Cratebase.Core.Abstractions;

/// <summary>Aus einer Audiodatei gelesene Tags/Technikdaten, unabhängig vom Tag-Format (ID3/Vorbis/MP4/...).</summary>
public sealed record TrackTags(
    int DurationMs,
    int? BitrateKbps,
    int? SampleRateHz,
    int? Channels,
    string? Title,
    string? Artist,
    string? Album,
    string? AlbumArtist,
    int? TrackNumber,
    int? Year,
    string? GenreRaw,
    string? Comment,
    double? Bpm,
    string? MusicalKey);

/// <summary>Implementiert in Cratebase.Tagging via TagLib-Sharp.</summary>
public interface ITagReader
{
    bool CanRead(string filePath);

    /// <summary>Liefert null bei nicht lesbaren/beschädigten Dateien, statt zu werfen – ein einzelner kaputter Track darf einen Scan von 100k Dateien nicht abbrechen.</summary>
    TrackTags? Read(string filePath);
}
