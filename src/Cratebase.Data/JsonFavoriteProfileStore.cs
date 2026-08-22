using System.Text.Json;
using Cratebase.Core.Abstractions;
using Cratebase.Core.Models;

namespace Cratebase.Data;

/// <summary>Einfache JSON-Datei statt SQLite-Tabelle (s. IFavoriteProfileStore-Doku).</summary>
public sealed class JsonFavoriteProfileStore(string filePath) : IFavoriteProfileStore
{
    public async Task<FavoriteProfileSettings> LoadAsync(CancellationToken ct = default)
    {
        if (!File.Exists(filePath))
        {
            return new FavoriteProfileSettings();
        }

        try
        {
            await using var stream = File.OpenRead(filePath);
            var settings = await JsonSerializer.DeserializeAsync<FavoriteProfileSettings>(stream, cancellationToken: ct);
            return settings ?? new FavoriteProfileSettings();
        }
        catch (Exception)
        {
            // Kaputte/unlesbare Einstellungsdatei darf den App-Start nicht verhindern.
            return new FavoriteProfileSettings();
        }
    }

    public async Task SaveAsync(FavoriteProfileSettings settings, CancellationToken ct = default)
    {
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using var stream = File.Create(filePath);
        await JsonSerializer.SerializeAsync(stream, settings, new JsonSerializerOptions { WriteIndented = true }, ct);
    }
}
