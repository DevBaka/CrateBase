using System.Text.Json;
using System.Xml.Linq;
using System.Globalization;
using Cratebase.Core.Models;
using Cratebase.Core.Abstractions;

namespace Cratebase.Import;

public sealed class SyncEntity
{
    public long LocalId { get; set; }
    public string Kind { get; set; } = "track";
    public Dictionary<string, string?> Fields { get; set; } = new();
    public string? RawXml { get; set; }
    public bool IgnoreRemoteAbsence { get; set; }
    public bool ClubReady { get; set; }
}
public sealed class SyncState
{
    public Dictionary<string, SyncEntity> Entities { get; set; } = new();
}
public sealed class SyncChange
{
    public string Identity { get; init; } = "";
    public string Field { get; init; } = "";
    public string? Local { get; init; }
    public string? Remote { get; init; }
    public bool Conflict { get; init; }
    public bool UseRemote { get; set; } = true;
    public string Description => $"{(Conflict ? "KONFLIKT" : "Änderung")} · {Identity} · {Field}";
}
public sealed class SyncPreview
{
    public SyncState Remote { get; init; } = new();
    public SyncState Local { get; init; } = new();
    public List<SyncChange> Changes { get; init; } = new();
    public string Warning => "Abgleich: Titel, Artist, Album, Genre, Kommentar, BPM, Key, Rating, Playlist-Hierarchie und Reihenfolge. Originale ENTRY-XML einschließlich Cues/Beatgrid bleibt erhalten; weitere Traktor-Sonderdaten außerhalb ENTRY werden nicht bearbeitet. Club-Markierungen bleiben lokal. Neue Playlisten ohne UUID werden zunächst über ihren Hierarchiepfad erkannt.";
}

/// <summary>Three-way merge. Import and confirmed export are distinct checkpoints.</summary>
public sealed class CollectionSync(ITrackRepository tracks, ILibraryRootRepository roots, IPlaylistRepository playlists, IWorkspaceStateStore store, IWorkspaceCheckpoint checkpoint)
{
    private const string StateKey = "traktor-sync-v1";
    private SyncState _known = new();
    private SyncState? _pending;
    private readonly SemaphoreSlim _gate = new(1);
    private static string? Attr(XElement? node, string key) => (string?)node?.Attribute(key);

    private static bool FillTechnicalMetadata(Track track, string? xml)
    {
        if(xml==null) return false;
        var info=XElement.Parse(xml).Element("INFO"); var changed=false;
        if(track.BitrateKbps==null && double.TryParse(Attr(info,"BITRATE"),CultureInfo.InvariantCulture,out var bitrate) && bitrate>0)
        { track.BitrateKbps=(int)Math.Round(bitrate/1000); changed=true; }
        if(track.DurationMs<=0 && double.TryParse(Attr(info,"PLAYTIME_FLOAT")??Attr(info,"PLAYTIME"),CultureInfo.InvariantCulture,out var seconds) && seconds>0 && seconds<int.MaxValue/1000.0)
        { track.DurationMs=(int)Math.Round(seconds*1000); changed=true; }
        if(string.IsNullOrEmpty(track.Format)) { track.Format=Path.GetExtension(track.RelativePath).TrimStart('.'); changed=true; }
        return changed;
    }

    public async Task RefreshTechnicalMetadataAsync(CancellationToken ct=default)
    {
        if(await store.ReadAsync("traktor-technical-metadata-v1",ct)!=null) return;
        var known=JsonSerializer.Deserialize<SyncState>(await store.ReadAsync(StateKey,ct)??"{}");
        var mapped=known?.Entities.Values.Where(e=>e.Kind=="track"&&e.LocalId>0&&e.RawXml!=null).GroupBy(e=>e.LocalId).ToDictionary(g=>g.Key,g=>g.First().RawXml)??new();
        foreach(var ids in mapped.Keys.Chunk(400))
        {
            var rows=await tracks.GetByIdsAsync(ids,ct);
            var changed=rows.Where(t=>FillTechnicalMetadata(t,mapped[t.Id])).ToArray();
            if(changed.Length>0) await tracks.UpdateBatchAsync(changed,ct);
        }
        await store.WriteAsync("traktor-technical-metadata-v1","done",ct);
    }

    public async Task<string?> GetSourcePathAsync(CancellationToken ct = default)
        => JsonSerializer.Deserialize<string>(await store.ReadAsync("traktor-nml-source",ct) ?? "null");

