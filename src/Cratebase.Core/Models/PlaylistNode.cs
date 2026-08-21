namespace Cratebase.Core.Models;

/// <summary>
/// 'smart_playlist' aus ARCHITECTURE.md Abschnitt 2.3 kommt erst mit der Regel-Engine (Phase 1
/// Folgeschritt) dazu – für dieses MVP gibt es nur Ordner und manuelle Playlisten, damit der
/// DJ-Software-Import (Phase 2) einen stabilen Baum vorfindet, ohne dass hier schon spekulativ
/// Regel-Logik entsteht.
/// </summary>
public enum PlaylistNodeType
{
    Folder,
    Playlist,
}

/// <summary>
/// Ein Knoten im Playlist-Baum. Ordner enthalten Playlisten und weitere Ordner
/// (siehe ARCHITECTURE.md Abschnitt 2.3) – bewusst getrennt von der physischen
/// Ordnerstruktur der Musikdateien auf der Festplatte.
/// </summary>
public sealed class PlaylistNode
{
    public long Id { get; set; }
    public long? ParentId { get; set; }
    public PlaylistNodeType NodeType { get; set; }
    public string Name { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}
