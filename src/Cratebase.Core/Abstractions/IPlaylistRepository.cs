using Cratebase.Core.Models;

namespace Cratebase.Core.Abstractions;

public interface IPlaylistRepository
{
    /// <summary>Der komplette Baum in einem Rutsch (flache Liste, ParentId bildet die Hierarchie) – für ~zehntausende Knoten unproblematisch, im Gegensatz zur Track-Suche.</summary>
    Task<IReadOnlyList<PlaylistNode>> GetAllNodesAsync(CancellationToken ct = default);
    Task<long> EnsureClubRootAsync(CancellationToken ct = default);
    Task<IReadOnlyCollection<long>> GetClubNodeIdsAsync(CancellationToken ct = default);

    Task<PlaylistNode> CreateAsync(string name, PlaylistNodeType type, long? parentId, CancellationToken ct = default);

    Task RenameAsync(long nodeId, string newName, CancellationToken ct = default);
    Task MoveAsync(long nodeId, long? parentId, CancellationToken ct = default);

    /// <summary>Löscht einen Knoten. Bei Ordnern kaskadiert das (per FK ON DELETE CASCADE) auf alle Kinder.</summary>
    Task DeleteAsync(long nodeId, CancellationToken ct = default);

    Task AddTracksAsync(long playlistNodeId, IReadOnlyCollection<long> trackIds, CancellationToken ct = default);

    Task RemoveTrackAsync(long playlistNodeId, long trackId, CancellationToken ct = default);
}
