using System.Text.Json;
using Cratebase.Core.Abstractions;
using Cratebase.Core.Models;

namespace Cratebase.Data;

/// <summary>Einfache JSON-Datei statt SQLite-Tabelle (s. IFavoriteProfileStore-Doku).</summary>
public sealed class JsonFavoriteProfileStore(string filePath) : IFavoriteProfileStore
{
    private readonly SemaphoreSlim _writeLock = new(1);
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

        await _writeLock.WaitAsync(ct);
        var temporary=filePath+"."+Guid.NewGuid().ToString("N")+".tmp";
        try
        {
            await using(var stream=File.Create(temporary))
                await JsonSerializer.SerializeAsync(stream, settings, new JsonSerializerOptions { WriteIndented = true }, ct);
            File.Move(temporary,filePath,true);
        }
        finally { if(File.Exists(temporary)) File.Delete(temporary); _writeLock.Release(); }
    }
}
