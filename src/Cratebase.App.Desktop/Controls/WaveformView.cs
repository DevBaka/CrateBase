using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Cratebase.Core.Models;

namespace Cratebase.App.Desktop.Controls;

/// <summary>
/// Ersetzt den reinen Fortschritts-Slider in der Transportleiste: zeichnet die 3-Band-Waveform
/// (<see cref="WaveformData"/>) als übereinandergelegte, halbtransparente Balken (rot=Bass,
/// grün=Mitten, blau=Höhen - "RGB-Waveform"), plus Fortschrittslinie, plus Klick-zum-Seeken.
/// Reines <see cref="Control"/> mit eigenem Render statt eines ItemsControls über 600 Elemente -
/// bei stetigem Positions-Update (mehrmals pro Sekunde) wäre das spürbar langsamer.
/// </summary>
public sealed class WaveformView : Control
{
    public static readonly StyledProperty<WaveformData?> WaveformProperty =
        AvaloniaProperty.Register<WaveformView, WaveformData?>(nameof(Waveform));

    public static readonly StyledProperty<double> ProgressProperty =
        AvaloniaProperty.Register<WaveformView, double>(nameof(Progress));

    public static readonly StyledProperty<bool> IsLoadingProperty =
        AvaloniaProperty.Register<WaveformView, bool>(nameof(IsLoading));

    public static readonly StyledProperty<ICommand?> SeekCommandProperty =
        AvaloniaProperty.Register<WaveformView, ICommand?>(nameof(SeekCommand));

    private static readonly IBrush BackgroundBrush = new SolidColorBrush(Color.Parse("#08090B"));
    private static readonly IBrush LowBrush = new SolidColorBrush(Color.Parse("#CCFF5C5C"));
    private static readonly IBrush MidBrush = new SolidColorBrush(Color.Parse("#CC33E78C"));
    private static readonly IBrush HighBrush = new SolidColorBrush(Color.Parse("#CC4CC3FF"));
    private static readonly IBrush ProgressBrush = new SolidColorBrush(Colors.White);
    private static readonly IBrush EmptyBrush = new SolidColorBrush(Color.Parse("#2B2E35"));

    static WaveformView()
    {
        AffectsRender<WaveformView>(WaveformProperty, ProgressProperty, IsLoadingProperty);
    }

    public WaveformData? Waveform
    {
        get => GetValue(WaveformProperty);
        set => SetValue(WaveformProperty, value);
    }

    /// <summary>0..1</summary>
    public double Progress
    {
        get => GetValue(ProgressProperty);
        set => SetValue(ProgressProperty, value);
    }

    public bool IsLoading
    {
        get => GetValue(IsLoadingProperty);
        set => SetValue(IsLoadingProperty, value);
    }

    /// <summary>Wird mit dem Klick-Anteil (0..1) als Parameter aufgerufen.</summary>
    public ICommand? SeekCommand
    {
        get => GetValue(SeekCommandProperty);
        set => SetValue(SeekCommandProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(Bounds.Size);
        context.FillRectangle(BackgroundBrush, bounds);

        var data = Waveform;
        var height = Bounds.Height;
        var width = Bounds.Width;
        var mid = height / 2;

        if (data is null || data.Columns.Count == 0 || width <= 0)
        {
            // Ruhige, flache Linie statt leerer Fläche - signalisiert "keine Waveform", ohne wie ein Rendering-Fehler auszusehen.
            context.DrawLine(new Pen(EmptyBrush, 1), new Point(0, mid), new Point(width, mid));
        }
        else
        {
            var count = data.Columns.Count;
            var barWidth = Math.Max(1.0, width / count);

            for (var i = 0; i < count; i++)
            {
                var column = data.Columns[i];
                var x = i * barWidth;
                DrawBand(context, LowBrush, x, mid, barWidth, column.Low, height);
                DrawBand(context, MidBrush, x, mid, barWidth, column.Mid, height * 0.72);
                DrawBand(context, HighBrush, x, mid, barWidth, column.High, height * 0.46);
            }
        }

        var progressX = width * Math.Clamp(Progress, 0, 1);
        context.DrawLine(new Pen(ProgressBrush, 2), new Point(progressX, 0), new Point(progressX, height));
    }

    private static void DrawBand(DrawingContext context, IBrush brush, double x, double mid, double barWidth, float amplitude, double maxHeight)
    {
        var barHeight = Math.Max(1.0, amplitude * maxHeight);
        var rect = new Rect(x, mid - barHeight / 2, Math.Max(1.0, barWidth - 0.5), barHeight);
        context.FillRectangle(brush, rect);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (Bounds.Width <= 0)
        {
            return;
        }

        var fraction = Math.Clamp(e.GetPosition(this).X / Bounds.Width, 0, 1);
        if (SeekCommand?.CanExecute(fraction) == true)
        {
            SeekCommand.Execute(fraction);
        }
    }
}
