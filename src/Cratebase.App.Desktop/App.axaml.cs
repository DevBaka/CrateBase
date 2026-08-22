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
            IWaveformGenerator waveformGenerator = new FfmpegWaveformGenerator();
            IWaveformCache waveformCache = new FileWaveformCache(GetWaveformCacheDir());

            _audioPlayer = new LibVlcAudioPlayer();

            var viewModel = new MainViewModel(
                trackRepository, libraryRootRepository, playlistRepository, libraryScanner, _audioPlayer,
                traktorImporter, tagReader, waveformGenerator, waveformCache);

            var mainWindow = new MainWindow { DataContext = viewModel };
            mainWindow.Opened += async (_, _) => await viewModel.InitializeAsync();
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
        var baseDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var appDir = Path.Combine(baseDir, "Cratebase");
        Directory.CreateDirectory(appDir);
        return Path.Combine(appDir, "cratebase.db");
    }

    private static string GetWaveformCacheDir()
    {
        var baseDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(baseDir, "Cratebase", "waveforms");
    }
}
