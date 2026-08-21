using System.Collections.ObjectModel;
using Avalonia.Threading;
using Cratebase.Core.Abstractions;
using Cratebase.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Cratebase.App.Desktop.ViewModels;

/// <summary>
/// Einziges ViewModel dieses Prototyps. Spricht ausschließlich über die Core-Interfaces
/// (ITrackRepository/ILibraryRootRepository/IPlaylistRepository/ILibraryScanner/IAudioPlayer) –
/// kein SQL, kein LibVLC-Typ taucht hier auf. Die konkreten Implementierungen werden im
/// Composition Root (App.axaml.cs) verdrahtet.
/// </summary>
public partial class MainViewModel : ViewModelBase
{
    private const int PageSize = 500;

    private readonly ITrackRepository _trackRepository;
    private readonly ILibraryRootRepository _libraryRootRepository;
    private readonly IPlaylistRepository _playlistRepository;
    private readonly ILibraryScanner _libraryScanner;
    private readonly IAudioPlayer _audioPlayer;
    private readonly ICollectionImporter _traktorImporter;

    private readonly Dictionary<long, LibraryRoot> _libraryRootsById = new();
    private int _offset;
    private int _currentTrackIndex = -1;
    private bool _isApplyingPlayerPosition;

    public MainViewModel(
        ITrackRepository trackRepository,
        ILibraryRootRepository libraryRootRepository,
        IPlaylistRepository playlistRepository,
        ILibraryScanner libraryScanner,
        IAudioPlayer audioPlayer,
        ICollectionImporter traktorImporter)
    {
        _trackRepository = trackRepository;
        _libraryRootRepository = libraryRootRepository;
        _playlistRepository = playlistRepository;
        _libraryScanner = libraryScanner;
        _audioPlayer = audioPlayer;
        _traktorImporter = traktorImporter;

        // Feuern auf einem LibVLC-Thread (s. IAudioPlayer-Doku) – zurück auf den UI-Thread posten,
        // bevor gebundene Properties gesetzt werden.
        _audioPlayer.PositionChanged += (_, _) => Dispatcher.UIThread.Post(UpdatePlaybackPosition);
        _audioPlayer.PlaybackEnded += (_, _) => Dispatcher.UIThread.Post(() => PlayNextCommand.Execute(null));
    }

    public ObservableCollection<PlaylistTreeItemViewModel> RootNodes { get; } = [];

    public ObservableCollection<Track> Tracks { get; } = [];

    [ObservableProperty]
    public partial PlaylistTreeItemViewModel? SelectedNode { get; set; }

    [ObservableProperty]
    public partial Track? SelectedTrack { get; set; }

    [ObservableProperty]
    public partial string? SearchText { get; set; }

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = "Bereit. Füge einen Ordner hinzu, um deine Bibliothek zu scannen.";

    [ObservableProperty]
    public partial bool IsScanning { get; set; }

    [ObservableProperty]
    public partial int LoadedCount { get; set; }

    [ObservableProperty]
    public partial int TotalCount { get; set; }

    public bool CanLoadMore => LoadedCount < TotalCount;

    // --- Player-Zustand ---

    [ObservableProperty]
    public partial bool IsPlaying { get; set; }

    [ObservableProperty]
    public partial string NowPlayingTitle { get; set; } = "–";

    [ObservableProperty]
    public partial string NowPlayingArtist { get; set; } = "";

    [ObservableProperty]
    public partial double PositionSeconds { get; set; }

    [ObservableProperty]
    public partial double DurationSeconds { get; set; }

    [ObservableProperty]
    public partial double Volume { get; set; } = 80;

    public async Task InitializeAsync()
    {
        var roots = await _libraryRootRepository.GetAllAsync();
        foreach (var root in roots)
        {
            _libraryRootsById[root.Id] = root;
        }

        await ReloadPlaylistTreeAsync();
        await ReloadTracksAsync(reset: true);
        _audioPlayer.Volume = (float)(Volume / 100.0);
    }

    /// <summary>Wird vom Code-behind nach dem Ordner-Auswahldialog aufgerufen (s. MainWindow.axaml.cs).</summary>
    [RelayCommand]
    private async Task ScanFolderAsync(string folderPath)
    {
        if (IsScanning || string.IsNullOrWhiteSpace(folderPath))
        {
            return;
        }

        IsScanning = true;
        try
        {
            var label = Path.GetFileName(folderPath.TrimEnd(Path.DirectorySeparatorChar));
            var root = await _libraryRootRepository.AddOrGetAsync(folderPath, string.IsNullOrEmpty(label) ? folderPath : label);
            _libraryRootsById[root.Id] = root;

            // Progress<T> fängt den aktuellen SynchronizationContext (UI-Thread) beim Erstellen ein,
            // der Callback braucht deshalb kein manuelles Dispatcher.Post.
            var progress = new Progress<ScanProgress>(p =>
            {
                StatusMessage = p.CurrentRelativePath is null
                    ? $"Scan fertig: {p.FilesAdded} neu, {p.FilesUpdated} aktualisiert, {p.FilesUnchanged} unverändert, {p.FilesFailed} ohne lesbare Tags."
                    : $"Scanne … {p.FilesSeen} Dateien gesehen ({p.FilesAdded} neu) – {p.CurrentRelativePath}";
            });

            await _libraryScanner.ScanAsync(root, progress);
            await ReloadTracksAsync(reset: true);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Scan fehlgeschlagen: {ex.Message}";
        }
        finally
        {
            IsScanning = false;
        }
    }