    public async Task EnsureCollectionMembershipAsync(CancellationToken ct = default)
    {
        if(await store.ReadAsync("traktor-collection-track-ids",ct)!=null) return;
        var known=JsonSerializer.Deserialize<SyncState>(await store.ReadAsync(StateKey,ct)??"{}");
        var ids=known?.Entities.Values.Where(e=>e.Kind=="track"&&!e.IgnoreRemoteAbsence&&e.LocalId>0).Select(e=>e.LocalId).Distinct().ToArray()??[];
        await store.WriteAsync("traktor-collection-track-ids",JsonSerializer.Serialize(ids),ct);
    }

    public async Task<SyncPreview> PreviewAsync(string path, CancellationToken ct = default)
    {
        _known = JsonSerializer.Deserialize<SyncState>(await store.ReadAsync(StateKey, ct) ?? "{}") ?? new();
        var remote = await Task.Run(() => Read(path), ct);
        await store.WriteAsync("traktor-nml-source",JsonSerializer.Serialize(Path.GetFullPath(path)),ct);
        var local = await CaptureAsync(_known, ct);
        ReconcileLegacyPlaylists(remote,local,_known);
        var changes = new List<SyncChange>();
        foreach (var id in remote.Entities.Keys.Union(_known.Entities.Keys))
        {
            remote.Entities.TryGetValue(id, out var incoming);
            local.Entities.TryGetValue(id, out var current);
            _known.Entities.TryGetValue(id, out var baseline);
            if (incoming == null)
            {
                // Collection absence is never interpreted as a file deletion.
                if (baseline?.Kind == "track" || baseline?.IgnoreRemoteAbsence == true) continue;
                if (current != null) changes.Add(new() { Identity = id, Field = "$deleted", Local = JsonSerializer.Serialize(current.Fields), Remote = null, Conflict = baseline != null && !Equal(current.Fields, baseline.Fields) });
                continue;
            }
            if (current == null)
            {
                if(baseline!=null && Equal(incoming.Fields,baseline.Fields)) continue;
                changes.Add(new() { Identity = id, Field = "$new", Remote = incoming.Kind,Conflict=baseline!=null }); continue;
            }
            foreach (var field in incoming.Fields.Keys.Union(baseline?.Fields.Keys ?? Enumerable.Empty<string>()))
            {
                incoming.Fields.TryGetValue(field, out var r); current.Fields.TryGetValue(field, out var l);
                string? b = null; baseline?.Fields.TryGetValue(field, out b);
                if (r == l || (baseline != null && r == b)) continue;
                var emptyPlaylist=field=="members"&&(string.IsNullOrEmpty(l)||l=="[]");
                changes.Add(new() { Identity = id, Field = field, Local = l, Remote = r, Conflict = baseline == null ? !string.IsNullOrEmpty(l)&&!emptyPlaylist : l != b });
            }
        }
        return new() { Remote = remote, Local = local, Changes = changes };
    }
    private static bool Equal(Dictionary<string,string?> a, Dictionary<string,string?> b) => a.Count == b.Count && a.All(p => b.GetValueOrDefault(p.Key) == p.Value);

    private static void ReconcileLegacyPlaylists(SyncState remote,SyncState local,SyncState known)
    {
        // The previous importer created the hierarchy before it had any scanned tracks.
        // Reuse those IDs so favorite slots survive and an empty legacy playlist is repaired.
        var wrapper=local.Entities.FirstOrDefault(p=>p.Value.Kind=="folder"&&p.Value.Fields.GetValueOrDefault("name")=="Traktor Import"&&p.Value.Fields.GetValueOrDefault("parent")=="");
        var remoteHasWrapper=remote.Entities.Values.Any(e=>e.Kind=="folder"&&e.Fields.GetValueOrDefault("name")=="Traktor Import"&&e.Fields.GetValueOrDefault("parent")=="");
        foreach(var incoming in remote.Entities.Where(p=>p.Value.Kind!="track"))
        {
            if(local.Entities.ContainsKey(incoming.Key)) continue;
            var parent=incoming.Value.Fields.GetValueOrDefault("parent")??"";
            var candidates=local.Entities.Where(p=>p.Value.Kind==incoming.Value.Kind&&!known.Entities.ContainsKey(p.Key)
                &&p.Value.Fields.GetValueOrDefault("name")==incoming.Value.Fields.GetValueOrDefault("name")
                &&(p.Value.Fields.GetValueOrDefault("parent")==parent || (parent==""&&!remoteHasWrapper&&wrapper.Key!=null&&p.Value.Fields.GetValueOrDefault("parent")==wrapper.Key))).ToList();
            if(candidates.Count!=1) continue;
            var match=candidates[0]; local.Entities.Remove(match.Key); local.Entities[incoming.Key]=match.Value;
            if(parent=="") match.Value.Fields["parent"]="";
            foreach(var child in local.Entities.Values.Where(e=>e.Kind!="track"&&e.Fields.GetValueOrDefault("parent")==match.Key)) child.Fields["parent"]=incoming.Key;
        }
    }

