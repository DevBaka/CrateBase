using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Cratebase.App.Desktop.Converters;

/// <summary>Farbcodierung für die Aussteuerungsanzeige (dBFS-Wert je Kanal) - grün im normalen
/// Bereich, gelb ab -12dB als Vorwarnung, rot ab -3dB (nahe Vollaussteuerung/Clipping). Feste
/// Hex-Farben statt neuer globaler App.axaml-Ressourcen, da es (noch) keine Gelb-Akzentfarbe im
/// Farbschema gibt und dieser Zweck sehr eng an diese eine Anzeige gebunden ist.</summary>
public sealed class DbLevelToBrushConverter : IValueConverter
{
    public static readonly DbLevelToBrushConverter Instance = new();

    private static readonly IBrush GreenBrush = new SolidColorBrush(Color.Parse("#33E78C"));
    private static readonly IBrush YellowBrush = new SolidColorBrush(Color.Parse("#F2C94C"));
    private static readonly IBrush RedBrush = new SolidColorBrush(Color.Parse("#FF645F"));

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not double db)
        {
            return GreenBrush;
        }

        return db switch
        {
            >= -3 => RedBrush,
            >= -12 => YellowBrush,
            _ => GreenBrush,
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
