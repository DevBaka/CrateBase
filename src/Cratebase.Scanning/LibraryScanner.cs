using Cratebase.Core.Abstractions;
using Cratebase.Core.Models;

namespace Cratebase.Scanning;

/// <summary>
/// Rekursiver, inkrementeller Scan (s. ILibraryScanner). Kennt kein SQL – spricht nur über
/// ITrackRepository/ILibraryRootRepository mit der Datenschicht und über ITagReader mit dem
/// Tagging-Modul, damit Scanning austauschbar bleibt (z.B. später durch einen
/// Filesystem-Watcher ergänzt statt ersetzt werden kann).
/// </summary>
public sealed class LibraryScanner(ITrackRepository trackRepository, ILibraryRootRepository libraryRootRepository, ITagReader tagReader) : ILibraryScanner
{
    private const int ProgressReportInterval = 20;

    public async Task ScanAsync(LibraryRoot root, IProgress<ScanProgress>? progress, CancellationToken ct = default)
        => await ScanFolderAsync(root,root.Path,progress,ct);

    public async Task ScanFolderAsync(LibraryRoot root,string absoluteFolder,IProgress<ScanProgress>? progress,CancellationToken ct=default)
    {
        var rootPath=Path.GetFullPath(root.Path).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
        absoluteFolder=Path.GetFullPath(absoluteFolder);
        if(!string.Equals(absoluteFolder.TrimEnd(Path.DirectorySeparatorChar),rootPath.TrimEnd(Path.DirectorySeparatorChar),StringComparison.OrdinalIgnoreCase) && !absoluteFolder.StartsWith(rootPath,StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Scan-Ordner liegt außerhalb der Library-Wurzel.");
        if (!Directory.Exists(absoluteFolder))
        {
            throw new DirectoryNotFoundException($"Library-Root nicht gefunden: {root.Path}");
        }

        // Einmal komplett laden statt pro Datei einzeln nachzufragen – für 100k Tracks ein
        // einzelnes indiziertes SELECT statt 100k Round-Trips.
        var knownFingerprints = await trackRepository.GetFingerprintsByPathAsync(root.Id, ct);
        var presentPaths = new List<string>();
        var relativeFolder=Path.GetRelativePath(root.Path,absoluteFolder).Replace('\\','/');
        if(relativeFolder!=".")
        {
            var prefix=relativeFolder.TrimEnd('/')+"/";
            presentPaths.AddRange(knownFingerprints.Keys.Where(p=>!p.StartsWith(prefix,StringComparison.OrdinalIgnoreCase)));
        }
        var scanStartUtc = DateTime.UtcNow;

        var filesSeen = 0;
        var filesAdded = 0;
        var filesUpdated = 0;
        var filesUnchanged = 0;
        var filesFailed = 0;

        foreach (var absolutePath in EnumerateCandidateFiles(absoluteFolder))
        {
            ct.ThrowIfCancellationRequested();

            var relativePath = ToRelativePath(root.Path, absolutePath);
            presentPaths.Add(relativePath);
            filesSeen++;

            FileInfo fileInfo;
            try
            {
                fileInfo = new FileInfo(absolutePath);
            }
            catch (IOException)
            {
                filesFailed++;
                continue;
            }

            var mtimeUtc = fileInfo.LastWriteTimeUtc;

            if (knownFingerprints.TryGetValue(relativePath, out var known)
                && known.FileSizeBytes == fileInfo.Length
                && MtimesEqual(known.FileModifiedAtUtc, mtimeUtc))
            {
                filesUnchanged++;
            }
            else
            {
                var isNew = !knownFingerprints.ContainsKey(relativePath);
                var tags = tagReader.Read(absolutePath);
                if (tags is null)
                {
                    // Nicht überspringen: Datei trotzdem mit Basisdaten anlegen (Titel fällt in
                    // Track.DisplayTitle auf den Dateinamen zurück), statt sie beim Scan
                    // stillschweigend verschwinden zu lassen.
                    filesFailed++;
                }

                var track = BuildTrack(root.Id, relativePath, fileInfo, mtimeUtc, tags);
                await trackRepository.UpsertAsync(track, ct);

                if (isNew)
                {
                    filesAdded++;
                }
                else
                {
                    filesUpdated++;
                }
            }

            if (filesSeen % ProgressReportInterval == 0)
            {
                progress?.Report(new ScanProgress(filesSeen, filesAdded, filesUpdated, filesUnchanged, filesFailed, relativePath));
            }
        }

        await trackRepository.MarkMissingExceptAsync(root.Id, presentPaths, scanStartUtc, ct);
        await libraryRootRepository.UpdateLastScannedAsync(root.Id, scanStartUtc, ct);

        progress?.Report(new ScanProgress(filesSeen, filesAdded, filesUpdated, filesUnchanged, filesFailed, null));
    }

    /// <summary>Toleranz statt exakter Gleichheit: manche Dateisysteme runden mtimes unterschiedlich.</summary>
    private static bool MtimesEqual(DateTime a, DateTime b) => Math.Abs((a - b).TotalSeconds) < 1;

    private IEnumerable<string> EnumerateCandidateFiles(string rootPath)
    {
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.Hidden | FileAttributes.System,
        };

        foreach (var path in Directory.EnumerateFiles(rootPath, "*", options))
        {
            if (tagReader.CanRead(path))
            {
                yield return path;
            }
        }
    }

    private static string ToRelativePath(string rootPath, string absolutePath) =>
        Path.GetRelativePath(rootPath, absolutePath).Replace(Path.DirectorySeparatorChar, '/');

    private static Track BuildTrack(long libraryRootId, string relativePath, FileInfo fileInfo, DateTime mtimeUtc, TrackTags? tags)
    {
        var now = DateTime.UtcNow;
        return new Track
        {
            LibraryRootId = libraryRootId,
            RelativePath = relativePath,
            FileSizeBytes = fileInfo.Length,
            FileModifiedAtUtc = mtimeUtc,
            Format = fileInfo.Extension.TrimStart('.').ToLowerInvariant(),
            DurationMs = tags?.DurationMs ?? 0,
            BitrateKbps = tags?.BitrateKbps,
            SampleRateHz = tags?.SampleRateHz,
            Channels = tags?.Channels,
            Title = tags?.Title,
            Artist = tags?.Artist,
            Album = tags?.Album,
            AlbumArtist = tags?.AlbumArtist,
            TrackNumber = tags?.TrackNumber,
            Year = tags?.Year,
            GenreRaw = tags?.GenreRaw,
            Comment = tags?.Comment,
            Bpm = tags?.Bpm,
            MusicalKey = tags?.MusicalKey,
            DateAddedUtc = now, // wird von TrackRepository.UpsertAsync bei bestehenden Tracks ignoriert
            DateModifiedUtc = now,
        };
    }
}
