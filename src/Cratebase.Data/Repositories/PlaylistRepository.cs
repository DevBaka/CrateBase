using Cratebase.Core.Abstractions;
using Cratebase.Core.Models;
using Dapper;

namespace Cratebase.Data.Repositories;

public sealed class PlaylistRepository(SqliteConnectionFactory connectionFactory) : IPlaylistRepository
{
    internal const string ClubNodesSql="WITH RECURSIVE club_nodes(id) AS (SELECT CAST(json AS INTEGER) FROM workspace_state WHERE key='club-playlist-root' UNION ALL SELECT p.id FROM playlist_node p JOIN club_nodes c ON p.parent_id=c.id) SELECT id FROM club_nodes";
    public async Task<long> EnsureClubRootAsync(CancellationToken ct=default)
    {
        using var connection=connectionFactory.CreateConnection(); using var transaction=connection.BeginTransaction();
        var id=await connection.ExecuteScalarAsync<long?>(new CommandDefinition("SELECT p.id FROM playlist_node p JOIN workspace_state s ON p.id=CAST(s.json AS INTEGER) WHERE s.key='club-playlist-root' AND p.node_type='folder'",transaction:transaction,cancellationToken:ct));
        if(id==null)
        {
            id=await connection.ExecuteScalarAsync<long>(new CommandDefinition("INSERT INTO playlist_node(parent_id,node_type,name,sort_order,created_at,updated_at) VALUES(NULL,'folder','Club Collection',0,@now,@now) RETURNING id",new {now=SqliteDateTime.ToText(DateTime.UtcNow)},transaction,cancellationToken:ct));
            await connection.ExecuteAsync(new CommandDefinition("INSERT INTO workspace_state(key,json) VALUES('club-playlist-root',@id) ON CONFLICT(key) DO UPDATE SET json=excluded.json",new {id=id.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)},transaction,cancellationToken:ct));
        }
        transaction.Commit(); return id.Value;
    }
    public async Task<IReadOnlyCollection<long>> GetClubNodeIdsAsync(CancellationToken ct=default)
    {
        using var connection=connectionFactory.CreateConnection();
        return (await connection.QueryAsync<long>(new CommandDefinition(ClubNodesSql,cancellationToken:ct))).ToArray();
    }

    private const string SelectColumns = """
        id AS Id, parent_id AS ParentId, node_type AS NodeTypeRaw, name AS Name,
        sort_order AS SortOrder, created_at AS CreatedAtUtc, updated_at AS UpdatedAtUtc
        """;

    public async Task<IReadOnlyList<PlaylistNode>> GetAllNodesAsync(CancellationToken ct = default)
    {
        using var connection = connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<NodeRow>(new CommandDefinition(
            $"SELECT {SelectColumns} FROM playlist_node ORDER BY parent_id, sort_order, name;",
            cancellationToken: ct));
        return rows.Select(ToModel).ToList();
    }

    public async Task<PlaylistNode> CreateAsync(string name, PlaylistNodeType type, long? parentId, CancellationToken ct = default)
    {
        using var connection = connectionFactory.CreateConnection();
        var now = SqliteDateTime.ToText(DateTime.UtcNow);

        var newId = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            """
            INSERT INTO playlist_node (parent_id, node_type, name, sort_order, created_at, updated_at)
            VALUES (
                @parentId, @nodeType, @name,
                (SELECT COALESCE(MAX(sort_order), -1) + 1 FROM playlist_node WHERE parent_id IS @parentId),
                @now, @now
            )
            RETURNING id;
            """,
            new { parentId, nodeType = ToRaw(type), name, now },
            cancellationToken: ct));

        return new PlaylistNode
        {
            Id = newId,
            ParentId = parentId,
            NodeType = type,
            Name = name,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
        };
    }

    public async Task RenameAsync(long nodeId, string newName, CancellationToken ct = default)
    {
        using var connection = connectionFactory.CreateConnection();
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE playlist_node SET name = @newName, updated_at = @now WHERE id = @nodeId;",
            new { nodeId, newName, now = SqliteDateTime.ToText(DateTime.UtcNow) },
            cancellationToken: ct));
    }

    public async Task DeleteAsync(long nodeId, CancellationToken ct = default)
    {
        using var connection = connectionFactory.CreateConnection();
        if(await IsClubRootAsync(connection,nodeId,ct)) throw new InvalidOperationException("Die Club Collection kann nicht gelöscht werden.");
        await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM playlist_node WHERE id = @nodeId;", new { nodeId }, cancellationToken: ct));
    }

    public async Task MoveAsync(long nodeId, long? parentId, CancellationToken ct = default)
    {
        using var connection = connectionFactory.CreateConnection();
        if(await IsClubRootAsync(connection,nodeId,ct)) throw new InvalidOperationException("Die Club Collection kann nicht verschoben werden.");
        using var transaction=connection.BeginTransaction();
        var exists=await connection.ExecuteScalarAsync<int>(new CommandDefinition("SELECT COUNT(*) FROM playlist_node WHERE id=@nodeId",new {nodeId},transaction,cancellationToken:ct));
        if(exists!=1) throw new InvalidOperationException("Die Playlist oder Gruppe existiert nicht mehr.");
        if (parentId.HasValue)
        {
            var target=await connection.ExecuteScalarAsync<int>(new CommandDefinition("SELECT COUNT(*) FROM playlist_node WHERE id=@parentId AND node_type='folder'",new {parentId},transaction,cancellationToken:ct));
            if(target!=1) throw new InvalidOperationException("Das Ziel muss eine vorhandene Playlist-Gruppe sein.");
            var cycle = await connection.ExecuteScalarAsync<int>(new CommandDefinition("WITH RECURSIVE children(id) AS (SELECT @nodeId UNION ALL SELECT p.id FROM playlist_node p JOIN children c ON p.parent_id=c.id) SELECT COUNT(*) FROM children WHERE id=@parentId",new { nodeId,parentId },transaction,cancellationToken:ct));
            if(cycle>0) throw new InvalidOperationException("Eine Playlist-Gruppe kann nicht in sich selbst verschoben werden.");
        }
        var wasClub=await connection.ExecuteScalarAsync<int>(new CommandDefinition($"SELECT COUNT(*) FROM ({ClubNodesSql}) WHERE id=@nodeId",new {nodeId},transaction,cancellationToken:ct))>0;
        await connection.ExecuteAsync(new CommandDefinition("UPDATE playlist_node SET parent_id=@parentId,sort_order=(SELECT COALESCE(MAX(sort_order),-1)+1 FROM playlist_node WHERE parent_id IS @parentId),updated_at=@now WHERE id=@nodeId AND parent_id IS NOT @parentId",new { nodeId,parentId,now=SqliteDateTime.ToText(DateTime.UtcNow) },transaction,cancellationToken:ct));
        if(!wasClub) await connection.ExecuteAsync(new CommandDefinition($"UPDATE track SET club_ready=1 WHERE id IN (WITH RECURSIVE moved(id) AS (SELECT @nodeId UNION ALL SELECT p.id FROM playlist_node p JOIN moved m ON p.parent_id=m.id) SELECT pt.track_id FROM playlist_track pt JOIN moved m ON m.id=pt.playlist_node_id) AND EXISTS(SELECT 1 FROM ({ClubNodesSql}) c WHERE c.id=@nodeId)",new {nodeId},transaction,cancellationToken:ct));
        transaction.Commit();
    }

    private static async Task<bool> IsClubRootAsync(Microsoft.Data.Sqlite.SqliteConnection connection,long id,CancellationToken ct)
        => await connection.ExecuteScalarAsync<int>(new CommandDefinition("SELECT COUNT(*) FROM workspace_state WHERE key='club-playlist-root' AND CAST(json AS INTEGER)=@id",new {id},cancellationToken:ct))>0;

    public async Task AddTracksAsync(long playlistNodeId, IReadOnlyCollection<long> trackIds, CancellationToken ct = default)
    {
        if (trackIds.Count == 0)
        {
            return;
        }

        using var connection = connectionFactory.CreateConnection();
        using var transaction = connection.BeginTransaction();

        if(await connection.ExecuteScalarAsync<int>(new CommandDefinition("SELECT COUNT(*) FROM playlist_node WHERE id=@playlistNodeId AND node_type='playlist'",new {playlistNodeId},transaction,cancellationToken:ct))!=1)
            throw new InvalidOperationException("Nur Playlisten nehmen Tracks auf.");
        var club=await connection.ExecuteScalarAsync<int>(new CommandDefinition($"SELECT COUNT(*) FROM ({ClubNodesSql}) WHERE id=@playlistNodeId",new {playlistNodeId},transaction,cancellationToken:ct))>0;
        if(club) foreach(var ids in trackIds.Chunk(400))
            await connection.ExecuteAsync(new CommandDefinition("UPDATE track SET club_ready=1 WHERE id IN @ids",new {ids},transaction,cancellationToken:ct));

        var nextPosition = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COALESCE(MAX(position), -1) + 1 FROM playlist_track WHERE playlist_node_id = @playlistNodeId;",
            new { playlistNodeId }, transaction, cancellationToken: ct));

        var now = SqliteDateTime.ToText(DateTime.UtcNow);
        var rows = trackIds.Select((trackId, offset) => new
        {
            playlistNodeId,
            trackId,
            position = nextPosition + offset,
            dateAdded = now,
        });

        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT OR IGNORE INTO playlist_track (playlist_node_id, track_id, position, date_added)
            VALUES (@playlistNodeId, @trackId, @position, @dateAdded);
            """,
            rows, transaction, cancellationToken: ct));

        transaction.Commit();
    }

    public async Task RemoveTrackAsync(long playlistNodeId, long trackId, CancellationToken ct = default)
    {
        using var connection = connectionFactory.CreateConnection();
        await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM playlist_track WHERE playlist_node_id = @playlistNodeId AND track_id = @trackId;",
            new { playlistNodeId, trackId }, cancellationToken: ct));
    }

    private static string ToRaw(PlaylistNodeType type) => type switch
    {
        PlaylistNodeType.Folder => "folder",
        PlaylistNodeType.Playlist => "playlist",
        _ => throw new ArgumentOutOfRangeException(nameof(type)),
    };

    private static PlaylistNodeType FromRaw(string raw) => raw switch
    {
        "folder" => PlaylistNodeType.Folder,
        "playlist" => PlaylistNodeType.Playlist,
        _ => throw new InvalidOperationException($"Unbekannter playlist_node.node_type-Wert: '{raw}'."),
    };

    private static PlaylistNode ToModel(NodeRow row) => new()
    {
        Id = row.Id,
        ParentId = row.ParentId,
        NodeType = FromRaw(row.NodeTypeRaw),
        Name = row.Name,
        SortOrder = row.SortOrder,
        CreatedAtUtc = row.CreatedAtUtc,
        UpdatedAtUtc = row.UpdatedAtUtc,
    };

    /// <summary>
    /// Bewusst eine plain Klasse mit Default-Ctor statt eines Records mit Primary-Konstruktor:
    /// Dapper materialisiert Records ohne Default-Ctor über eine strikte, positionsbasierte
    /// Konstruktor-Zuordnung, die schon bei kleinen Typ-Abweichungen (INTEGER-Spalte vs. int
    /// statt long, TEXT-Spalte vs. DateTime) mit einer InvalidOperationException scheitert.
    /// Der property-basierte Weg über einen Default-Ctor ist toleranter (nutzt Convert.ChangeType).
    /// </summary>
    private sealed class NodeRow
    {
        public long Id { get; set; }
        public long? ParentId { get; set; }
        public string NodeTypeRaw { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public int SortOrder { get; set; }
        public DateTime CreatedAtUtc { get; set; }
        public DateTime UpdatedAtUtc { get; set; }
    }
}
