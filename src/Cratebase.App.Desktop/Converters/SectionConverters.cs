using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Cratebase.App.Desktop.Converters;

/// <summary>Grüner Hintergrund-Tint, wenn der gebundene Sektionsname (value) dem Tab-Key (ConverterParameter) entspricht - für die aktive Top-Nav-Markierung.</summary>
public sealed class SectionToBackgroundConverter : IValueConverter
{
    public static readonly SectionToBackgroundConverter Instance = new();
    private static readonly IBrush Active = new SolidColorBrush(Color.Parse("#2433E78C"));

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Equals(value?.ToString(), parameter?.ToString()) ? Active : Brushes.Transparent;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Grüne statt gedämpfte Schriftfarbe für den aktiven Top-Nav-Tab.</summary>
public sealed class SectionToForegroundConverter : IValueConverter
{
    public static readonly SectionToForegroundConverter Instance = new();
    private static readonly IBrush Active = new SolidColorBrush(Color.Parse("#33E78C"));
    private static readonly IBrush Inactive = new SolidColorBrush(Color.Parse("#8D919B"));

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Equals(value?.ToString(), parameter?.ToString()) ? Active : Inactive;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
