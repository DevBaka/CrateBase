using Cratebase.Core.Models;

namespace Cratebase.Core.Abstractions;

/// <summary>
/// Persistiert die vom Nutzer angelegten Schnellzugriff-Ordner (s. QuickAccessFolder-Doku).
/// Bewusst außerhalb der SQLite-DB (einfache Nutzereinstellung, keine Domänendaten, für die
/// Cratebase.Data/SQL zuständig ist) - Implementierung schreibt eine kleine JSON-Datei.
/// </summary>
public interface IQuickAccessStore
{
    Task<IReadOnlyList<QuickAccessFolder>> LoadAsync(CancellationToken ct = default);

    Task SaveAsync(IReadOnlyList<QuickAccessFolder> folders, CancellationToken ct = default);
}
