namespace Cratebase.App.Desktop.ViewModels;

/// <summary>Ein einzelner Anzeige-Slot der Favoriten-Leiste (s. MainViewModel.RebuildFavoriteSlots).
/// Reines Anzeige-Objekt, bei jeder Änderung frisch aus dem aktiven FavoriteProfile aufgebaut - kein
/// ObservableObject nötig, da die gesamte FavoriteSlots-Collection dabei neu befüllt wird.</summary>
public sealed class FavoriteSlotViewModel(int index, long? playlistNodeId, string? playlistName)
{
    /// <summary>0-basiert (intern, für Slots-Dictionary-Key).</summary>
    public int Index { get; } = index;

    /// <summary>1-basiert, für die Anzeige ("Slot 1" statt "Slot 0").</summary>
    public int DisplayNumber => Index + 1;

    public long? PlaylistNodeId { get; } = playlistNodeId;

    public string? PlaylistName { get; } = playlistName;

    public bool IsAssigned => PlaylistNodeId is not null;

    public string DisplayText => PlaylistName ?? "Leer";
}
