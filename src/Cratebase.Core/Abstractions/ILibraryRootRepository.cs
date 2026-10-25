using Cratebase.Core.Models;

namespace Cratebase.Core.Abstractions;

public interface ILibraryRootRepository
{
    Task<IReadOnlyList<LibraryRoot>> GetAllAsync(CancellationToken ct = default);

    /// <summary>Legt einen Root an, oder liefert den bestehenden zurück, falls der Pfad schon überwacht wird.</summary>
    Task<LibraryRoot> AddOrGetAsync(string path, string label, CancellationToken ct = default);

    Task UpdateLastScannedAsync(long id, DateTime whenUtc, CancellationToken ct = default);
    Task RelocateAsync(long id, string localPath, string relativePrefix = "", CancellationToken ct = default);

    /// <summary>Speichert, wie Traktor diesen Root selbst benennt (s. LibraryRoot.TraktorVolume-Doku) -
    /// vom Traktor-Import automatisch gelernt (s. TraktorPathResolver), für den NML-Export genutzt.</summary>
    Task SetTraktorMappingAsync(long id, string volume, string dirPrefix, CancellationToken ct = default);
}