    public static SyncState Read(string path)
    {
        var state = new SyncState();
        using var reader = System.Xml.XmlReader.Create(path, new() { DtdProcessing = System.Xml.DtdProcessing.Ignore, XmlResolver = null });
        var doc = XDocument.Load(reader);
        if(doc.Root?.Name!="NML" || doc.Root.Element("COLLECTION")==null)
            throw new InvalidDataException("Die Datei enthält keine Traktor-NML-Collection. Bitte collection.nml oder einen vollständigen NML-Export auswählen.");
        foreach (var entry in doc.Root?.Element("COLLECTION")?.Elements("ENTRY") ?? [])
        {
            var location = entry.Element("LOCATION");
            if (location == null) continue;
            var key = TraktorPath.FromLocation(Attr(location,"VOLUME") ?? "", Attr(location,"DIR") ?? "", Attr(location,"FILE") ?? "").ToCombinedKey();
            var info = entry.Element("INFO");
            state.Entities["track:" + key] = new() { RawXml = entry.ToString(SaveOptions.DisableFormatting), Fields = new()
            {
                ["title"] = Attr(entry,"TITLE"), ["artist"] = Attr(entry,"ARTIST"), ["album"] = Attr(entry.Element("ALBUM"),"TITLE"),
                ["genre"] = Attr(info,"GENRE"), ["comment"] = Attr(info,"COMMENT"), ["key"] = Attr(info,"KEY"),
                ["bpm"] = double.TryParse(Attr(entry.Element("TEMPO"),"BPM"),CultureInfo.InvariantCulture,out var parsedBpm) ? parsedBpm.ToString(CultureInfo.InvariantCulture) : null,
                ["rating"] = Attr(info,"RANKING") ?? "0"
            }};
        }
        void Visit(XElement node, string parent, string parentPath)
        {
            var name = Attr(node,"NAME") ?? "";
            var nodePath = parentPath + "/" + Uri.EscapeDataString(name);
            var uuid = Attr(node.Element("PLAYLIST"),"UUID");
            var id = "node:" + (string.IsNullOrEmpty(uuid) ? nodePath : "uuid:" + uuid);
            var playlist = Attr(node,"TYPE") == "PLAYLIST";
            state.Entities[id] = new() { Kind = playlist ? "playlist" : "folder", Fields = new()
            {
                ["name"] = name, ["parent"] = parent,
                ["members"] = playlist ? JsonSerializer.Serialize(node.Element("PLAYLIST")?.Elements("ENTRY").Select(e => "track:" + Attr(e.Element("PRIMARYKEY"),"KEY")).Where(k => k != "track:").ToArray() ?? []) : null
            }};
            foreach (var child in node.Element("SUBNODES")?.Elements("NODE") ?? []) Visit(child,id,nodePath);
        }
        foreach (var node in doc.Root?.Element("PLAYLISTS")?.Element("NODE")?.Element("SUBNODES")?.Elements("NODE") ?? []) Visit(node,"","");
        return state;
    }

