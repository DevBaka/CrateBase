using Cratebase.Core.Abstractions;
using Cratebase.Core.Models;

namespace Cratebase.Scanning;

public sealed class TargetActions(ITrackRepository tracks, ILibraryRootRepository roots, IPlaylistRepository playlists, ITagReader tags, ILibraryQuery query) : ITargetActions
{
    public async Task<Track> RegisterAsync(string path, CancellationToken ct)
    {
        path = Path.GetFullPath(path);
        var root = (await roots.GetAllAsync(ct)).Where(r => path.StartsWith(r.Path.TrimEnd('\\', '/') + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)).OrderByDescending(r => r.Path.Length).FirstOrDefault()
            ?? await roots.AddOrGetAsync(Path.GetDirectoryName(path)!, Path.GetFileName(Path.GetDirectoryName(path))!, ct);
        var relative = Path.GetRelativePath(root.Path, path).Replace('\\', '/');
        var existing = await tracks.FindByPathAsync(root.Id, relative, ct);
        if (existing != null) return existing;
        var info = new FileInfo(path);
        var metadata = await Task.Run(() => tags.Read(path), ct);
        var track = new Track { LibraryRootId = root.Id, RelativePath = relative, FileSizeBytes = info.Length,
            FileModifiedAtUtc = info.LastWriteTimeUtc, Format = info.Extension.TrimStart('.'), DateAddedUtc = DateTime.UtcNow, DateModifiedUtc = DateTime.UtcNow,
            Title = metadata?.Title, Artist = metadata?.Artist, Album = metadata?.Album, GenreRaw = metadata?.GenreRaw,
            DurationMs = metadata?.DurationMs ?? 0, BitrateKbps = metadata?.BitrateKbps, SampleRateHz = metadata?.SampleRateHz,
            Bpm = metadata?.Bpm, MusicalKey = metadata?.MusicalKey };
        track.Id = await tracks.UpsertAsync(track, ct);
        return track;
    }

    public async Task AddAsync(LibraryTarget target, IReadOnlyList<Track> selection, CopyCollision collision, IProgress<TransferProgress>? progress, CancellationToken ct)
    {
        if (target.Kind == LibraryTargetKind.Playlist)
        {
            await playlists.AddTracksAsync(target.PlaylistId!.Value, selection.Select(t => t.Id).Distinct().ToArray(), ct);
            progress?.Report(new(selection.Count, selection.Count, "Playlist aktualisiert.")); return;
        }
        if (target.Kind != LibraryTargetKind.Directory) throw new InvalidOperationException("Dieses Ziel nimmt keine Tracks auf.");
        if (!Directory.Exists(target.Path)) throw new DirectoryNotFoundException("Zielordner ist nicht erreichbar.");
        var rootMap = (await roots.GetAllAsync(ct)).ToDictionary(r => r.Id);
        var errors = new List<string>();
        for (var i = 0; i < selection.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var track = selection[i];
            var source = Path.GetFullPath(Path.Combine(rootMap[track.LibraryRootId].Path, track.RelativePath));
            var destination = Path.Combine(target.Path!, Path.GetFileName(source));
            string? temporary = null;
            try
            {
                if (File.Exists(destination))
                {
                    if (collision == CopyCollision.Skip) { progress?.Report(new(i + 1, selection.Count, "Übersprungen: " + Path.GetFileName(source))); continue; }
                    var suffix = 2;
                    while (File.Exists(destination)) destination = Path.Combine(target.Path!, $"{Path.GetFileNameWithoutExtension(source)} ({suffix++}){Path.GetExtension(source)}");
                }
                temporary = Path.Combine(target.Path!, ".cratebase-" + Guid.NewGuid().ToString("N") + ".partial");
                await using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 131072, true))
                await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 131072, true))
                    await input.CopyToAsync(output, ct);
                ct.ThrowIfCancellationRequested();
                File.Move(temporary, destination, false); temporary = null;
                // Once committed to its final name, finish registration even if cancellation arrives.
                var copy = await RegisterAsync(destination, CancellationToken.None);
                await query.SetClubReadyAsync([copy.Id], track.ClubReady, CancellationToken.None);
                await query.SetFlagAsync([copy.Id],"extended",track.Extended,CancellationToken.None);
                await query.SetFlagAsync([copy.Id],"intro",track.Intro,CancellationToken.None);
                await query.SetFlagAsync([copy.Id],"trash",track.Trash,CancellationToken.None);
                progress?.Report(new(i + 1, selection.Count, Path.GetFileName(destination)));
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { errors.Add(Path.GetFileName(source) + ": " + ex.Message); }
            finally { if (temporary != null && File.Exists(temporary)) File.Delete(temporary); }
        }
        if (errors.Count > 0) throw new IOException(string.Join(Environment.NewLine, errors));
    }
}
