using System.Globalization;
using Avalonia.Data.Converters;

namespace Cratebase.App.Desktop.Converters;

/// <summary>MM:SS, oder H:MM:SS ab einer Stunde - spiegelt MainViewModel.FormatSetTimestamp (dort
/// privat, hier für das Marker-DataTemplate als Binding-Converter dupliziert statt eine öffentliche
/// API nur für diesen einen Anzeigezweck aufzumachen).</summary>
public sealed class TimeSpanToTimestampConverter : IValueConverter
{
    public static readonly TimeSpanToTimestampConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not TimeSpan position)
        {
            return null;
        }

        return position.TotalHours >= 1
            ? position.ToString(@"h\:mm\:ss")
            : $"{(int)position.TotalMinutes:00}:{position.Seconds:00}";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
