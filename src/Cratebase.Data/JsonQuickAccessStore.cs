using System.Text.Json;
using Cratebase.Core.Abstractions;
using Cratebase.Core.Models;

namespace Cratebase.Data;

/// <summary>Einfache JSON-Datei statt SQLite-Tabelle (s. IQuickAccessStore-Doku).</summary>
public sealed class JsonQuickAccessStore(string filePath) : IQuickAccessStore
{
    public async Task<IReadOnlyList<QuickAccessFolder>> LoadAsync(CancellationToken ct = default)
    {
        if (!File.Exists(filePath))
        {
            return [];
        }

        try
        {
            await using var stream = File.OpenRead(filePath);
            var folders = await JsonSerializer.DeserializeAsync<List<QuickAccessFolder>>(stream, cancellationToken: ct);
            return folders ?? [];
        }
        catch (Exception)
        {
            // Kaputte/unlesbare Einstellungsdatei darf den App-Start nicht verhindern.
            return [];
        }
    }

    public async Task SaveAsync(IReadOnlyList<QuickAccessFolder> folders, CancellationToken ct = default)
    {
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using var stream = File.Create(filePath);
        await JsonSerializer.SerializeAsync(stream, folders, new JsonSerializerOptions { WriteIndented = true }, ct);
    }
}
