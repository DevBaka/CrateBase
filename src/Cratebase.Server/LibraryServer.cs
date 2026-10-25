using System.Net;
using System.Security.Cryptography;
using System.Text;
using Cratebase.Core.Abstractions;
using Cratebase.Core.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Cratebase.Server;

/// <summary>Embedded, authenticated API. Uses the same repositories and target actions as the desktop.</summary>
public sealed class LibraryServer(ITrackRepository tracks, ILibraryRootRepository roots, IPlaylistRepository playlists,
    ILibraryQuery query, ITargetActions actions, IFavoriteProfileStore favorites, string cacheDirectory, ILibraryRevision? revision = null, IRemotePlayback? playback = null) : IAsyncDisposable
{
    private WebApplication? _app;
    private readonly SemaphoreSlim _writes = new(1, 1);
    private readonly StreamCache _cache = new(cacheDirectory);
    private long _revision;
    public bool IsRunning => _app != null;
    public string Address { get; private set; } = "Ausgeschaltet";
    public event Action<RemoteChange>? Changed;

    public async Task StartAsync(ServerSettings settings, CancellationToken ct = default)
    {
        if (IsRunning) throw new InvalidOperationException("Server läuft bereits. Vor dem Ändern anhalten.");
        if (settings.Port is < 1024 or > 65535 || settings.AccessKey.Length < 32 || settings.CacheMegabytes is < 64 or > 65536)
            throw new ArgumentException("Port 1024–65535, Schlüssel mindestens 32 Zeichen und Cache 64–65536 MB erforderlich.");
        var clubRootId=await playlists.EnsureClubRootAsync(ct);
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { Args = [], ApplicationName = typeof(LibraryServer).Assembly.FullName });
        builder.Logging.ClearProviders(); // Never log authorization, physical paths or query strings.
        builder.WebHost.ConfigureKestrel(k =>
        {
            k.Limits.MaxRequestBodySize = 64 * 1024;
            k.Limits.MaxConcurrentConnections = 64;
            k.Listen(settings.AllowLan ? IPAddress.IPv6Any : IPAddress.Loopback, settings.Port, listen =>
            {
                if (!string.IsNullOrWhiteSpace(settings.CertificatePath)) listen.UseHttps(settings.CertificatePath, settings.CertificatePassword);
            });
        });
        var app = builder.Build();
        var keyHash = SHA256.HashData(Encoding.UTF8.GetBytes(settings.AccessKey));
        app.Use(async (context, next) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            context.Response.Headers.XContentTypeOptions = "nosniff";
            var auth = context.Request.Headers.Authorization.ToString();
            if (!auth.StartsWith("Bearer ", StringComparison.Ordinal) || !CryptographicOperations.FixedTimeEquals(keyHash, SHA256.HashData(Encoding.UTF8.GetBytes(auth[7..]))))
            { context.Response.StatusCode = 401; await context.Response.WriteAsJsonAsync(new { error = "Zugriffsschlüssel fehlt oder ist ungültig." }); return; }
            try { await next(context); }
            catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { }
            catch (Exception ex)
            {
                if (context.Response.HasStarted) { context.Abort(); return; }
                context.Response.StatusCode = ex switch { ArgumentException => 400, KeyNotFoundException => 404, FileNotFoundException => 404, UnauthorizedAccessException => 403, DirectoryNotFoundException => 404, _ => 503 };
                var message = ex is ArgumentException or KeyNotFoundException ? ex.Message : "Aktion nicht möglich. Datei, Ziel oder Streaming-Encoder ist nicht erreichbar. Bitte Server-Einstellungen prüfen.";
                await context.Response.WriteAsJsonAsync(new { error = message });
            }
        });
        app.MapGet("/api/status", async (CancellationToken token) => new { name = "Cratebase", version = 1, revision = revision != null ? await revision.ReadAsync(token) : Interlocked.Read(ref _revision), qualities = StreamCache.Bitrates, format = "mp3", clubRootId });
        app.MapGet("/api/fields", () => TrackMetadata.Fields);
        app.MapGet("/api/player",async(bool? includeQueue,CancellationToken token)=>
        {
            if(playback==null)return Results.Json(new{error="Desktop-Fernsteuerung ist in diesem Server nicht verfügbar."},statusCode:503);
            var state=await playback.ReadAsync(token);
            var ids=includeQueue==true?state.Queue.ToArray():state.TrackId is {} selected?new[]{selected}:Array.Empty<long>();
            var queue=(await tracks.GetByIdsAsync(ids,token)).ToDictionary(t=>t.Id);
            return Results.Ok(new{deviceId="desktop",name=Environment.MachineName,state.Playing,state.PositionMs,state.DurationMs,state.Volume,state.Index,
                track=state.TrackId is {} id&&queue.TryGetValue(id,out var current)?PublicTrack(current):null,
                queue=includeQueue==true?state.Queue.Where(queue.ContainsKey).Select(id=>PublicTrack(queue[id])).ToArray():null});
        });
        app.MapPost("/api/player/load",async(RemotePlaybackLoad request,CancellationToken token)=>
        {
            if(playback==null)throw new InvalidOperationException("Desktop-Player nicht verfügbar.");
            if(request.Ids.Length is <1 or >250||request.Index<0||request.Index>=request.Ids.Length||request.PositionMs<0)throw new ArgumentException("Ungültige Wiedergabeliste oder Position.");
            var found=(await tracks.GetByIdsAsync(request.Ids.Distinct().ToArray(),token)).ToDictionary(t=>t.Id);
            if(request.Ids.Any(id=>!found.ContainsKey(id)))throw new KeyNotFoundException("Track nicht gefunden.");
            await playback.LoadAsync(request.Ids.Select(id=>found[id]).ToArray(),request.Index,request.PositionMs,request.Playing,token);return Results.NoContent();
        });
        app.MapPost("/api/player/command",async(RemotePlaybackCommand request,CancellationToken token)=>
        {
            if(playback==null)throw new InvalidOperationException("Desktop-Player nicht verfügbar.");
            if(request.PositionMs<0||request.Volume is {} v&&(!double.IsFinite(v)||v<0||v>100))throw new ArgumentException("Ungültige Position oder Lautstärke.");
            await playback.CommandAsync(request,token);return Results.NoContent();
        });
        app.MapGet("/api/playlists", async (CancellationToken token) => await playlists.GetAllNodesAsync(token));
        app.MapGet("/api/roots", async (CancellationToken token) => (await roots.GetAllAsync(token)).Select(r => new { r.Id, r.Label }));
        app.MapGet("/api/folders", async (long rootId, string? path, CancellationToken token) =>
        {
            var directory = await ResolveFolder(rootId, path, token);
            var root = (await roots.GetAllAsync(token)).First(r => r.Id == rootId);
            return Directory.EnumerateDirectories(directory).Where(d => (File.GetAttributes(d) & FileAttributes.ReparsePoint) == 0)
                .OrderBy(d => d, StringComparer.OrdinalIgnoreCase).Take(2000)
                .Select(d => new FolderEntry(Path.GetFileName(d), rootId, Path.GetRelativePath(root.Path, d).Replace('\\', '/'))).ToArray();
        });
        app.MapPost("/api/tracks/query", async (TrackQuery request, CancellationToken token) =>
        {
            if (request.Search?.Length > 256 || request.Filters?.Count > 20 || request.Offset < 0) throw new ArgumentException("Suchabfrage ist zu groß.");
            var source = await ValidateTarget(request.Source, token);
            var safe = request with { Source = source, Limit = Math.Clamp(request.Limit, 1, 250) };
            return new { tracks = (await query.QueryAsync(safe, token)).Select(PublicTrack), totals = await query.TotalsAsync(safe, token) };
        });
        app.MapGet("/api/tracks/{id:long}", async (long id, CancellationToken token) => PublicTrack(await GetTrack(id, token)));
        app.MapPut("/api/flags", async (FlagChange change, CancellationToken token) =>
        {
            if (change.Field is not ("club" or "extended" or "intro" or "trash") || change.Ids.Length is < 1 or > 250) throw new ArgumentException("Ungültige Markierung oder Track-Anzahl.");
            await Write(async () => { await query.SetFlagAsync(change.Ids, change.Field, change.Value, token); }, token);
            Notify(change.Ids.Length == 1 ? change.Ids[0] : null); return Results.NoContent();
        });
        app.MapPatch("/api/tracks/{id:long}", async (long id, MetadataChange change, CancellationToken token) =>
        {
            await Write(async () =>
            {
                var track = await GetTrack(id, token);
                foreach (var pair in change.Values)
                {
                    if (pair.Value?.Length > 4000) throw new ArgumentException("Metadaten sind zu lang.");
                    var value = pair.Value;
                    switch (pair.Key)
                    {
                        case "title": track.Title = value; break; case "artist": track.Artist = value; break;
                        case "album": track.Album = value; break; case "albumartist": track.AlbumArtist = value; break;
                        case "genre": track.GenreRaw = value; break; case "subgenre": track.SubGenre = value; break;
                        case "comment": track.Comment = value; break; case "comment2": track.Comment2 = value; break;
                        case "color": track.ColorTag = value; break; case "key": track.MusicalKey = value; break;
                        case "bpm": track.Bpm = string.IsNullOrWhiteSpace(value) ? null : Convert.ToDouble(TrackMetadata.Parse(TrackMetadata.Get("bpm"), value)); break;
                        case "year": track.Year = ParseInteger(value, 0, 9999); break;
                        case "tracknumber": track.TrackNumber = ParseInteger(value, 0, 9999); break;
                        case "rating": track.Rating = ParseInteger(value, 0, 5) ?? 0; break;
                        default: throw new ArgumentException("Dieses Feld kann nicht bearbeitet werden: " + pair.Key);
                    }
                }
                track.DateModifiedUtc = DateTime.UtcNow; await tracks.UpdateMetadataAsync(track, change.Values.Keys.ToArray(), token);
            }, token);
            Notify(id); return PublicTrack(await GetTrack(id, token));
        });
        app.MapPost("/api/playlists", async (PlaylistChange change, CancellationToken token) =>
        {
            ValidateName(change.Name); await ValidateParent(change.ParentId,token); PlaylistNode? node = null;
            await Write(async () => node = await playlists.CreateAsync(change.Name.Trim(), change.Folder ? PlaylistNodeType.Folder : PlaylistNodeType.Playlist, change.ParentId, token), token);
            Notify(); return Results.Ok(node);
        });
        app.MapPut("/api/playlists/{id:long}", async (long id, PlaylistChange change, CancellationToken token) =>
        {
            ValidateName(change.Name);
            if(id==clubRootId)throw new ArgumentException("Die Club Collection kann nicht umbenannt oder verschoben werden.");
            await ValidateParent(change.ParentId,token);
            await Write(async () => { await playlists.MoveAsync(id, change.ParentId, token); await playlists.RenameAsync(id, change.Name.Trim(), token); }, token);
            Notify(); return Results.NoContent();
        });
        app.MapDelete("/api/playlists/{id:long}", async (long id, CancellationToken token) =>
        {
            // Mobile cannot destroy the special Club Collection root.
            if (id == await playlists.EnsureClubRootAsync(token)) throw new ArgumentException("Die Club Collection kann nicht gelöscht werden.");
            await Write(() => playlists.DeleteAsync(id, token), token); Notify(); return Results.NoContent();
        });
        app.MapPost("/api/playlists/{id:long}/tracks", async (long id, AddTracks request, CancellationToken token) =>
        {
            await Add(new(LibraryTargetKind.Playlist, "Playlist", id), request.Ids, token); return Results.NoContent();
        });
        app.MapDelete("/api/playlists/{id:long}/tracks/{trackId:long}", async (long id, long trackId, CancellationToken token) =>
        { await Write(() => playlists.RemoveTrackAsync(id, trackId, token), token); Notify(trackId); return Results.NoContent(); });
        app.MapGet("/api/favorites", async (CancellationToken token) =>
        {
            var profiles = await LoadFavorites(token);
            // Paths remain server-side; clients address slots by profile and index.
            return new { profiles.ActiveProfileName, profiles.SlotCount, profiles = profiles.Profiles.Select(p => new { p.Name, slots = p.Targets.Select(s => new { index = s.Key, s.Value.Name, kind = s.Value.Kind }) }) };
        });
        app.MapPost("/api/favorites/{profile}/{slot:int}", async (string profile, int slot, AddTracks request, CancellationToken token) =>
        {
            var p = (await LoadFavorites(token)).Profiles.FirstOrDefault(p => p.Name == profile) ?? throw new KeyNotFoundException("Favoritenprofil nicht gefunden.");
            if (!p.Targets.TryGetValue(slot, out var target)) throw new ArgumentException("Favoriten-Slot ist nicht belegt.");
            if (target.Kind == LibraryTargetKind.Directory)
            {
                var root = (await roots.GetAllAsync(token)).OrderByDescending(r => r.Path.Length).FirstOrDefault(r =>
                    target.Path != null && (Path.GetFullPath(target.Path).Equals(Path.GetFullPath(r.Path), StringComparison.OrdinalIgnoreCase) ||
                    Path.GetFullPath(target.Path).StartsWith(Path.GetFullPath(r.Path).TrimEnd('\\', '/') + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)))
                    ?? throw new ArgumentException("Favoritenordner liegt außerhalb der freigegebenen Musikordner.");
                target = target with { PlaylistId = root.Id, Path = Path.GetRelativePath(root.Path, target.Path!) };
            }
            await Add(target, request.Ids, token); return Results.NoContent();
        });
        app.MapGet("/api/stream/{id:long}", async (long id, int? bitrate, HttpContext context) =>
        {
            var token = context.RequestAborted;
            var track = await GetTrack(id, token);
            var root = (await roots.GetAllAsync(token)).FirstOrDefault(r => r.Id == track.LibraryRootId) ?? throw new FileNotFoundException();
            var path = ContainedPath(root.Path, track.RelativePath);
            if (!File.Exists(path)) { await tracks.SetFileAvailabilityAsync(id, false, token); Notify(id); throw new FileNotFoundException(); }
            var compressed = await _cache.PrepareAsync(path, bitrate ?? 160, settings, token);
            var file = new FileStream(compressed, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, 65536, true);
            return Results.File(file, "audio/mpeg", enableRangeProcessing: true);
        });
        try { await app.StartAsync(ct); _app = app; Address = $"{(string.IsNullOrWhiteSpace(settings.CertificatePath) ? "http" : "https")}://{(settings.AllowLan ? "[::]" : "127.0.0.1")}:{settings.Port}"; }
        catch { await app.DisposeAsync(); throw; }
    }

    public async Task StopAsync(CancellationToken ct = default)
    {
        var app = _app; if (app == null) return;
        _app = null; Address = "Ausgeschaltet";
        try { using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromSeconds(5)); await app.StopAsync(timeout.Token); }
        finally { await app.DisposeAsync(); }
    }
    public async ValueTask DisposeAsync() { await StopAsync(); revision?.Dispose(); }
    private async Task Write(Func<Task> action, CancellationToken ct) { await _writes.WaitAsync(ct); try { await action(); } finally { _writes.Release(); } }
    private void Notify(long? id = null) { Interlocked.Increment(ref _revision); Changed?.Invoke(new(id)); }
    private async Task<Track> GetTrack(long id, CancellationToken ct) => await tracks.GetByIdAsync(id, ct) ?? throw new KeyNotFoundException("Track nicht gefunden.");
    private static int? ParseInteger(string? value, int min, int max) => string.IsNullOrWhiteSpace(value) ? null : int.TryParse(value, out var n) && n >= min && n <= max ? n : throw new ArgumentException("Zahl außerhalb des zulässigen Bereichs.");
    private static void ValidateName(string name) { if (string.IsNullOrWhiteSpace(name) || name.Length > 200) throw new ArgumentException("Name muss 1–200 Zeichen enthalten."); }
    private async Task ValidateParent(long? id,CancellationToken ct)
    {
        if(id!=null&&!(await playlists.GetAllNodesAsync(ct)).Any(n=>n.Id==id&&n.NodeType==PlaylistNodeType.Folder))throw new ArgumentException("Nur eine Playlist-Gruppe kann Untergruppen oder Playlisten enthalten.");
    }
    private async Task<FavoriteProfileSettings> LoadFavorites(CancellationToken ct)
    {
        var profiles=await favorites.LoadAsync(ct);var nodes=(await playlists.GetAllNodesAsync(ct)).ToDictionary(n=>n.Id);
        foreach(var profile in profiles.Profiles)
        {
            foreach(var old in profile.Slots)if(!profile.Targets.ContainsKey(old.Key))profile.Targets[old.Key]=new(LibraryTargetKind.Playlist,nodes.GetValueOrDefault(old.Value)?.Name??"Playlist",old.Value);
            foreach(var slot in profile.Targets.ToArray())if(slot.Value.Kind==LibraryTargetKind.Playlist&&slot.Value.PlaylistId is {} id&&nodes.TryGetValue(id,out var node))profile.Targets[slot.Key]=slot.Value with{Name=node.Name};
        }
        return profiles;
    }
    private async Task Add(LibraryTarget target, long[] ids, CancellationToken ct)
    {
        if (ids.Length is < 1 or > 250) throw new ArgumentException("1–250 Tracks pro Auftrag erlaubt.");
        target = await ValidateTarget(target, ct);
        var selection = await tracks.GetByIdsAsync(ids.Distinct().ToArray(), ct);
        if (selection.Count != ids.Distinct().Count()) throw new KeyNotFoundException("Track nicht gefunden.");
        var rootMap=(await roots.GetAllAsync(ct)).ToDictionary(r=>r.Id);
        foreach(var track in selection)_=ContainedPath(rootMap[track.LibraryRootId].Path,track.RelativePath);
        await Write(() => actions.AddAsync(target, selection, CopyCollision.KeepBoth, null, ct), ct); Notify();
    }
    private async Task<LibraryTarget> ValidateTarget(LibraryTarget target, CancellationToken ct)
    {
        if (target.Kind == LibraryTargetKind.Directory)
        {
            var all = await roots.GetAllAsync(ct);
            // Directory targets use root ID in PlaylistId and a relative path over the wire.
            var root = all.FirstOrDefault(r => r.Id == target.PlaylistId) ?? throw new ArgumentException("Unbekannter Musikordner.");
            return target with { Path = ContainedPath(root.Path, target.Path ?? "") };
        }
        if (target.Kind is LibraryTargetKind.Playlist or LibraryTargetKind.PlaylistFolder)
        {
            var node = (await playlists.GetAllNodesAsync(ct)).FirstOrDefault(n => n.Id == target.PlaylistId) ?? throw new KeyNotFoundException("Playlist nicht gefunden.");
            return target with { Kind = node.NodeType == PlaylistNodeType.Folder ? LibraryTargetKind.PlaylistFolder : LibraryTargetKind.Playlist };
        }
        if (target.Kind is LibraryTargetKind.Collection or LibraryTargetKind.ClubCollection or LibraryTargetKind.ClubUnsorted or LibraryTargetKind.UnreachableFiles or LibraryTargetKind.TraktorCollection) return target;
        throw new ArgumentException("Unbekannte Quelle.");
    }
    private async Task<string> ResolveFolder(long rootId, string? path, CancellationToken ct)
    {
        var root = (await roots.GetAllAsync(ct)).FirstOrDefault(r => r.Id == rootId) ?? throw new ArgumentException("Unbekannter Musikordner.");
        return ContainedPath(root.Path, path ?? "");
    }
    public static string ContainedPath(string root, string relative)
    {
        if (Path.IsPathRooted(relative) || relative.Contains(':') || relative.IndexOf('\0') >= 0) throw new ArgumentException("Ungültiger relativer Pfad.");
        var basePath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var prefix=Path.EndsInDirectorySeparator(basePath)?basePath:basePath+Path.DirectorySeparatorChar;
        var candidate = Path.GetFullPath(Path.Combine(basePath, relative));
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!candidate.Equals(basePath, comparison) && !candidate.StartsWith(prefix, comparison)) throw new ArgumentException("Pfad liegt außerhalb des Musikordners.");
        // Reject links/junctions, including intermediate segments (no root escape through a link).
        var current = new FileInfo(candidate).FullName;
        while (!current.Equals(basePath,comparison))
        {
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new ArgumentException("Verknüpfungen werden nicht freigegeben.");
            current = Path.GetDirectoryName(current)??throw new ArgumentException("Pfad liegt außerhalb des Musikordners.");
        }
        return candidate;
    }
    private static object PublicTrack(Track t) => new { t.Id, t.Title, t.Artist, t.DisplayTitle, t.DisplayArtist, t.Album, t.AlbumArtist, t.TrackNumber,
        t.GenreRaw, t.SubGenre, t.Year, t.Bpm, t.MusicalKey, t.Rating, t.Comment, t.Comment2, t.ColorTag, t.Format, t.BitrateKbps,
        t.SampleRateHz, t.Channels, t.DurationMs, t.FileSizeBytes, t.ClubReady, t.Extended, t.Intro, t.Trash, t.FileAvailability, t.DateModifiedUtc };
}
