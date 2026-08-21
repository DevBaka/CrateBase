using System.Linq;
using Cratebase.Core.Abstractions;

namespace Cratebase.Tagging;

/// <summary>
/// Liest Tags/Technikdaten via TagLib-Sharp. Fängt bewusst jede Exception ab und gibt
/// stattdessen null zurück – bei ~100k Dateien sind ein paar kaputte/exotische Files
/// normal, und die dürfen einen Bibliotheks-Scan nicht abbrechen (s. ITagReader.Read).
/// </summary>
public sealed class TagLibTagReader : ITagReader
{
    private static readonly string[] SupportedExtensions = [".mp3", ".flac", ".wav", ".aiff", ".aif", ".m4a", ".ogg"];

    public bool CanRead(string filePath) =>
        SupportedExtensions.Contains(Path.GetExtension(filePath), StringComparer.OrdinalIgnoreCase);

    public TrackTags? Read(string filePath)
    {
        try
        {
            using var file = TagLib.File.Create(filePath);
            var tag = file.Tag;
            var properties = file.Properties;

            return new TrackTags(
                DurationMs: (int)properties.Duration.TotalMilliseconds,
                BitrateKbps: properties.AudioBitrate > 0 ? properties.AudioBitrate : null,
                SampleRateHz: properties.AudioSampleRate > 0 ? properties.AudioSampleRate : null,
                Channels: properties.AudioChannels > 0 ? properties.AudioChannels : null,
                Title: NullIfBlank(tag.Title),
                Artist: NullIfBlank(tag.FirstPerformer ?? tag.FirstAlbumArtist),
                Album: NullIfBlank(tag.Album),
                AlbumArtist: NullIfBlank(tag.FirstAlbumArtist),
                TrackNumber: tag.Track > 0 ? (int)tag.Track : null,
                Year: tag.Year > 0 ? (int)tag.Year : null,
                GenreRaw: NullIfBlank(tag.FirstGenre),
                Comment: NullIfBlank(tag.Comment),
                Bpm: tag.BeatsPerMinute > 0 ? tag.BeatsPerMinute : null,
                MusicalKey: NullIfBlank(tag.InitialKey));
        }
        catch (Exception)
        {
            // Absichtlich breit gefangen: TagLib-Sharp wirft für viele "kaputte" Dateien
            // unterschiedlichste Exception-Typen (CorruptFileException, IO, unsupported
            // format, ...). Der Scanner behandelt einen null-Rückgabewert als "Tags nicht
            // lesbar" und legt den Track trotzdem mit Datei-Basisdaten an.
            return null;
        }
    }

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
