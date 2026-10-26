using Cratebase.Core.Abstractions;
using Cratebase.Core.Models;
using Dapper;

namespace Cratebase.Data.Repositories;

public sealed class LibraryQuery(SqliteConnectionFactory factory) : ILibraryQuery
{
    private static readonly IReadOnlyDictionary<string,string> Columns=new Dictionary<string,string>
    {
        ["club"]="t.club_ready",["extended"]="t.extended",["intro"]="t.intro",["trash"]="t.trash",["title"]="COALESCE(NULLIF(t.title,''),t.relative_path)",["artist"]="t.artist",["album"]="t.album",["albumartist"]="t.album_artist",["tracknumber"]="t.track_number",
        ["bpm"]="t.bpm",["key"]="t.musical_key",["duration"]="t.duration_ms/1000.0",["format"]="t.format",["bitrate"]="t.bitrate_kbps",["samplerate"]="t.sample_rate_hz",["channels"]="t.channels",["filesize"]="t.file_size/1000000.0",
        ["genre"]="t.genre_raw",["subgenre"]="t.sub_genre",["year"]="t.year",["rating"]="t.rating",["playcount"]="t.play_count",["comment"]="t.comment",["comment2"]="t.comment2",["color"]="t.color_tag",["lastplayed"]="t.last_played_at",["added"]="t.date_added",["modified"]="t.date_modified",["filemodified"]="t.file_mtime",["missing"]="t.missing_since",["path"]="t.relative_path",["availability"]="CASE WHEN t.missing_since IS NOT NULL THEN 'Datei nicht erreichbar' ELSE '' END"
    };
    private static string Column(string field)=>Columns.TryGetValue(field,out var column)?column:throw new ArgumentException("Unbekanntes Metadatenfeld: "+field);
    private static string Escape(string value)=>value.Replace("~","~~").Replace("%","~%").Replace("_","~_");
    private static (string Sql, DynamicParameters Args) Filter(TrackQuery query)
    {
        var args = new DynamicParameters();
        var clauses = new List<string> { "1=1" };
        if(query.Source.Kind==LibraryTargetKind.UnreachableFiles)clauses.Add("t.missing_since IS NOT NULL");
        if (query.Source.Kind == LibraryTargetKind.ClubCollection) clauses.Add("t.club_ready=1");
        if (query.Source.Kind == LibraryTargetKind.ClubUnsorted)
            clauses.Add($"t.club_ready=1 AND NOT EXISTS(SELECT 1 FROM playlist_track pt JOIN ({PlaylistRepository.ClubNodesSql}) c ON c.id=pt.playlist_node_id WHERE pt.track_id=t.id)");
        if (query.Source.Kind == LibraryTargetKind.TraktorCollection)
            clauses.Add("t.id IN (SELECT CAST(imported.value AS INTEGER) FROM workspace_state s, json_each(s.json) imported WHERE s.key='traktor-collection-track-ids')");
        if (query.Source.Kind is LibraryTargetKind.Playlist or LibraryTargetKind.PlaylistFolder)
        {
            args.Add("playlist", query.Source.PlaylistId);
            clauses.Add($"(t.club_ready=1 OR NOT EXISTS(SELECT 1 FROM ({PlaylistRepository.ClubNodesSql}) c WHERE c.id=@playlist))");
            clauses.Add(query.Source.Kind == LibraryTargetKind.Playlist
                ? "EXISTS (SELECT 1 FROM playlist_track pt WHERE pt.track_id=t.id AND pt.playlist_node_id=@playlist)"
                : "t.id IN (WITH RECURSIVE nodes(id) AS (SELECT @playlist UNION ALL SELECT p.id FROM playlist_node p JOIN nodes n ON p.parent_id=n.id) SELECT pt.track_id FROM playlist_track pt JOIN nodes n ON n.id=pt.playlist_node_id)");
        }
        if (query.Source.Kind == LibraryTargetKind.Directory)
        {
            args.Add("directory", System.IO.Path.GetFullPath(query.Source.Path!).TrimEnd('\\', '/').Replace('\\', '/') + "/");
            clauses.Add("rtrim(replace(r.path,'\\','/'),'/') || '/' || t.relative_path LIKE @directory || '%' ESCAPE '~'");
            // Escape LIKE metacharacters in physical paths.
            args.Add("directory", ((string)args.Get<string>("directory")).Replace("~", "~~").Replace("%", "~%").Replace("_", "~_"));
        }
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var fields=query.SearchField=="all"?TrackMetadata.Fields:[TrackMetadata.Get(query.SearchField)];
            var search=query.Search.Trim();
            if(query.SearchField!="all"&&TrackMetadata.Get(query.SearchField).Kind==MetadataKind.Boolean)
            {
                args.Add("searchFlag",TrackMetadata.Parse(TrackMetadata.Get(query.SearchField),search)); clauses.Add(Column(query.SearchField)+"=@searchFlag");
            }
            else
            {
                if(query.SearchField!="all"&&TrackMetadata.Get(query.SearchField).Kind==MetadataKind.Number) search=search.Replace(',','.');
                args.Add("search","%"+Escape(search)+"%");
                clauses.Add("("+string.Join(" OR ",fields.Select(f=>$"CAST({Column(f.Key)} AS TEXT) LIKE @search ESCAPE '~'"))+")");
            }
        }
        var filterIndex=0;
        var smartClauses=new List<string>();
        foreach(var entry in (query.Filters??[]).Select(f=>(Filter:f,Smart:false)).Concat((query.SmartRules??[]).Select(f=>(Filter:f,Smart:true))))
        {
            var filter=entry.Filter;
            var destination=entry.Smart?smartClauses:clauses;
            TrackMetadata.Validate(filter);
            var field=TrackMetadata.Get(filter.Field); var column=Column(field.Key);
            var parameter="filter"+filterIndex++;
            if(filter.Operator is MetadataOperator.IsEmpty or MetadataOperator.IsNotEmpty)
            {
                var empty=$"({column} IS NULL OR TRIM(CAST({column} AS TEXT))='')";
                destination.Add(filter.Operator==MetadataOperator.IsEmpty?empty:"NOT "+empty); continue;
            }
            var value=TrackMetadata.Parse(field,filter.Value);
            if(filter.Operator is MetadataOperator.Contains or MetadataOperator.NotContains)
            {
                args.Add(parameter,"%"+Escape((string)value)+"%");
                destination.Add($"COALESCE({column},'') {(filter.Operator==MetadataOperator.NotContains?"NOT LIKE":"LIKE")} @{parameter} ESCAPE '~'"); continue;
            }
            args.Add(parameter,value);
            var operation=filter.Operator switch { MetadataOperator.Equals=>"=",MetadataOperator.NotEquals=>"<>",MetadataOperator.GreaterThan=>">",MetadataOperator.GreaterOrEqual=>">=",MetadataOperator.LessThan=>"<",MetadataOperator.LessOrEqual=>"<=",_=>throw new ArgumentException("Ungültiger Filtervergleich.") };
            var expression=field.Kind==MetadataKind.Date?$"date({column})":field.Kind==MetadataKind.Text?$"{column} COLLATE NOCASE":column;
            destination.Add($"{expression} {operation} @{parameter}");
        }
        if(smartClauses.Count>0)clauses.Add("("+string.Join(query.SmartMatchAny?" OR ":" AND ",smartClauses)+")");
        return ("FROM track t JOIN library_root r ON r.id=t.library_root_id WHERE " + string.Join(" AND ", clauses), args);
    }

    private async Task<TrackQuery> ResolveAsync(TrackQuery query,CancellationToken ct)
    {
        if(query.Source.Kind!=LibraryTargetKind.SmartPlaylist)return query;
        using var connection=factory.CreateConnection();
        var json=await connection.QuerySingleOrDefaultAsync<string>(new CommandDefinition("SELECT json FROM workspace_state WHERE key=@key",new {key=SmartPlaylist.StateKey},cancellationToken:ct));
        var definition=(System.Text.Json.JsonSerializer.Deserialize<List<SmartPlaylist>>(json??"[]")??[]).FirstOrDefault(p=>p.Id==query.Source.Path)
            ??throw new InvalidOperationException("Diese Smart-Playlist existiert nicht mehr.");
        definition.Validate();
        return query with {Source=definition.Source,SmartRules=definition.Rules,SmartMatchAny=definition.MatchAny,
            Sort=query.Sort=="default"?definition.Sort:query.Sort};
    }

    public async Task<IReadOnlyList<Track>> QueryAsync(TrackQuery query, CancellationToken ct = default)
    {
        query=await ResolveAsync(query,ct);
        using var connection = factory.CreateConnection();
        var (filter, args) = Filter(query);
        var direction=query.Descending?"DESC":"ASC";
        var order=query.Sort=="default"
            ?query.Source.Kind==LibraryTargetKind.Playlist?$"(SELECT position FROM playlist_track WHERE track_id=t.id AND playlist_node_id=@playlist) {direction}":$"t.artist COLLATE NOCASE {direction}, t.title COLLATE NOCASE {direction}"
            :$"{Column(query.Sort)} {(TrackMetadata.Get(query.Sort).Kind==MetadataKind.Text?"COLLATE NOCASE":"")} {direction}";
        args.Add("limit", Math.Clamp(query.Limit, 1, 2000)); args.Add("offset", Math.Max(0, query.Offset));
        var result = await connection.QueryAsync<Track>(new CommandDefinition($"SELECT {TrackRepository.SelectColumns} {filter} ORDER BY {order}, t.id LIMIT @limit OFFSET @offset", args, cancellationToken: ct));
        return result.ToList();
    }

    public async Task<TrackTotals> TotalsAsync(TrackQuery query, CancellationToken ct = default)
    {
        query=await ResolveAsync(query,ct);
        using var connection = factory.CreateConnection();
        var (filter, args) = Filter(query);
        return await connection.QuerySingleAsync<TrackTotals>(new CommandDefinition($"SELECT COUNT(*) AS Count, COALESCE(SUM(t.file_size),0) AS Bytes, COALESCE(SUM(t.duration_ms),0) AS DurationMs, COALESCE(SUM(CASE WHEN t.file_size=0 THEN 1 ELSE 0 END),0) AS UnknownSizes {filter}", args, cancellationToken: ct));
    }

    public async Task SetClubReadyAsync(IReadOnlyCollection<long> ids, bool value, CancellationToken ct = default)
        => await SetFlagAsync(ids,"club",value,ct);

    public async Task SetFlagAsync(IReadOnlyCollection<long> ids,string field,bool value,CancellationToken ct=default)
    {
        if(field is not ("club" or "extended" or "intro" or "trash")) throw new ArgumentException("Dieses Feld ist keine Track-Markierung.");
        using var connection = factory.CreateConnection();
        using var transaction = connection.BeginTransaction();
        foreach (var chunk in ids.Chunk(400))
            await connection.ExecuteAsync(new CommandDefinition($"UPDATE track SET {Column(field).Replace("t.","")}=@value WHERE id IN @ids", new { value, ids = chunk }, transaction, cancellationToken: ct));
        transaction.Commit();
    }
}