    private async Task<SyncState> CaptureAsync(SyncState mapping, CancellationToken ct, bool includeClub=false)
    {
        var result = new SyncState();
        var clubNodes=(await playlists.GetClubNodeIdsAsync(ct)).ToHashSet();
        var nodes = (await playlists.GetAllNodesAsync(ct)).Where(n=>includeClub||!clubNodes.Contains(n.Id)).ToArray();
        var identities = mapping.Entities.Where(p => p.Value.Kind != "track"&&p.Value.LocalId>0).GroupBy(p=>p.Value.LocalId).ToDictionary(g=>g.Key,g=>g.First().Key);
        foreach (var node in nodes) identities.TryAdd(node.Id,"node:uuid:" + Guid.NewGuid().ToString("N"));
        if(includeClub)
        {
            var clubIdentities=JsonSerializer.Deserialize<Dictionary<long,string>>(await store.ReadAsync("club-export-identities",ct)??"{}")??new();
            foreach(var id in clubNodes)
            {
                if(!clubIdentities.TryGetValue(id,out var key)) clubIdentities[id]=key=identities[id];
                identities[id]=key;
            }
            await store.WriteAsync("club-export-identities",JsonSerializer.Serialize(clubIdentities),ct);
        }
        var trackKeys = mapping.Entities.Where(p => p.Value.Kind == "track"&&p.Value.LocalId>0).GroupBy(p => p.Value.LocalId).ToDictionary(g => g.Key,g => g.First().Key);
        var rootMap = (await roots.GetAllAsync(ct)).ToDictionary(r => r.Id);
        // Include all collection tracks, not only tracks belonging to playlists.
        for (var offset = 0;;offset += 1000)
        {
            var page = await tracks.SearchAsync(null,null,true,1000,offset,ct);
            foreach (var track in page)
            {
                if (!trackKeys.TryGetValue(track.Id,out var key))
                {
                    var root = rootMap[track.LibraryRootId];
                    var segments = track.RelativePath.Replace('\\','/').Split('/');
                    var prefix = (root.TraktorDirPrefix ?? "").Split("/:",StringSplitOptions.RemoveEmptyEntries);
                    var volume = root.TraktorVolume ?? Path.GetPathRoot(root.Path)?.TrimEnd('\\','/') ?? "";
                    if (root.TraktorVolume == null) prefix = root.Path[(Path.GetPathRoot(root.Path)?.Length ?? 0)..].Replace('\\','/').Split('/',StringSplitOptions.RemoveEmptyEntries);
                    key = "track:" + new TraktorPath(volume,prefix.Concat(segments[..^1]).ToArray(),segments[^1]).ToCombinedKey();
                    trackKeys[track.Id] = key;
                }
                result.Entities[key] = new() { LocalId = track.Id, ClubReady=track.ClubReady, RawXml = mapping.Entities.GetValueOrDefault(key)?.RawXml, Fields = TrackFields(track) };
            }
            if (page.Count < 1000) break;
        }
        foreach (var node in nodes)
        {
            var members = node.NodeType == PlaylistNodeType.Playlist ? await tracks.SearchAsync(null,[node.Id],true,1_000_000,0,ct) : [];
            result.Entities[identities[node.Id]] = new() { LocalId = node.Id, Kind = node.NodeType == PlaylistNodeType.Playlist ? "playlist" : "folder", Fields = new()
            {
                ["name"] = node.Name, ["parent"] = node.ParentId.HasValue ? identities.GetValueOrDefault(node.ParentId.Value,"") : "",
                ["members"] = node.NodeType == PlaylistNodeType.Playlist ? JsonSerializer.Serialize(members.Select(t => trackKeys[t.Id]).ToArray()) : null
            }};
        }
        return result;
    }
    private static Dictionary<string,string?> TrackFields(Track t) => new() { ["title"] = t.Title,["artist"] = t.Artist,["album"] = t.Album,["genre"] = t.GenreRaw,["comment"] = t.Comment,["key"] = t.MusicalKey,["bpm"] = t.Bpm?.ToString(CultureInfo.InvariantCulture),["rating"] = (t.Rating*51).ToString(CultureInfo.InvariantCulture) };

    public async Task ApplyAsync(SyncPreview preview, CancellationToken ct = default, IProgress<TransferProgress>? progress = null)
        => await checkpoint.ExecuteAsync(()=>ApplyCoreAsync(preview,ct,progress),ct);