    /// <summary>Wird vom Code-behind nach dem NML-Auswahldialog aufgerufen (s. MainWindow.axaml.cs).</summary>
    [RelayCommand]
    private async Task ImportTraktorAsync(string nmlPath)
    {
        if (IsScanning || string.IsNullOrWhiteSpace(nmlPath))
        {
            return;
        }

        IsScanning = true; // teilt sich die "gerade beschäftigt"-Sperre mit dem Scan - beides schreibt in dieselbe DB
        try
        {
            var progress = new Progress<ImportProgress>(p =>
            {
                StatusMessage = p.CurrentItem is null
                    ? $"Traktor-Import fertig: {p.TracksMatched} zugeordnet, {p.TracksUnmatched} nicht gefunden (noch nicht gescannt?), {p.CuePointsImported} Cue-Points."
                    : $"Traktor-Import … {p.EntriesSeen} Einträge gelesen ({p.TracksMatched} zugeordnet) – {p.CurrentItem}";
            });

            var summary = await _traktorImporter.ImportAsync(nmlPath, progress);
            StatusMessage = $"Traktor-Import fertig: {summary.TracksMatched}/{summary.TotalEntries} Tracks zugeordnet, " +
                             $"{summary.CuePointsImported} Cue-Points, {summary.PlaylistsCreated} Playlist-Knoten, {summary.PlaylistTracksLinked} Playlist-Zuordnungen.";
            await ReloadPlaylistTreeAsync();
            await ReloadTracksAsync(reset: true);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Traktor-Import fehlgeschlagen: {ex.Message}";
        }
        finally
        {
            IsScanning = false;
        }
    }

    [RelayCommand]
    private async Task LoadMoreAsync() => await ReloadTracksAsync(reset: false);

    partial void OnSearchTextChanged(string? value) => _ = ReloadTracksAsync(reset: true);

    partial void OnSelectedNodeChanged(PlaylistTreeItemViewModel? value) => _ = ReloadTracksAsync(reset: true);

    private async Task ReloadTracksAsync(bool reset)
    {
        if (reset)
        {
            _offset = 0;
            Tracks.Clear();
        }

        if (SelectedNode?.Node is { NodeType: PlaylistNodeType.Folder })
        {
            // Ordner selbst enthalten keine Tracks, nur Playlisten/Unterordner (s. ARCHITECTURE.md 2.3).
            // Rekursive Aggregation über alle enthaltenen Playlisten ist ein Folgeschritt, kein MVP-Scope.
            TotalCount = 0;
            LoadedCount = 0;
            StatusMessage = "Dieser Ordner enthält selbst keine Tracks – wähle eine Playlist darin aus.";
            OnPropertyChanged(nameof(CanLoadMore));
            return;
        }

        var playlistNodeId = SelectedNode?.Node?.Id;
        var results = await _trackRepository.SearchAsync(SearchText, playlistNodeId, includeMissing: false, PageSize, _offset, CancellationToken.None);
        foreach (var track in results)
        {
            Tracks.Add(track);
        }

        _offset += results.Count;
        LoadedCount = Tracks.Count;
        TotalCount = await _trackRepository.CountAsync(SearchText, playlistNodeId, includeMissing: false, CancellationToken.None);
        OnPropertyChanged(nameof(CanLoadMore));
    }

    private async Task ReloadPlaylistTreeAsync()
    {
        var nodes = await _playlistRepository.GetAllNodesAsync();
        var byId = nodes.ToDictionary(n => n.Id, n => new PlaylistTreeItemViewModel(n, n.Name));

        var roots = new List<PlaylistTreeItemViewModel> { new(null, "Bibliothek") };
        foreach (var node in nodes.OrderBy(n => n.SortOrder).ThenBy(n => n.Name, StringComparer.OrdinalIgnoreCase))
        {
            var vm = byId[node.Id];
            if (node.ParentId is { } parentId && byId.TryGetValue(parentId, out var parentVm))
            {
                parentVm.Children.Add(vm);
            }
            else
            {
                roots.Add(vm);
            }
        }

        var previouslySelectedId = SelectedNode?.Node?.Id;

        RootNodes.Clear();
        foreach (var root in roots)
        {
            RootNodes.Add(root);
        }

        SelectedNode = previouslySelectedId is null
            ? RootNodes[0]
            : FindNode(RootNodes, previouslySelectedId.Value) ?? RootNodes[0];
    }

