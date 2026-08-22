namespace Cratebase.Core.Models;

/// <summary>
/// Ein gespeichertes Set von Playlist-Favoriten-Zuweisungen ("Preset") - z.B. "Hardstyle" mit
/// mehreren thematischen Playlisten auf festen Slot-Nummern, oder "House" mit einer anderen
/// Belegung. Slots sind sparse (Key = Slot-Index, Value = Playlist-Id) und unabhängig von der
/// aktuell konfigurierten Slot-Anzahl (<see cref="FavoriteProfileSettings.SlotCount"/>) - ein
/// Profil mit nur 9 belegten Slots zeigt bei 20 sichtbaren Slots einfach 11 leere.
/// </summary>
public sealed class FavoriteProfile
{
    public string Name { get; set; } = string.Empty;

    public Dictionary<int, long> Slots { get; set; } = new();
}

/// <summary>Persistenz-Hülle für alle Favoriten-Profile - liegt wie <see cref="QuickAccessFolder"/>
/// als JSON außerhalb der SQLite-DB (reine App-Einstellung, keine Domänendaten).</summary>
public sealed class FavoriteProfileSettings
{
    public List<FavoriteProfile> Profiles { get; set; } = new();

    public string? ActiveProfileName { get; set; }

    public int SlotCount { get; set; } = 12;
}
