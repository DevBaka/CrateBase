using Cratebase.Core.Models;

namespace Cratebase.Core.Abstractions;

/// <summary>Persistiert die Playlist-Favoriten-Profile (s. FavoriteProfile-Doku) - wie
/// IQuickAccessStore bewusst außerhalb der SQLite-DB (reine App-Einstellung).</summary>
public interface IFavoriteProfileStore
{
    Task<FavoriteProfileSettings> LoadAsync(CancellationToken ct = default);

    Task SaveAsync(FavoriteProfileSettings settings, CancellationToken ct = default);
}
