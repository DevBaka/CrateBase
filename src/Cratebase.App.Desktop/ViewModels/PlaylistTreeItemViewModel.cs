using System.Collections.ObjectModel;
using Cratebase.Core.Models;

namespace Cratebase.App.Desktop.ViewModels;

/// <summary>
/// Ein Knoten im TreeView. <see cref="Node"/> ist null für den synthetischen "Bibliothek"-Eintrag
/// (zeigt die gesamte, nicht gefilterte Sammlung – existiert nicht als PlaylistNode in der DB).
/// </summary>
public sealed class PlaylistTreeItemViewModel(PlaylistNode? node, string displayName)
{
    public PlaylistNode? Node { get; } = node;

    public string DisplayName { get; } = displayName;

    public ObservableCollection<PlaylistTreeItemViewModel> Children { get; } = [];

    public bool IsPlaylist => Node?.NodeType == PlaylistNodeType.Playlist;
}
