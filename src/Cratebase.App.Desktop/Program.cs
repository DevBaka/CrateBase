using Avalonia;
using System;
using System.Runtime.InteropServices;

namespace Cratebase.App.Desktop;

sealed class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        ConfigureLinuxLibVlcResolver();

        // Muss einmal pro Prozess laufen, bevor irgendwo ein LibVLC/MediaPlayer instanziiert
        // wird (lokalisiert die native libvlc – Systembibliothek unter Linux, gebündelte
        // Runtime-Pakete unter Windows/Mac, s. Cratebase.Audio/LibVlcAudioPlayer.cs).
        LibVLCSharp.Shared.Core.Initialize();

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    /// <summary>
    /// LibVLCSharp sucht per DllImport nach dem unversionierten "libvlc"/"libvlccore" –
    /// den Symlink liefern die meisten Distros aber nur im "-devel"-Paket, die Runtime-Pakete
    /// installieren nur die versionierte SONAME (z.B. libvlc.so.5). Statt ein System-Paket zu
    /// verlangen, weichen wir hier auf die üblichen SONAMEs aus. Betrifft nur Linux – Windows/Mac
    /// nutzen die von VideoLAN.LibVLC.* gebündelte native Runtime (s. csproj), wo der Name passt.
    /// </summary>
    private static void ConfigureLinuxLibVlcResolver()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        NativeLibrary.SetDllImportResolver(typeof(LibVLCSharp.Shared.Core).Assembly, (libraryName, _, _) =>
        {
            string[] candidates = libraryName switch
            {
                "libvlc" => ["libvlc.so.5", "libvlc.so"],
                "libvlccore" => ["libvlccore.so.9", "libvlccore.so"],
                _ => [],
            };

            foreach (var candidate in candidates)
            {
                if (NativeLibrary.TryLoad(candidate, out var handle))
                {
                    return handle;
                }
            }

            return IntPtr.Zero; // Default-Resolution weiterprobieren lassen.
        });
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();
}
