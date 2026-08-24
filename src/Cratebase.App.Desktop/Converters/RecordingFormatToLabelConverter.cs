using System.Globalization;
using Avalonia.Data.Converters;
using Cratebase.Core.Models;

namespace Cratebase.App.Desktop.Converters;

public sealed class RecordingFormatToLabelConverter : IValueConverter
{
    public static readonly RecordingFormatToLabelConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        RecordingFormat.Flac => "FLAC (verlustfrei, komprimiert)",
        RecordingFormat.Wav => "WAV (verlustfrei, unkomprimiert)",
        RecordingFormat.Mp3320 => "MP3 320 kbit/s",
        _ => value?.ToString(),
    };

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
