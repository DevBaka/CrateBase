using System.Globalization;
using Avalonia.Data.Converters;

namespace Cratebase.App.Desktop.Converters;

public sealed class SecondsToTimeStringConverter : IValueConverter
{
    public static readonly SecondsToTimeStringConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var seconds = value switch
        {
            double d => d,
            int i => i,
            _ => 0d,
        };

        var time = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return time.Hours > 0
            ? time.ToString(@"h\:mm\:ss")
            : time.ToString(@"m\:ss");
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
