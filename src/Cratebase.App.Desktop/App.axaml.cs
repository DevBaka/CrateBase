using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Cratebase.Analysis;
using Cratebase.App.Desktop.ViewModels;
using Cratebase.App.Desktop.Views;
using Cratebase.Audio;
using Cratebase.Core.Abstractions;
using Cratebase.Data;
using Cratebase.Data.Repositories;
using Cratebase.Import;
using Cratebase.Scanning;
using Cratebase.Tagging;

namespace Cratebase.App.Desktop;

public partial class App : Application
{
    private IAudioPlayer? _audioPlayer;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            Services.WindowsMediaSession.RegisterApplication();
            // Composition Root: hier und nur hier werden konkrete Implementierungen aus
            // Data/Tagging/Scanning/Audio verdrahtet. Alles, was von hier aus weitergereicht
            // wird (MainViewModel-Konstruktor), sieht nur noch die Core-Interfaces – kein
            // externes DI-Framework nötig für diesen Umfang, s. ARCHITECTURE.md Abschnitt 3.
            var databasePath = GetDatabasePath();
            var connectionFactory = new SqliteConnectionFactory(databasePath);
            DatabaseInitializer.EnsureCreated(connectionFactory);

            ITrackRepository trackRepository = new TrackRepository(connectionFactory);
            ILibraryRootRepository libraryRootRepository = new LibraryRootRepository(connectionFactory);
            IPlaylistRepository playlistRepository = new PlaylistRepository(connectionFactory);
            ICuePointRepository cuePointRepository = new CuePointRepository(connectionFactory);
            ITagReader tagReader = new TagLibTagReader();
            ILibraryScanner libraryScanner = new LibraryScanner(trackRepository, libraryRootRepository, tagReader);
            ICollectionImporter traktorImporter = new TraktorCollectionImporter(trackRepository, libraryRootRepository, playlistRepository, cuePointRepository);
            INmlExporter nmlExporter = new TraktorNmlWriter(trackRepository, libraryRootRepository, playlistRepository);
            IWaveformGenerator waveformGenerator = new FfmpegWaveformGenerator();
            IWaveformCache waveformCache = new FileWaveformCache(GetWaveformCacheDir());
            IQuickAccessStore quickAccessStore = new JsonQuickAccessStore(GetQuickAccessFilePath());
            IFavoriteProfileStore favoriteProfileStore = new JsonFavoriteProfileStore(GetFavoriteProfilesFilePath());
            IAudioRecorder audioRecorder = new FfmpegAudioRecorder();
            INowPlayingReader nowPlayingReader = new NowPlayingReader();

            _audioPlayer = new LibVlcAudioPlayer();

            var libraryQuery = new LibraryQuery(connectionFactory);
            var workspaceState = new WorkspaceStateStore(connectionFactory);
            var targetActions = new TargetActions(trackRepository, libraryRootRepository, playlistRepository, tagReader, libraryQuery);
            var sync = new CollectionSync(trackRepository, libraryRootRepository, playlistRepository, workspaceState, new WorkspaceCheckpoint(connectionFactory));
            var stripes = new TraktorStripeWaveformSource(new TraktorAnalysisLookup(workspaceState, libraryRootRepository));
            var player = new WorkspacePlayer(_audioPlayer, libraryRootRepository, waveformGenerator, waveformCache, stripes, trackRepository);
            var server = new Cratebase.Server.LibraryServer(trackRepository, libraryRootRepository, playlistRepository, libraryQuery,
                targetActions, favoriteProfileStore, Path.Combine(GetAppDirectory(), "stream-cache"), new LibraryRevision(connectionFactory),new Services.DesktopPlaybackBridge(player));
            var recorder = new MainViewModel(trackRepository, libraryRootRepository, playlistRepository, libraryScanner,
                _audioPlayer, traktorImporter, tagReader, waveformGenerator, waveformCache, quickAccessStore,
                favoriteProfileStore, nmlExporter, audioRecorder, nowPlayingReader, subscribePlaybackEvents: false);
            var mainWindow = new WorkspaceWindow(trackRepository, libraryRootRepository, playlistRepository, libraryQuery,
                targetActions, sync, workspaceState, favoriteProfileStore, quickAccessStore, libraryScanner, tagReader, player, recorder, (FfmpegAudioRecorder)audioRecorder,new ClubMediaExporter(libraryRootRepository,libraryQuery,sync),server,cuePointRepository);
            desktop.MainWindow = mainWindow;
            desktop.ShutdownRequested += (_, _) => _audioPlayer.Dispose();
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// XDG_DATA_HOME (Linux: ~/.local/share) bzw. das plattformäquivalente LocalApplicationData –
    /// bewusst nicht das Projekt-/Quellverzeichnis, DB und Musiksammlung sind getrennte Dinge.
    /// </summary>
    private static string GetDatabasePath()
    {
        var appDir = GetAppDirectory();
        Directory.CreateDirectory(appDir);
        return Path.Combine(appDir, "cratebase.db");
    }

    private static string GetWaveformCacheDir()
    {
        return Path.Combine(GetAppDirectory(), "waveforms");
    }

    private static string GetQuickAccessFilePath()
    {
        return Path.Combine(GetAppDirectory(), "quickaccess.json");
    }

    private static string GetFavoriteProfilesFilePath()
    {
        return Path.Combine(GetAppDirectory(), "favorite_profiles.json");
    }

    private static string GetAppDirectory() => Environment.GetEnvironmentVariable("CRATEBASE_DATA_DIR")
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Cratebase");
}
