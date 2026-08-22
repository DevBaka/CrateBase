using System.Collections.ObjectModel;
using System.Globalization;
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
    private readonly ITagReader _tagReader;
    private readonly IWaveformGenerator _waveformGenerator;
    private readonly IWaveformCache _waveformCache;
    private readonly IQuickAccessStore _quickAccessStore;

    private readonly Dictionary<long, LibraryRoot> _libraryRootsById = new();
    private readonly Dictionary<long, IReadOnlyDictionary<string, TrackFingerprint>> _fingerprintsByRoot = new();
    private int _offset;
    private int _currentTrackIndex = -1;
    private bool _isApplyingPlayerPosition;
    private long _waveformRequestId;

    public MainViewModel(
        ITrackRepository trackRepository,
        ILibraryRootRepository libraryRootRepository,
        IPlaylistRepository playlistRepository,
        ILibraryScanner libraryScanner,
        IAudioPlayer audioPlayer,
        ICollectionImporter traktorImporter,
        ITagReader tagReader,
        IWaveformGenerator waveformGenerator,
        IWaveformCache waveformCache,
        IQuickAccessStore quickAccessStore)
    {
        _trackRepository = trackRepository;
        _libraryRootRepository = libraryRootRepository;
        _playlistRepository = playlistRepository;
        _libraryScanner = libraryScanner;
        _audioPlayer = audioPlayer;
        _traktorImporter = traktorImporter;
        _tagReader = tagReader;
        _waveformGenerator = waveformGenerator;
        _waveformCache = waveformCache;
        _quickAccessStore = quickAccessStore;

        // Feuern auf einem LibVLC-Thread (s. IAudioPlayer-Doku) – zurück auf den UI-Thread posten,
        // bevor gebundene Properties gesetzt werden.
        _audioPlayer.PositionChanged += (_, _) => Dispatcher.UIThread.Post(UpdatePlaybackPosition);
        _audioPlayer.PlaybackEnded += (_, _) => Dispatcher.UIThread.Post(() => PlayNextCommand.Execute(null));
    }

    /// <summary>
    /// Ein Baum, gemischt aus <see cref="PlaylistTreeItemViewModel"/> (Bibliothek + Playlisten,
    /// immer zuerst) und <see cref="FileSystemNodeViewModel"/> (Festplatten/Schnellzugriffe, danach
    /// angehängt) - bewusst EIN Baum statt zweier per Button umschaltbarer Ansichten, damit
    /// Festplatten "direkt dort, wo die Playlisten sind" ausklappbar sind. Avalonia löst das
    /// passende DataTemplate pro Element automatisch über den Laufzeittyp auf (s. MainWindow.axaml,
    /// TreeView.DataTemplates). <see cref="ReplacePlaylistNodes"/>/<see cref="ReplaceFilesystemNodes"/>
    /// tauschen jeweils nur ihre Hälfte aus, damit z.B. eine Playlist-Umbenennung nicht den
    /// Aufklapp-Zustand des Dateisystem-Baums zurücksetzt (und umgekehrt).
    /// </summary>
    public ObservableCollection<object> RootNodes { get; } = [];

    public ObservableCollection<Track> Tracks { get; } = [];

    // --- Sektions-Navigation (Top-Nav wie im Claude-Design-Vorbild) ---
    // Nur "Library" ist tatsächlich gebaut. Die anderen fünf sind bewusst als Platzhalter da,
    // statt sie wegzulassen: die Navigationsstruktur (Set Planner/Analytics/Duplicates/DJ App/
    // History) ist Teil dessen, wohin die App laut Vision wachsen soll, auch wenn die einzelnen
    // Features noch nicht existieren - lieber ehrlich "kommt später" zeigen als so tun, als gäbe
    // es die Idee nicht.

    [ObservableProperty]
    public partial string CurrentSection { get; set; } = "Library";

    public bool IsLibrarySection => CurrentSection == "Library";

    public string PlaceholderSectionTitle => CurrentSection switch
    {
        "Planner" => "Set Planner",
        "Analytics" => "Analytics",
        "Duplicates" => "Duplicates",
        "DjApp" => "DJ App",
        "History" => "Verlauf",
        _ => CurrentSection,
    };

    partial void OnCurrentSectionChanged(string value)
    {
        OnPropertyChanged(nameof(IsLibrarySection));
        OnPropertyChanged(nameof(PlaceholderSectionTitle));
    }

    [RelayCommand]
    private void GoToSection(string section) => CurrentSection = section;

    // --- Spalten-Sichtbarkeit (Track-Tabelle) ---

    [ObservableProperty]
    public partial bool ShowAlbumColumn { get; set; } = true;

    [ObservableProperty]
    public partial bool ShowGenreColumn { get; set; } = true;

    [ObservableProperty]
    public partial bool ShowBpmColumn { get; set; } = true;

    [ObservableProperty]
    public partial bool ShowKeyColumn { get; set; } = true;

    [ObservableProperty]
    public partial bool ShowDurationColumn { get; set; } = true;

    [ObservableProperty]
    public partial bool ShowSubGenreColumn { get; set; } = true;

    [ObservableProperty]
    public partial bool ShowCommentColumn { get; set; }

    [ObservableProperty]
    public partial bool ShowComment2Column { get; set; }

    [ObservableProperty]
    public partial bool ShowColorColumn { get; set; }

    [ObservableProperty]
    public partial bool ShowPathColumn { get; set; } = true;

    // --- Sidebar-Auswahl (ein Baum, s. RootNodes-Doku) ---

    [ObservableProperty]
    public partial object? SelectedSidebarItem { get; set; }

    /// <summary>Playlist-Baum-Auswahl, oder null, wenn gerade ein Dateisystem-Knoten gewählt ist.</summary>
    private PlaylistTreeItemViewModel? SelectedPlaylistNode => SelectedSidebarItem as PlaylistTreeItemViewModel;

    /// <summary>Dateisystem-Auswahl, oder null, wenn gerade ein Playlist-Knoten gewählt ist.</summary>
    private FileSystemNodeViewModel? SelectedFileSystemNode => SelectedSidebarItem as FileSystemNodeViewModel;

    public bool IsShowingFileList => SelectedFileSystemNode is not null;

    public bool IsShowingTrackGrid => !IsShowingFileList;

    /// <summary>"Auf Wunsch" (s. Nutzer-Feedback): Ordner/Playlist-Ordner zeigen standardmäßig nur ihren
    /// direkten Inhalt; mit diesem Schalter rekursiv alles darunter (verschachtelte Playlisten bzw.
    /// Unterordner-Dateien) - für Playlist-Ordner UND Dateisystem-Ordner gemeinsam genutzt. Der Zustand
    /// gilt pro Ordner (s. _includeSubfoldersByKey) und bleibt für die laufende Sitzung gemerkt - aktiviert
    /// man ihn z.B. für "Hardstyle", steht er beim nächsten Besuch dort wieder an, während der Wurzelordner
    /// unabhängig davon aus bleibt (kein globales/dauerhaft-sitzenbleibendes Flag, s. Nutzer-Feedback).
    /// Überlebt aktuell keinen App-Neustart (reines In-Memory-Dictionary), das wäre ein separater Ausbau.</summary>
    [ObservableProperty]
    public partial bool IncludeSubfolders { get; set; }

    private readonly Dictionary<string, bool> _includeSubfoldersByKey = new();
    private string? _currentSidebarKey;

    private static string GetSidebarKey(object? item) => item switch
    {
        FileSystemNodeViewModel fs => "fs:" + fs.FullPath.TrimEnd('/', '\\').ToLowerInvariant(),
        PlaylistTreeItemViewModel { Node: { } node } => "pl:" + node.Id,
        PlaylistTreeItemViewModel => "pl:root", // "Bibliothek" (Node == null)
        _ => "none",
    };

    partial void OnSelectedSidebarItemChanged(object? value)
    {
        OnPropertyChanged(nameof(IsShowingFileList));
        OnPropertyChanged(nameof(IsShowingTrackGrid));

        // Zustand des bisherigen Ordners merken, bevor wir umschalten...
        if (_currentSidebarKey is not null)
        {
            _includeSubfoldersByKey[_currentSidebarKey] = IncludeSubfolders;
        }

        // ...und für den neuen Ordner den gemerkten Zustand wiederherstellen (Default: aus).
        _currentSidebarKey = GetSidebarKey(value);
        var remembered = _includeSubfoldersByKey.GetValueOrDefault(_currentSidebarKey);

        if (IncludeSubfolders != remembered)
        {
            IncludeSubfolders = remembered; // löst selbst schon einen Reload aus, s. OnIncludeSubfoldersChanged
            return;
        }

        if (value is FileSystemNodeViewModel)
        {
            _ = ReloadFolderFilesAsync();
        }
        else
        {
            _ = ReloadTracksAsync(reset: true);
        }
    }

    partial void OnIncludeSubfoldersChanged(bool value)
    {
        if (_currentSidebarKey is not null)
        {
            _includeSubfoldersByKey[_currentSidebarKey] = value;
        }

        if (IsShowingFileList)
        {
            _ = ReloadFolderFilesAsync();
        }
        else
        {
            _ = ReloadTracksAsync(reset: true);
        }
    }

    // --- Datei-Browser (jetzt Teil desselben Baums, s. RootNodes) ---

    /// <summary>Zeigt dieselben Spalten wie die Bibliothek (Titel/Artist/Genre/BPM/... - s. Nutzer-Feedback
    /// "warum nur Dateinamen") - für bereits gescannte Dateien echte, aus der DB nachgeladene Track-Objekte,
    /// für den Rest "synthetische" Tracks (Id=0, s. Track.IsSynthetic) nur mit Dateiname/Pfad befüllt.</summary>
    public ObservableCollection<Track> FolderTracks { get; } = [];

    [ObservableProperty]
    public partial int FolderScannedCount { get; set; }

    [ObservableProperty]
    public partial int FolderTotalCount { get; set; }

    /// <summary>Mehr als das zeigt die Liste nicht an (Anzahl gescannt/gesamt bleibt trotzdem für ALLE
    /// gefundenen Dateien korrekt, s. unten) - bei "Unterordner einbeziehen" auf einem großen Root
    /// (z.B. der ganzen Bibliothek) sonst zehntausende Zeilen.</summary>
    private const int FolderFilesDisplayCap = 3000;

    /// <summary>Zeigt, was wirklich auf der Platte liegt (nicht nur, was schon in der DB steht) – Inhalt des
    /// gewählten Ordners, mit <see cref="IncludeSubfolders"/> rekursiv, sonst nur die direkten Dateien.</summary>
    private async Task ReloadFolderFilesAsync()
    {
        FolderTracks.Clear();
        FolderScannedCount = 0;
        FolderTotalCount = 0;

        var node = SelectedFileSystemNode;
        if (node is null)
        {
            return;
        }

        var matchedRoot = FindLibraryRootForPath(node.FullPath);
        IReadOnlyDictionary<string, TrackFingerprint>? fingerprints = null;
        if (matchedRoot is not null)
        {
            if (!_fingerprintsByRoot.TryGetValue(matchedRoot.Id, out var fp))
            {
                fp = await _trackRepository.GetFingerprintsByPathAsync(matchedRoot.Id);
                _fingerprintsByRoot[matchedRoot.Id] = fp;
            }

            fingerprints = fp;
        }

        var searchOption = IncludeSubfolders ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var rootPathForRelative = matchedRoot?.Path;

        // Enumeration + Fingerprint-Abgleich (billige Dictionary-Lookups, kein I/O) laufen im
        // Hintergrund für ALLE gefundenen Dateien; welche Track-Id (falls vorhanden) zu welcher
        // angezeigten Datei gehört, wird hier gesammelt - die vollen Metadaten kommen danach in
        // einem einzigen Bulk-Fetch (GetByIdsAsync), nicht mit einer Abfrage pro Datei.
        var (displayed, scannedTrackIds, totalFound, scannedCount) = await Task.Run(() =>
        {
            var files = SafeListAudioFiles(node.FullPath, searchOption);
            var scanned = 0;
            var shown = new List<(string File, string RelativePath, long? TrackId)>(Math.Min(files.Count, FolderFilesDisplayCap));
            var ids = new List<long>();

            for (var i = 0; i < files.Count; i++)
            {
                var file = files[i];
                var relativePath = rootPathForRelative is not null
                    ? Path.GetRelativePath(rootPathForRelative, file).Replace(Path.DirectorySeparatorChar, '/')
                    : file;

                long? trackId = null;
                if (fingerprints is not null && fingerprints.TryGetValue(relativePath, out var fp))
                {
                    trackId = fp.TrackId;
                    scanned++;
                }

                if (i < FolderFilesDisplayCap)
                {
                    shown.Add((file, relativePath, trackId));
                    if (trackId is not null)
                    {
                        ids.Add(trackId.Value);
                    }
                }
            }

            return (shown, ids, files.Count, scanned);
        });

        var fullTracksById = (await _trackRepository.GetByIdsAsync(scannedTrackIds)).ToDictionary(t => t.Id);

        foreach (var (file, relativePath, trackId) in displayed)
        {
            if (trackId is not null && fullTracksById.TryGetValue(trackId.Value, out var realTrack))
            {
                FolderTracks.Add(realTrack);
            }
            else
            {
                FolderTracks.Add(new Track
                {
                    LibraryRootId = matchedRoot?.Id ?? 0,
                    RelativePath = relativePath,
                    Format = Path.GetExtension(file).TrimStart('.').ToLowerInvariant(),
                    DateAddedUtc = DateTime.UtcNow,
                    DateModifiedUtc = DateTime.UtcNow,
                    // Titel/Artist/etc. bleiben null -> DisplayTitle fällt auf den Dateinamen zurück.
                });
            }
        }

        FolderScannedCount = scannedCount;
        FolderTotalCount = totalFound;

        if (totalFound > FolderFilesDisplayCap)
        {
            StatusMessage = $"Zeige die ersten {FolderFilesDisplayCap} von {totalFound} Dateien – für die ganze Bibliothek eher „Bibliothek“ statt „Unterordner einbeziehen“ auf dem Wurzelordner nutzen.";
        }
    }

    private List<string> SafeListAudioFiles(string path, SearchOption searchOption)
    {
        try
        {
            return Directory.EnumerateFiles(path, "*", searchOption)
                .Where(_tagReader.CanRead)
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (Exception)
        {
            return [];
        }
    }

    /// <summary>Bestmögliche (längster Pfad-Präfix) LibraryRoot-Übereinstimmung für einen absoluten Pfad -
    /// ein Dateisystem-Knoten kennt selbst keine LibraryRootId mehr (s. FileSystemNodeViewModel-Doku),
    /// das erlaubt beliebige Schnellzugriff-Pfade, auch wenn sie noch nicht gescannt sind.</summary>
    private LibraryRoot? FindLibraryRootForPath(string absolutePath)
    {
        LibraryRoot? best = null;
        var bestLength = -1;
        foreach (var root in _libraryRootsById.Values)
        {
            var normalizedRoot = root.Path.TrimEnd('/', '\\');
            if (absolutePath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase) && normalizedRoot.Length > bestLength)
            {
                best = root;
                bestLength = normalizedRoot.Length;
            }
        }

        return best;
    }

    private static bool PathsEqual(string a, string b) =>
        string.Equals(a.TrimEnd('/', '\\'), b.TrimEnd('/', '\\'), StringComparison.OrdinalIgnoreCase);

    private static bool IsPathNestedUnder(string path, string possibleParent)
    {
        var normalizedParent = possibleParent.TrimEnd('/', '\\');
        var normalizedPath = path.TrimEnd('/', '\\');
        return normalizedPath.Length > normalizedParent.Length
            && normalizedPath.StartsWith(normalizedParent, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Nur "oberste" LibraryRoots + Schnellzugriffe landen als eigene Top-Level-Zeile im Baum.
    /// "Diesen Ordner scannen" (s. ScanSelectedFolderAsync) legt für Unterordner einen eigenen,
    /// verschachtelten LibraryRoot an (pragmatisch, s. ARCHITECTURE.md 11.2) - ohne diesen Filter
    /// würde so ein Unterordner zusätzlich als redundanter zweiter Top-Level-Eintrag auftauchen,
    /// obwohl er schon über den übergeordneten Root erreichbar ist.
    /// </summary>
    private List<FileSystemNodeViewModel> BuildFilesystemNodesList()
    {
        var allRoots = _libraryRootsById.Values.ToList();
        var topLevelRoots = allRoots.Where(root => !allRoots.Any(other => other.Id != root.Id && IsPathNestedUnder(root.Path, other.Path)));

        var list = new List<FileSystemNodeViewModel>();
        foreach (var root in topLevelRoots.OrderBy(r => r.Label, StringComparer.OrdinalIgnoreCase))
        {
            list.Add(new FileSystemNodeViewModel(root.Label, root.Path));
        }

        foreach (var quickAccess in QuickAccessFolders)
        {
            if (allRoots.Any(r => PathsEqual(r.Path, quickAccess.Path) || IsPathNestedUnder(quickAccess.Path, r.Path)))
            {
                continue; // schon über eine (Vorfahren-)Bibliothek erreichbar, keine doppelte Zeile
            }

            list.Add(new FileSystemNodeViewModel(quickAccess.Name, quickAccess.Path));
        }

        return list;
    }

    /// <summary>Tauscht nur die Dateisystem-Hälfte von RootNodes aus (Festplatten + Schnellzugriffe) - der
    /// Playlist-Teil (immer zuerst in RootNodes) bleibt unangetastet. Bewusst kein RootNodes.Clear() für
    /// den kompletten Austausch: das feuert eine "Reset"-Notification, auf die TreeView mit Verlust der
    /// aktuellen Selektion reagiert (beobachtet: Bibliothek-Auswahl sprang nach einem Rebuild auf einen
    /// Zufallsknoten) - nur gezieltes Remove/Insert der betroffenen Elemente ist dagegen unauffällig.</summary>
    private void ReplaceFilesystemNodes(List<FileSystemNodeViewModel> newNodes)
    {
        var firstFilesystemIndex = RootNodes.Count;
        for (var i = 0; i < RootNodes.Count; i++)
        {
            if (RootNodes[i] is FileSystemNodeViewModel)
            {
                firstFilesystemIndex = i;
                break;
            }
        }

        while (RootNodes.Count > firstFilesystemIndex)
        {
            RootNodes.RemoveAt(RootNodes.Count - 1);
        }

        foreach (var node in newNodes)
        {
            RootNodes.Add(node);
        }
    }

    /// <summary>Tauscht nur die Playlist-Hälfte aus - s. ReplaceFilesystemNodes-Doku, umgekehrt.</summary>
    private void ReplacePlaylistNodes(List<PlaylistTreeItemViewModel> newNodes)
    {
        var oldPlaylistCount = 0;
        foreach (var item in RootNodes)
        {
            if (item is not PlaylistTreeItemViewModel)
            {
                break;
            }

            oldPlaylistCount++;
        }

        for (var i = oldPlaylistCount - 1; i >= 0; i--)
        {
            RootNodes.RemoveAt(i);
        }

        for (var i = 0; i < newNodes.Count; i++)
        {
            RootNodes.Insert(i, newNodes[i]);
        }
    }

    // --- Schnellzugriffe (Einstellungen) ---

    public ObservableCollection<QuickAccessFolder> QuickAccessFolders { get; } = [];

    /// <summary>Wird vom Code-behind nach dem Ordner-Auswahldialog aufgerufen (s. MainWindow.axaml.cs).</summary>
    [RelayCommand]
    private async Task AddQuickAccessAsync(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || QuickAccessFolders.Any(q => PathsEqual(q.Path, path)))
        {
            return;
        }

        var name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar));
        QuickAccessFolders.Add(new QuickAccessFolder(string.IsNullOrEmpty(name) ? path : name, path));
        await _quickAccessStore.SaveAsync(QuickAccessFolders);
        ReplaceFilesystemNodes(BuildFilesystemNodesList());
    }

    [RelayCommand]
    private async Task RemoveQuickAccessAsync(QuickAccessFolder folder)
    {
        var existing = QuickAccessFolders.FirstOrDefault(q => q.Path == folder.Path);
        if (existing is not null)
        {
            QuickAccessFolders.Remove(existing);
        }

        await _quickAccessStore.SaveAsync(QuickAccessFolders);
        ReplaceFilesystemNodes(BuildFilesystemNodesList());
    }

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

    [ObservableProperty]
    public partial WaveformData? CurrentWaveform { get; set; }

    [ObservableProperty]
    public partial bool IsWaveformLoading { get; set; }

    /// <summary>Von WaveformView beim Klick aufgerufen (Bruchteil 0..1 der Breite) – setzt einfach PositionSeconds,
    /// die restliche Seek-Logik läuft schon über OnPositionSecondsChanged.</summary>
    [RelayCommand]
    private void SeekToFraction(double fraction) => PositionSeconds = fraction * DurationSeconds;

    public async Task InitializeAsync()
    {
        var roots = await _libraryRootRepository.GetAllAsync();
        foreach (var root in roots)
        {
            _libraryRootsById[root.Id] = root;
        }

        foreach (var folder in await _quickAccessStore.LoadAsync())
        {
            QuickAccessFolders.Add(folder);
        }

        // RebuildPlaylistNodesAsync wählt "Bibliothek" und stößt darüber (Property-Setter,
        // s. OnSelectedSidebarItemChanged) einmalig ReloadTracksAsync an - kein zusätzlicher
        // expliziter Aufruf hier nötig (der würde nur mit dem fire-and-forget-Aufruf aus dem
        // Setter um den finalen Zustand konkurrieren).
        await RebuildPlaylistNodesAsync();
        ReplaceFilesystemNodes(BuildFilesystemNodesList());
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
            // Zeigt dieser Pfad schon irgendwo im Baum (als eigener Root oder als Unterordner
            // eines bekannten Roots)? Nur wenn nicht, kann sich das sichtbare Ergebnis von
            // BuildFilesystemNodesList() überhaupt ändern - ReplaceFilesystemNodes trotzdem immer
            // aufzurufen (frühere Version) hat bei jedem "Diesen Ordner scannen" den kompletten
            // Dateisystem-Teil neu erzeugt und damit jeden Aufklapp-Zustand zurückgesetzt, obwohl
            // sich am sichtbaren Baum nichts geändert hätte.
            var pathAlreadyVisible = _libraryRootsById.Values.Any(r => PathsEqual(r.Path, folderPath) || IsPathNestedUnder(folderPath, r.Path));

            var label = Path.GetFileName(folderPath.TrimEnd(Path.DirectorySeparatorChar));
            var root = await _libraryRootRepository.AddOrGetAsync(folderPath, string.IsNullOrEmpty(label) ? folderPath : label);
            _libraryRootsById[root.Id] = root;
            if (!pathAlreadyVisible)
            {
                ReplaceFilesystemNodes(BuildFilesystemNodesList()); // wirklich neuer, sichtbarer Eintrag
            }

            // Progress<T> fängt den aktuellen SynchronizationContext (UI-Thread) beim Erstellen ein,
            // der Callback braucht deshalb kein manuelles Dispatcher.Post.
            var progress = new Progress<ScanProgress>(p =>
            {
                StatusMessage = p.CurrentRelativePath is null
                    ? $"Scan fertig: {p.FilesAdded} neu, {p.FilesUpdated} aktualisiert, {p.FilesUnchanged} unverändert, {p.FilesFailed} ohne lesbare Tags."
                    : $"Scanne … {p.FilesSeen} Dateien gesehen ({p.FilesAdded} neu) – {p.CurrentRelativePath}";
            });

            // Task.Run: LibraryScanner liest Tags synchron (TagLib-Sharp macht blockierende
            // Datei-I/O) - ohne das hier auf einen Threadpool-Thread zu verlagern, würde das
            // direkt auf dem UI-Thread laufen und die App für die Dauer des Scans einfrieren
            // (beobachtet: kurzes Hängen beim Klick auf "Diesen Ordner scannen"). progress
            // bleibt trotzdem sicher, da Progress<T> den UI-SynchronizationContext beim Erstellen
            // eingefangen hat, unabhängig davon, von welchem Thread aus Report() aufgerufen wird.
            await Task.Run(() => _libraryScanner.ScanAsync(root, progress));
            _fingerprintsByRoot.Remove(root.Id); // Fingerprints sind nach dem Scan veraltet
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

    /// <summary>Scannt den im Datei-Browser gerade ausgewählten Ordner (nicht die ganze Bibliothek neu) – nützlich,
    /// um gezielt Lücken zu schließen. Der Ordner steht schon als Knoten im Baum (sonst hätte man ihn nicht
    /// auswählen können), ScanFolderAsync baut den Dateisystem-Teil deshalb NICHT neu auf - der Aufklapp-Zustand
    /// bleibt erhalten.</summary>
    [RelayCommand]
    private async Task ScanSelectedFolderAsync()
    {
        if (SelectedFileSystemNode is { } node)
        {
            await ScanFolderAsync(node.FullPath);
            _fingerprintsByRoot.Clear(); // einfachheitshalber: irgendein Root könnte betroffen sein
            await ReloadFolderFilesAsync();
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
            await RebuildPlaylistNodesAsync();
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

    private async Task ReloadTracksAsync(bool reset)
    {
        if (IsShowingFileList)
        {
            return; // Dateisystem-Ansicht zeigt keine Track-Tabelle, s. ReloadFolderFilesAsync
        }

        if (reset)
        {
            _offset = 0;
            Tracks.Clear();
        }

        IReadOnlyCollection<long>? playlistNodeIds = null;

        if (SelectedPlaylistNode?.Node is { NodeType: PlaylistNodeType.Folder } folderNode)
        {
            if (!IncludeSubfolders)
            {
                TotalCount = 0;
                LoadedCount = 0;
                StatusMessage = "Dieser Ordner enthält selbst keine Tracks – „Unterordner einbeziehen“ aktivieren oder eine Playlist darin wählen.";
                OnPropertyChanged(nameof(CanLoadMore));
                return;
            }

            var nestedIds = CollectPlaylistIds(SelectedPlaylistNode!);
            if (nestedIds.Count == 0)
            {
                TotalCount = 0;
                LoadedCount = 0;
                StatusMessage = "Dieser Ordner enthält keine Playlisten mit Tracks.";
                OnPropertyChanged(nameof(CanLoadMore));
                return;
            }

            playlistNodeIds = nestedIds;
        }
        else if (SelectedPlaylistNode?.Node is { NodeType: PlaylistNodeType.Playlist } playlistNode)
        {
            playlistNodeIds = [playlistNode.Id];
        }

        var results = await _trackRepository.SearchAsync(SearchText, playlistNodeIds, includeMissing: false, PageSize, _offset, CancellationToken.None);
        foreach (var track in results)
        {
            Tracks.Add(track);
        }

        _offset += results.Count;
        LoadedCount = Tracks.Count;
        TotalCount = await _trackRepository.CountAsync(SearchText, playlistNodeIds, includeMissing: false, CancellationToken.None);
        OnPropertyChanged(nameof(CanLoadMore));
    }

    /// <summary>Sammelt rekursiv alle Playlist-Ids (keine Ordner) unter einem Baum-Knoten - für "Unterordner einbeziehen".</summary>
    private static List<long> CollectPlaylistIds(PlaylistTreeItemViewModel node)
    {
        var ids = new List<long>();
        void Walk(PlaylistTreeItemViewModel current)
        {
            if (current.Node is { NodeType: PlaylistNodeType.Playlist } playlist)
            {
                ids.Add(playlist.Id);
            }

            foreach (var child in current.Children)
            {
                Walk(child);
            }
        }

        Walk(node);
        return ids;
    }

    private async Task RebuildPlaylistNodesAsync()
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

        var previouslySelectedId = SelectedPlaylistNode?.Node?.Id;
        var hadNoPlaylistSelection = SelectedSidebarItem is null;

        ReplacePlaylistNodes(roots);

        if (hadNoPlaylistSelection)
        {
            SelectedSidebarItem = roots[0];
        }
        else if (previouslySelectedId is not null)
        {
            SelectedSidebarItem = FindPlaylistNode(roots, previouslySelectedId.Value) ?? roots[0];
        }
        // War zuvor ein Dateisystem-Knoten gewählt, bleibt das unangetastet (SelectedSidebarItem ändert sich nicht).
    }

    private static PlaylistTreeItemViewModel? FindPlaylistNode(IEnumerable<PlaylistTreeItemViewModel> nodes, long id)
    {
        foreach (var node in nodes)
        {
            if (node.Node?.Id == id)
            {
                return node;
            }

            var match = FindPlaylistNode(node.Children, id);
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
        var parentId = SelectedPlaylistNode?.Node?.Id;
        await _playlistRepository.CreateAsync("Neuer Ordner", PlaylistNodeType.Folder, parentId);
        await RebuildPlaylistNodesAsync();
    }

    [RelayCommand]
    private async Task CreatePlaylistAsync()
    {
        var parentId = SelectedPlaylistNode?.Node?.Id;
        await _playlistRepository.CreateAsync("Neue Playlist", PlaylistNodeType.Playlist, parentId);
        await RebuildPlaylistNodesAsync();
    }

    [RelayCommand]
    private async Task DeleteSelectedNodeAsync()
    {
        if (SelectedPlaylistNode?.Node is null)
        {
            return; // "Bibliothek" ist kein echter Knoten und nicht löschbar, Dateisystem-Knoten sind nicht löschbar.
        }

        await _playlistRepository.DeleteAsync(SelectedPlaylistNode.Node.Id);
        await RebuildPlaylistNodesAsync();
    }

    [RelayCommand]
    private async Task AddSelectedTrackToPlaylistAsync()
    {
        if (SelectedPlaylistNode?.Node is not { NodeType: PlaylistNodeType.Playlist } || SelectedTrack is null)
        {
            StatusMessage = "Wähle eine Playlist im Baum und einen Track in der Liste aus.";
            return;
        }

        await _playlistRepository.AddTracksAsync(SelectedPlaylistNode.Node.Id, [SelectedTrack.Id]);
        StatusMessage = $"„{SelectedTrack.DisplayTitle}“ zu „{SelectedPlaylistNode.DisplayName}“ hinzugefügt.";
    }

    [ObservableProperty]
    public partial Track? SelectedTrack { get; set; }

    // --- Tag-Editor ("Edit-Modus") ---
    // Eigene Felder statt direkt an Track-Properties zu binden: Track ist bewusst ein reines
    // Datenmodell ohne INotifyPropertyChanged (Cratebase.Core kennt kein UI-Framework, s.
    // ARCHITECTURE.md Abschnitt 3) - Bindings brauchen deshalb einen ObservableObject-Umweg über
    // das ViewModel, mit explizitem Speichern statt Live-Sync in beide Richtungen.

    [ObservableProperty]
    public partial bool IsEditMode { get; set; }

    [RelayCommand]
    private void ToggleEditMode() => IsEditMode = !IsEditMode;

    [ObservableProperty]
    public partial string? EditTitle { get; set; }

    [ObservableProperty]
    public partial string? EditArtist { get; set; }

    [ObservableProperty]
    public partial string? EditAlbum { get; set; }

    [ObservableProperty]
    public partial string? EditGenre { get; set; }

    [ObservableProperty]
    public partial string? EditSubGenre { get; set; }

    [ObservableProperty]
    public partial string? EditComment { get; set; }

    [ObservableProperty]
    public partial string? EditComment2 { get; set; }

    [ObservableProperty]
    public partial string? EditColorTag { get; set; }

    [ObservableProperty]
    public partial string? EditBpmText { get; set; }

    [ObservableProperty]
    public partial string? EditKey { get; set; }

    /// <summary>Autocomplete-Vorschläge je Feld - "was ich dort einspeichere, soll ich per Dropdown wieder
    /// finden" (Nutzer-Feedback): einfach alle schon in der DB benutzten Werte dieser Spalte, keine
    /// separate Vokabular-Tabelle nötig. Einmal geladen, wenn der Edit-Modus aktiviert wird, und nach
    /// jedem Speichern neu (damit ein gerade neu getippter Wert sofort für den nächsten Track vorschlägt).</summary>
    public ObservableCollection<string> GenreSuggestions { get; } = [];
    public ObservableCollection<string> SubGenreSuggestions { get; } = [];
    public ObservableCollection<string> CommentSuggestions { get; } = [];
    public ObservableCollection<string> Comment2Suggestions { get; } = [];
    public ObservableCollection<string> ColorSuggestions { get; } = [];

    private bool _isLoadingEditFields;

    partial void OnIsEditModeChanged(bool value)
    {
        if (!value)
        {
            return;
        }

        _ = LoadEditSuggestionsAsync();
        LoadEditFieldsFromSelectedTrack();
    }

    partial void OnSelectedTrackChanged(Track? value)
    {
        if (IsEditMode)
        {
            LoadEditFieldsFromSelectedTrack();
        }
    }

    /// <summary>Füllt die Edit-Felder aus dem gerade gewählten Track - der Guard verhindert, dass dieses
    /// reine Anzeigen selbst schon als "Feld geändert" durchgeht (hier gibt's aktuell keine Auto-Save-
    /// Logik pro Tastendruck, aber die Guard-Variable ist der klare Ansatzpunkt, falls das mal dazukommt).</summary>
    private void LoadEditFieldsFromSelectedTrack()
    {
        _isLoadingEditFields = true;
        try
        {
            var track = SelectedTrack;
            EditTitle = track?.Title;
            EditArtist = track?.Artist;
            EditAlbum = track?.Album;
            EditGenre = track?.GenreRaw;
            EditSubGenre = track?.SubGenre;
            EditComment = track?.Comment;
            EditComment2 = track?.Comment2;
            EditColorTag = track?.ColorTag;
            EditBpmText = track?.Bpm?.ToString("0.##", CultureInfo.InvariantCulture);
            EditKey = track?.MusicalKey;
        }
        finally
        {
            _isLoadingEditFields = false;
        }
    }

    private async Task LoadEditSuggestionsAsync()
    {
        async Task Fill(ObservableCollection<string> target, TrackTextField field)
        {
            var values = await _trackRepository.GetDistinctTextValuesAsync(field);
            target.Clear();
            foreach (var value in values)
            {
                target.Add(value);
            }
        }

        await Task.WhenAll(
            Fill(GenreSuggestions, TrackTextField.GenreRaw),
            Fill(SubGenreSuggestions, TrackTextField.SubGenre),
            Fill(CommentSuggestions, TrackTextField.Comment),
            Fill(Comment2Suggestions, TrackTextField.Comment2),
            Fill(ColorSuggestions, TrackTextField.ColorTag));
    }

    /// <summary>Wird vom Code-behind ausgelöst, wenn eines der Edit-Felder den Fokus verliert (s. MainWindow.axaml.cs) -
    /// bewusst kein Speichern pro Tastendruck (unnötig viele DB-Writes), aber auch kein separater "Speichern"-Button
    /// nötig ("einfach und schnell bearbeiten", Nutzer-Feedback).</summary>
    [RelayCommand]
    private async Task SaveEditedMetadataAsync()
    {
        if (_isLoadingEditFields || SelectedTrack is not { IsSynthetic: false } track)
        {
            return; // nichts gewählt, oder eine noch nicht gescannte Datei ohne DB-Datensatz zum Speichern
        }

        var changed = false;
        changed |= SetIfDifferent(track, t => t.Title, (t, v) => t.Title = v, NullIfBlank(EditTitle));
        changed |= SetIfDifferent(track, t => t.Artist, (t, v) => t.Artist = v, NullIfBlank(EditArtist));
        changed |= SetIfDifferent(track, t => t.Album, (t, v) => t.Album = v, NullIfBlank(EditAlbum));
        changed |= SetIfDifferent(track, t => t.GenreRaw, (t, v) => t.GenreRaw = v, NullIfBlank(EditGenre));
        changed |= SetIfDifferent(track, t => t.SubGenre, (t, v) => t.SubGenre = v, NullIfBlank(EditSubGenre));
        changed |= SetIfDifferent(track, t => t.Comment, (t, v) => t.Comment = v, NullIfBlank(EditComment));
        changed |= SetIfDifferent(track, t => t.Comment2, (t, v) => t.Comment2 = v, NullIfBlank(EditComment2));
        changed |= SetIfDifferent(track, t => t.ColorTag, (t, v) => t.ColorTag = v, NullIfBlank(EditColorTag));
        changed |= SetIfDifferent(track, t => t.MusicalKey, (t, v) => t.MusicalKey = v, NullIfBlank(EditKey));

        var parsedBpm = ParseBpm(EditBpmText);
        if (track.Bpm != parsedBpm)
        {
            track.Bpm = parsedBpm;
            changed = true;
        }

        if (!changed)
        {
            return;
        }

        track.DateModifiedUtc = DateTime.UtcNow;
        await _trackRepository.UpdateAsync(track);
        StatusMessage = $"„{track.DisplayTitle}“ gespeichert.";

        RefreshTrackInLists(track);
        await LoadEditSuggestionsAsync(); // neu getippte Werte sofort als Vorschlag verfügbar
    }

    private static bool SetIfDifferent(Track track, Func<Track, string?> get, Action<Track, string?> set, string? newValue)
    {
        if (string.Equals(get(track), newValue, StringComparison.Ordinal))
        {
            return false;
        }

        set(track, newValue);
        return true;
    }

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static double? ParseBpm(string? text) =>
        !string.IsNullOrWhiteSpace(text) && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var bpm)
            ? bpm
            : null;

    /// <summary>Track ist absichtlich kein ObservableObject (s. Kommentar oben) - ein In-Place-Ändern der
    /// Properties allein aktualisiert deshalb keine gebundene DataGrid-Zeile. Entfernen+Wiedereinfügen an
    /// derselben Stelle zwingt die betroffenen Listen zu einem frischen Re-Bind, ohne dass Track dafür
    /// INotifyPropertyChanged bräuchte. SelectedTrack kurz auf null und zurück, weil ein erneutes Zuweisen
    /// derselben Objektreferenz sonst als "unverändert" gilt und keine Reselektion auslöst.</summary>
    private void RefreshTrackInLists(Track track)
    {
        ReplaceInCollection(Tracks, track);
        ReplaceInCollection(FolderTracks, track);

        if (SelectedTrack is not null && ReferenceEquals(SelectedTrack, track))
        {
            SelectedTrack = null;
            SelectedTrack = track;
        }
    }

    private static void ReplaceInCollection(ObservableCollection<Track> collection, Track track)
    {
        for (var i = 0; i < collection.Count; i++)
        {
            if (collection[i].Id == track.Id)
            {
                collection.RemoveAt(i);
                collection.Insert(i, track);
                return;
            }
        }
    }

    // --- Wiedergabe ---

    [RelayCommand]
    private void PlaySelectedTrack()
    {
        switch (SelectedTrack)
        {
            case { IsSynthetic: false } track:
                PlayTrack(track);
                break;
            case { IsSynthetic: true }:
                StatusMessage = "Diese Datei ist noch nicht gescannt – erst „Diesen Ordner scannen“, dann abspielbar.";
                break;
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

        _ = LoadWaveformAsync(track);
    }

    /// <summary>Cache zuerst (schnell), sonst per ffmpeg neu generieren (s. IWaveformGenerator-Doku für die
    /// Begründung/Grenzen). _waveformRequestId schützt davor, dass eine spät ankommende Waveform von einem
    /// inzwischen verlassenen Track die Anzeige des aktuellen Tracks überschreibt.</summary>
    private async Task LoadWaveformAsync(Track track)
    {
        var requestId = ++_waveformRequestId;
        CurrentWaveform = null;

        var cached = await _waveformCache.TryLoadAsync(track);
        if (requestId != _waveformRequestId)
        {
            return;
        }

        if (cached is not null)
        {
            CurrentWaveform = cached;
            return;
        }

        IsWaveformLoading = true;
        try
        {
            var generated = await _waveformGenerator.GenerateAsync(ResolveAbsolutePath(track));
            if (requestId != _waveformRequestId)
            {
                return;
            }

            if (generated is not null)
            {
                CurrentWaveform = generated;
                await _waveformCache.SaveAsync(track, generated);
            }
        }
        finally
        {
            if (requestId == _waveformRequestId)
            {
                IsWaveformLoading = false;
            }
        }
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
        OnPropertyChanged(nameof(ProgressFraction));
        if (!_isApplyingPlayerPosition)
        {
            _audioPlayer.Seek(TimeSpan.FromSeconds(value));
        }
    }

    partial void OnDurationSecondsChanged(double value) => OnPropertyChanged(nameof(ProgressFraction));

    /// <summary>0..1, fürs WaveformView (das kennt keine Sekunden).</summary>
    public double ProgressFraction => DurationSeconds > 0 ? PositionSeconds / DurationSeconds : 0;

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
