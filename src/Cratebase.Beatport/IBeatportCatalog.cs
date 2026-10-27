namespace Cratebase.Beatport;
// Independent of Avalonia and the host: reusable by a future Cratebase module.
public interface IBeatportCatalog
{
    Task<IReadOnlyList<NamedEntity>> ArtistsAsync(string query, CancellationToken ct);
    Task<IReadOnlyList<NamedEntity>> GenresAsync(CancellationToken ct);
    Task<IReadOnlyList<NamedEntity>> SubgenresAsync(long genre, CancellationToken ct);
    Task<IReadOnlyList<Track>> TracksAsync(long? genre, long? subgenre, long? artist, CancellationToken ct);
    Task<IReadOnlyList<NamedEntity>> PlaylistsAsync(CancellationToken ct);
    Task<IReadOnlyList<PlaylistItem>> PlaylistTracksAsync(long playlist, CancellationToken ct);
    Task<NamedEntity> CreatePlaylistAsync(string name, CancellationToken ct);
    Task AddAsync(long playlist, IEnumerable<long> tracks, CancellationToken ct);
    Task RemoveAsync(long playlist, long item, CancellationToken ct);
}