    private async Task ApplyCoreAsync(SyncPreview preview, CancellationToken ct, IProgress<TransferProgress>? progress)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var merged = JsonSerializer.Deserialize<SyncState>(JsonSerializer.Serialize(preview.Local))!;
            foreach (var change in preview.Changes.Where(c => c.UseRemote))
            {
                if (change.Field == "$new") merged.Entities[change.Identity] = preview.Remote.Entities[change.Identity];
                else if (change.Field == "$deleted") merged.Entities.Remove(change.Identity);
                else if (merged.Entities.TryGetValue(change.Identity,out var entity)) entity.Fields[change.Field] = change.Remote;
            }
            var rootMap=(await roots.GetAllAsync(ct)).ToDictionary(r=>r.Id);
            var rootPaths=rootMap.Values.GroupBy(r=>r.Path,StringComparer.OrdinalIgnoreCase).ToDictionary(g=>g.Key,g=>g.First(),StringComparer.OrdinalIgnoreCase);
            var trackEntities=merged.Entities.Where(p => p.Value.Kind == "track").ToList();
            var createdTracks=new List<Track>();
            var updatedTracks=new List<(SyncEntity Entity,Track Track)>();
            var completed=0;
            foreach (var pair in trackEntities)
            {
                var entity = pair.Value;
                if (preview.Remote.Entities.TryGetValue(pair.Key,out var original)) entity.RawXml = original.RawXml;
                Track? track = entity.LocalId > 0 ? await tracks.GetByIdAsync(entity.LocalId,ct) : null;
                if (track == null)
                {
                    var path = TraktorPath.FromCombinedKey(pair.Key[6..])!;
                    var rootPath = OperatingSystem.IsWindows() ? path.Volume.TrimEnd('\\','/') + "\\" : "/";
                    if(!rootPaths.TryGetValue(rootPath,out var root))
                    {
                        root=await roots.AddOrGetAsync(rootPath,path.Volume,ct);
                        await roots.SetTraktorMappingAsync(root.Id,path.Volume,"",ct);
                        rootPaths[rootPath]=root; rootMap[root.Id]=root;
                    }
                    var relative = string.Join('/',path.DirSegments.Append(path.FileName));
                    track = await tracks.FindByPathAsync(root.Id,relative,ct);
                    if (track == null)
                    {
                        var info = new FileInfo(Path.Combine(rootPath,relative));
                        var raw=entity.RawXml==null?null:XElement.Parse(entity.RawXml).Element("INFO");
                        var duration=double.TryParse(Attr(raw,"PLAYTIME"),CultureInfo.InvariantCulture,out var seconds)?(int)(seconds*1000):0;
                        track = new() { LibraryRootId = root.Id, RelativePath = relative, Format = Path.GetExtension(relative).TrimStart('.'), FileSizeBytes = info.Exists ? info.Length : 0, FileModifiedAtUtc = info.Exists ? info.LastWriteTimeUtc : DateTime.UnixEpoch, DurationMs=duration, DateAddedUtc = DateTime.UtcNow, DateModifiedUtc = DateTime.UtcNow };
                        createdTracks.Add(track);
                    }
                    entity.LocalId = track.Id;
                }
                FillTechnicalMetadata(track,entity.RawXml);
                track.Title = entity.Fields.GetValueOrDefault("title"); track.Artist = entity.Fields.GetValueOrDefault("artist"); track.Album = entity.Fields.GetValueOrDefault("album");
                track.GenreRaw = entity.Fields.GetValueOrDefault("genre"); track.Comment = entity.Fields.GetValueOrDefault("comment"); track.MusicalKey = entity.Fields.GetValueOrDefault("key");
                track.Bpm = double.TryParse(entity.Fields.GetValueOrDefault("bpm"),CultureInfo.InvariantCulture,out var bpm) ? bpm : null;
                track.Rating = int.TryParse(entity.Fields.GetValueOrDefault("rating"),out var rating) ? (int)Math.Round(rating/51.0) : 0;
                var trackRoot=rootMap[track.LibraryRootId];
                track.MissingSinceUtc=File.Exists(Path.Combine(trackRoot.Path,track.RelativePath))?null:track.MissingSinceUtc??DateTime.UtcNow;
                updatedTracks.Add((entity,track));
                completed++;
                if(completed%100==0 || completed==trackEntities.Count) progress?.Report(new(completed,trackEntities.Count,"Traktor-Tracks übernehmen"));
            }
            progress?.Report(new(completed,trackEntities.Count,"Traktor-Tracks speichern"));
            if(createdTracks.Count>0)
            {
                var ids=await tracks.UpsertBatchAsync(createdTracks,ct);
                for(var i=0;i<createdTracks.Count;i++) createdTracks[i].Id=ids[i];
            }
            await tracks.UpdateBatchAsync(updatedTracks.Select(p=>p.Track).ToArray(),ct);
            foreach(var pair in updatedTracks) pair.Entity.LocalId=pair.Track.Id;
            var pendingNodes = merged.Entities.Where(p => p.Value.Kind != "track").ToList();
            while (pendingNodes.Count > 0)
            {
                var ready = pendingNodes.Where(p => string.IsNullOrEmpty(p.Value.Fields.GetValueOrDefault("parent")) || merged.Entities.GetValueOrDefault(p.Value.Fields["parent"]!)?.LocalId > 0).ToList();
                if (ready.Count == 0) throw new InvalidDataException("Playlist-Hierarchie enthält einen Zyklus oder einen fehlenden Elternordner.");
                foreach (var pair in ready)
                {
                    var entity = pair.Value;
                    if (entity.LocalId == 0)
                    {
                        var parent = entity.Fields.GetValueOrDefault("parent");
                        var created = await playlists.CreateAsync(entity.Fields["name"] ?? "Playlist",entity.Kind == "playlist" ? PlaylistNodeType.Playlist : PlaylistNodeType.Folder,string.IsNullOrEmpty(parent) ? null : merged.Entities[parent].LocalId,ct);
                        entity.LocalId = created.Id;
                    }
                    else
                    {
                        await playlists.RenameAsync(entity.LocalId,entity.Fields["name"] ?? "Playlist",ct);
                        var parent=entity.Fields.GetValueOrDefault("parent");
                        await playlists.MoveAsync(entity.LocalId,string.IsNullOrEmpty(parent)?null:merged.Entities[parent].LocalId,ct);
                    }
                    if (entity.Kind == "playlist")
                    {
                        var old = await tracks.SearchAsync(null,[entity.LocalId],true,1_000_000,0,ct);
                        foreach (var t in old) await playlists.RemoveTrackAsync(entity.LocalId,t.Id,ct);
                        var keys = JsonSerializer.Deserialize<string[]>(entity.Fields.GetValueOrDefault("members") ?? "[]")!;
                        await playlists.AddTracksAsync(entity.LocalId,keys.Where(merged.Entities.ContainsKey).Select(k => merged.Entities[k].LocalId).Distinct().ToArray(),ct);
                    }
                    pendingNodes.Remove(pair);
                }
            }
            foreach (var change in preview.Changes.Where(c => c.UseRemote && c.Field == "$deleted"))
                if (preview.Local.Entities.TryGetValue(change.Identity,out var deleted)) await playlists.DeleteAsync(deleted.LocalId,ct);
            // Keep remote values as baseline while retaining local IDs and original XML.
            foreach (var pair in preview.Remote.Entities)
                if (merged.Entities.TryGetValue(pair.Key,out var entity)) pair.Value.LocalId = entity.LocalId;
            _known = preview.Remote;
            // Locally created identities must also survive restarts and subsequent export.
            foreach (var pair in merged.Entities) if (!_known.Entities.ContainsKey(pair.Key)) { pair.Value.IgnoreRemoteAbsence=true; _known.Entities[pair.Key] = pair.Value; }
            await store.WriteAsync(StateKey,JsonSerializer.Serialize(_known),ct);
            var importedIds=preview.Remote.Entities.Values.Where(e=>e.Kind=="track"&&e.LocalId>0&&!e.IgnoreRemoteAbsence).Select(e=>e.LocalId).Distinct().ToArray();
            await store.WriteAsync("traktor-collection-track-ids",JsonSerializer.Serialize(importedIds),ct);
        }
        finally { _gate.Release(); }
    }

    public async Task ExportAsync(string path, CancellationToken ct = default)
        => await ExportCoreAsync(path,false,ct);

    public async Task ExportClubAsync(string path, CancellationToken ct = default)
        => await ExportCoreAsync(path,true,ct);

    public async Task ExportClubWithFilesAsync(string path,IReadOnlyDictionary<long,string> copiedPaths,CancellationToken ct=default)
        => await ExportCoreAsync(path,true,ct,copiedPaths);

    private async Task ExportCoreAsync(string path,bool clubOnly,CancellationToken ct,IReadOnlyDictionary<long,string>? copiedPaths=null)
    {
        var source=await GetSourcePathAsync(ct);
        if(source!=null&&string.Equals(Path.GetFullPath(source),Path.GetFullPath(path),StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Die Traktor-Quelldatei darf nicht überschrieben werden. Bitte einen separaten Exportpfad wählen.");
        _known = JsonSerializer.Deserialize<SyncState>(await store.ReadAsync(StateKey,ct) ?? "{}") ?? new();
        var snapshot = await CaptureAsync(_known,ct,includeClub:clubOnly);
        if(clubOnly)
        {
            var allowed=snapshot.Entities.Where(p=>p.Value.Kind=="track"&&p.Value.ClubReady&&(copiedPaths==null||copiedPaths.ContainsKey(p.Value.LocalId))).Select(p=>p.Key).ToHashSet();
            var clubIds=(await playlists.GetClubNodeIdsAsync(ct)).ToHashSet();
            foreach(var key in snapshot.Entities.Where(p=>p.Value.Kind=="track"&&!allowed.Contains(p.Key)).Select(p=>p.Key).ToArray()) snapshot.Entities.Remove(key);
            var keep=new HashSet<string>(allowed);
            var assigned=new HashSet<string>();
            foreach(var pair in snapshot.Entities.Where(p=>p.Value.Kind!="track"&&clubIds.Contains(p.Value.LocalId))) keep.Add(pair.Key);
            foreach(var pair in snapshot.Entities.Where(p=>p.Value.Kind=="playlist"))
            {
                var members=JsonSerializer.Deserialize<string[]>(pair.Value.Fields.GetValueOrDefault("members")??"[]")!.Where(allowed.Contains).ToArray();
                pair.Value.Fields["members"]=JsonSerializer.Serialize(members);
                if(!clubIds.Contains(pair.Value.LocalId)) continue;
                assigned.UnionWith(members);
                var current=pair.Key;
                while(keep.Add(current) && snapshot.Entities.TryGetValue(current,out var node))
                { current=node.Fields.GetValueOrDefault("parent")??""; if(current.Length==0) break; }
            }
            foreach(var key in snapshot.Entities.Keys.Where(k=>!keep.Contains(k)).ToArray()) snapshot.Entities.Remove(key);
            var clubRoot=snapshot.Entities.FirstOrDefault(p=>p.Value.Kind=="folder"&&clubIds.Contains(p.Value.LocalId)&&string.IsNullOrEmpty(p.Value.Fields.GetValueOrDefault("parent"))).Key??"";
            var clubIdentities=JsonSerializer.Deserialize<Dictionary<long,string>>(await store.ReadAsync("club-export-identities",ct)??"{}")??new();
            foreach(var id in new long[]{-1,-2}) clubIdentities.TryAdd(id,"node:uuid:"+Guid.NewGuid().ToString("N"));
            await store.WriteAsync("club-export-identities",JsonSerializer.Serialize(clubIdentities),ct);
            snapshot.Entities[clubIdentities[-1]]=new() { Kind="playlist",Fields=new() { ["name"]="Club Collection",["parent"]=clubRoot,["members"]=JsonSerializer.Serialize(allowed.ToArray()) } };
            snapshot.Entities[clubIdentities[-2]]=new() { Kind="playlist",Fields=new() { ["name"]="Nicht einsortiert",["parent"]=clubRoot,["members"]=JsonSerializer.Serialize(allowed.Except(assigned).ToArray()) } };
        }
        // Every exported playlist gets a stable UUID; remap path-based import identities too.
        var renamed=snapshot.Entities.Where(p=>p.Value.Kind!="track" && !p.Key.StartsWith("node:uuid:")).ToDictionary(p=>p.Key,p=>"node:uuid:"+Guid.NewGuid().ToString("N"));
        foreach(var pair in renamed) { var entity=snapshot.Entities[pair.Key]; snapshot.Entities.Remove(pair.Key); snapshot.Entities[pair.Value]=entity; }
        foreach(var entity in snapshot.Entities.Values.Where(e=>e.Kind!="track"))
            if(entity.Fields.GetValueOrDefault("parent") is { } parent && renamed.TryGetValue(parent,out var replacement)) entity.Fields["parent"]=replacement;
        var collection = new XElement("COLLECTION",new XAttribute("ENTRIES",snapshot.Entities.Values.Count(e => e.Kind == "track")));
        TraktorPath ExportLocation(string key)
        {
            var entity=snapshot.Entities[key];
            if(copiedPaths!=null&&copiedPaths.TryGetValue(entity.LocalId,out var copy))
            {
                var full=Path.GetFullPath(copy);var root=Path.GetPathRoot(full)!;
                var segments=full[root.Length..].Replace('\\','/').Split('/');
                return new TraktorPath(root.TrimEnd('\\','/'),segments[..^1],segments[^1]);
            }
            return TraktorPath.FromCombinedKey(key[6..])!;
        }
        foreach (var pair in snapshot.Entities.Where(p => p.Value.Kind == "track"))
        {
            ct.ThrowIfCancellationRequested();
            var entity = pair.Value;
            var entry = entity.RawXml == null ? new XElement("ENTRY") : XElement.Parse(entity.RawXml);
            var location = ExportLocation(pair.Key);
            void Set(XElement element,string attribute,string? value) => element.SetAttributeValue(attribute,value);
            XElement Child(string name) { var c = entry.Element(name); if(c != null) return c; c = new(name); entry.Add(c); return c; }
            Set(entry,"TITLE",entity.Fields.GetValueOrDefault("title")); Set(entry,"ARTIST",entity.Fields.GetValueOrDefault("artist"));
            var loc = Child("LOCATION"); Set(loc,"VOLUME",location.Volume); Set(loc,"DIR",location.ToLocationDir()); Set(loc,"FILE",location.FileName);
            if(copiedPaths!=null)foreach(var attribute in loc.Attributes().Where(a=>a.Name.LocalName.Contains("VOLUME",StringComparison.OrdinalIgnoreCase)&&a.Name.LocalName!="VOLUME").ToArray())attribute.Remove();
            Set(Child("ALBUM"),"TITLE",entity.Fields.GetValueOrDefault("album"));
            foreach (var f in new[] { ("genre","GENRE"),("comment","COMMENT"),("key","KEY"),("rating","RANKING") }) Set(Child("INFO"),f.Item2,entity.Fields.GetValueOrDefault(f.Item1));
            Set(Child("TEMPO"),"BPM",entity.Fields.GetValueOrDefault("bpm")); collection.Add(entry);
        }
        XElement Node(string id, HashSet<string> ancestors)
        {
            if (!ancestors.Add(id)) throw new InvalidDataException("Zyklische Playlist-Hierarchie.");
            var e = snapshot.Entities[id];
            var n = new XElement("NODE",new XAttribute("TYPE",e.Kind == "playlist" ? "PLAYLIST" : "FOLDER"),new XAttribute("NAME",e.Fields["name"] ?? ""));
            if (e.Kind == "playlist")
            {
                var keys = JsonSerializer.Deserialize<string[]>(e.Fields.GetValueOrDefault("members") ?? "[]")!;
                var uuid = id.StartsWith("node:uuid:") ? id[10..] : Guid.NewGuid().ToString("N");
                n.Add(new XElement("PLAYLIST",new XAttribute("UUID",uuid),new XAttribute("TYPE","LIST"),new XAttribute("ENTRIES",keys.Length),keys.Select(k => new XElement("ENTRY",new XElement("PRIMARYKEY",new XAttribute("TYPE","TRACK"),new XAttribute("KEY",ExportLocation(k).ToCombinedKey()))))));
            }
            else n.Add(Subnodes(id,ancestors));
            ancestors.Remove(id); return n;
        }
        XElement Subnodes(string parent, HashSet<string> ancestors)
        {
            var children = snapshot.Entities.Where(p => p.Value.Kind != "track" && p.Value.Fields.GetValueOrDefault("parent") == parent).Select(p => Node(p.Key,ancestors)).ToArray();
            return new XElement("SUBNODES",new XAttribute("COUNT",children.Length),children);
        }
        var document = new XDocument(new XElement("NML",new XAttribute("VERSION","19"),new XElement("HEAD",new XAttribute("PROGRAM","Traktor - Native Instruments")),new XElement("MUSICFOLDERS"),collection,new XElement("SETS",new XAttribute("ENTRIES",0)),new XElement("PLAYLISTS",new XElement("NODE",new XAttribute("TYPE","FOLDER"),new XAttribute("NAME","$ROOT"),Subnodes("",new())))));
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { await Task.Run(() => document.Save(temporary),ct); ct.ThrowIfCancellationRequested(); File.Move(temporary,path,true); }
        finally { if(File.Exists(temporary)) File.Delete(temporary); }
        if(!clubOnly)
        {
            _pending = snapshot;
            await store.WriteAsync("traktor-pending-export",JsonSerializer.Serialize(snapshot),ct);
        }
    }
    public async Task ConfirmExportAsync(CancellationToken ct = default)
    {
        _pending ??= JsonSerializer.Deserialize<SyncState>(await store.ReadAsync("traktor-pending-export",ct) ?? "null");
        if (_pending == null) throw new InvalidOperationException("Kein unbestätigter Export vorhanden.");
        await store.WriteAsync(StateKey,JsonSerializer.Serialize(_pending),ct);
        await store.WriteAsync("traktor-pending-export","null",ct); _pending = null;
    }
}
