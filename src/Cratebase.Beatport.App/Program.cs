using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;
using Avalonia.Styling;
namespace Cratebase.Beatport.App;
internal static class Program
{
    [STAThread] public static void Main(string[] args) => AppBuilder.Configure<CratesApp>().UsePlatformDetect().StartWithClassicDesktopLifetime(args);
}
public sealed class CratesApp : Application
{
    public override void Initialize() { Styles.Add(new FluentTheme()); RequestedThemeVariant = ThemeVariant.Dark; }
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop) desktop.MainWindow = new MainWindow();
        base.OnFrameworkInitializationCompleted();
    }
}
