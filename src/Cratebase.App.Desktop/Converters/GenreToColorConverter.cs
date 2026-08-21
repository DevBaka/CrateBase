using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Cratebase.App.Desktop.Converters;

/// <summary>
/// Deterministische Farbe pro Genre-String (Hash → feste Palette), fürs Pill in der Track-Liste.
/// Keine echte Genre-Taxonomie/-Zuordnung - rein visuell, damit unterschiedliche Genres beim
/// Scannen auf einen Blick auseinanderzuhalten sind (s. "DJ Library App.dc.html"-Vorbild:
/// Pill-Text in Vollfarbe, Pill-Hintergrund in derselben Farbe bei niedriger Deckkraft).
/// </summary>
internal static class GenrePalette
{
    public static readonly Color[] Colors =
    [
        Color.Parse("#F98E8F"),
        Color.Parse("#F0995B"),
        Color.Parse("#CBB042"),
        Color.Parse("#0DCBC3"),
        Color.Parse("#1EC5E4"),
        Color.Parse("#63B9FE"),
        Color.Parse("#A9A5FF"),
        Color.Parse("#DF92D8"),
    ];

    public static readonly Color Fallback = Color.Parse("#5B5E66");

    public static Color PickColor(string? genre)
    {
        if (string.IsNullOrWhiteSpace(genre))
        {
            return Fallback;
        }

        var index = (uint)genre.GetHashCode() % (uint)Colors.Length;
        return Colors[index];
    }
}

public sealed class GenreToForegroundConverter : IValueConverter
{
    public static readonly GenreToForegroundConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        new SolidColorBrush(GenrePalette.PickColor(value as string));

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class GenreToBackgroundConverter : IValueConverter
{
    public static readonly GenreToBackgroundConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var color = GenrePalette.PickColor(value as string);
        return new SolidColorBrush(new Color(0x30, color.R, color.G, color.B));
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