    private static PlaylistTreeItemViewModel? FindNode(IEnumerable<PlaylistTreeItemViewModel> nodes, long id)
    {
        foreach (var node in nodes)
        {
            if (node.Node?.Id == id)
            {
                return node;
            }

            var match = FindNode(node.Children, id);
            if (match is not null)
            {
                return match;
            }
        }

        return null;
    }

    [RelayCommand]
    private async Task CreateFolderAsync()
    {
        var parentId = SelectedNode?.Node?.Id;
        await _playlistRepository.CreateAsync("Neuer Ordner", PlaylistNodeType.Folder, parentId);
        await ReloadPlaylistTreeAsync();
    }

    [RelayCommand]
    private async Task CreatePlaylistAsync()
    {
        var parentId = SelectedNode?.Node?.Id;
        await _playlistRepository.CreateAsync("Neue Playlist", PlaylistNodeType.Playlist, parentId);
        await ReloadPlaylistTreeAsync();
    }

    [RelayCommand]
    private async Task DeleteSelectedNodeAsync()
    {
        if (SelectedNode?.Node is null)
        {
            return; // "Bibliothek" ist kein echter Knoten und nicht löschbar.
        }

        await _playlistRepository.DeleteAsync(SelectedNode.Node.Id);
        await ReloadPlaylistTreeAsync();
    }

    [RelayCommand]
    private async Task AddSelectedTrackToPlaylistAsync()
    {
        if (SelectedNode?.Node is not { NodeType: PlaylistNodeType.Playlist } || SelectedTrack is null)
        {
            StatusMessage = "Wähle eine Playlist im Baum und einen Track in der Liste aus.";
            return;
        }

        await _playlistRepository.AddTracksAsync(SelectedNode.Node.Id, [SelectedTrack.Id]);
        StatusMessage = $"„{SelectedTrack.DisplayTitle}“ zu „{SelectedNode.DisplayName}“ hinzugefügt.";
    }

    // --- Wiedergabe ---

    [RelayCommand]
    private void PlaySelectedTrack()
    {
        if (SelectedTrack is not null)
        {
            PlayTrack(SelectedTrack);
        }
    }

    [RelayCommand]
    private void PlayPause()
    {
        switch (_audioPlayer.State)
        {
            case PlaybackState.Playing:
                _audioPlayer.Pause();
                IsPlaying = false;
                break;
            case PlaybackState.Paused:
                _audioPlayer.Play();
                IsPlaying = true;
                break;
            case PlaybackState.Stopped:
                PlaySelectedTrack();
                break;
        }
    }

    [RelayCommand]
    private void StopPlayback()
    {
        _audioPlayer.Stop();
        IsPlaying = false;
    }

    [RelayCommand]
    private void PlayNext()
    {
        if (Tracks.Count == 0)
        {
            return;
        }

        _currentTrackIndex = Math.Min(_currentTrackIndex + 1, Tracks.Count - 1);
        PlayTrack(Tracks[_currentTrackIndex]);
    }

    [RelayCommand]
    private void PlayPrevious()
    {
        if (Tracks.Count == 0)
        {
            return;
        }

        _currentTrackIndex = Math.Max(_currentTrackIndex - 1, 0);
        PlayTrack(Tracks[_currentTrackIndex]);
    }

    private void PlayTrack(Track track)
    {
        _currentTrackIndex = Tracks.IndexOf(track);
        _audioPlayer.Load(ResolveAbsolutePath(track));
        _audioPlayer.Play();

        NowPlayingTitle = track.DisplayTitle;
        NowPlayingArtist = track.DisplayArtist;
        DurationSeconds = track.DurationMs / 1000.0; // Fallback, bis LibVLC die echte Länge kennt (s. UpdatePlaybackPosition)
        PositionSeconds = 0;
        IsPlaying = true;
    }

    private void UpdatePlaybackPosition()
    {
        _isApplyingPlayerPosition = true;
        try
        {
            PositionSeconds = _audioPlayer.Position.TotalSeconds;
            var duration = _audioPlayer.Duration.TotalSeconds;
            if (duration > 0)
            {
                DurationSeconds = duration;
            }
        }
        finally
        {
            _isApplyingPlayerPosition = false;
        }
    }

    /// <summary>Wird auch beim programmatischen Fortschritts-Update aufgerufen – der Guard
    /// verhindert, dass jedes Wiedergabe-Tick als Nutzer-Seek interpretiert wird.</summary>
    partial void OnPositionSecondsChanged(double value)
    {
        if (!_isApplyingPlayerPosition)
        {
            _audioPlayer.Seek(TimeSpan.FromSeconds(value));
        }
    }

    partial void OnVolumeChanged(double value) => _audioPlayer.Volume = (float)(value / 100.0);

    private string ResolveAbsolutePath(Track track)
    {
        if (!_libraryRootsById.TryGetValue(track.LibraryRootId, out var root))
        {
            throw new InvalidOperationException($"LibraryRoot {track.LibraryRootId} ist nicht geladen.");
        }

        return Path.Combine(root.Path, track.RelativePath.Replace('/', Path.DirectorySeparatorChar));
    }
}
