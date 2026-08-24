using System.Globalization;
using Avalonia.Data.Converters;
using Cratebase.Core.Abstractions;

namespace Cratebase.App.Desktop.Converters;

public sealed class NowPlayingSourceTypeToLabelConverter : IValueConverter
{
    public static readonly NowPlayingSourceTypeToLabelConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        NowPlayingSourceType.PlainTextFile => "Now-Playing-Textdatei",
        NowPlayingSourceType.TraktorHistoryNml => "Traktor-History (NML)",
        _ => value?.ToString(),
    };

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
