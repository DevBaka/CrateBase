using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using System.Runtime.InteropServices;
using Cratebase.Core.Models;

namespace Cratebase.App.Desktop.Controls;

public sealed record WaveformMarker(double Progress,string Label,uint Color,bool Draggable=false,string? Id=null);

/// <summary>
/// Ersetzt den reinen Fortschritts-Slider in der Transportleiste: zeichnet die 3-Band-Waveform
/// (<see cref="WaveformData"/>) als übereinandergelegte, halbtransparente Balken (rot=Bass,
/// grün=Mitten, blau=Höhen - "RGB-Waveform"), plus Fortschrittslinie, plus Klick-zum-Seeken.
/// Reines <see cref="Control"/> mit eigenem Render statt eines ItemsControls über 600 Elemente -
/// bei stetigem Positions-Update (mehrmals pro Sekunde) wäre das spürbar langsamer.
/// </summary>
public sealed class WaveformView : Control
{
    private string? _dragMarker;
    private double _dragValue;
    public event Action<string,double>? MarkerDragged;
    public event Action<string,double>? MarkerDragCompleted;
    private WriteableBitmap? _stripeBitmap;
    private WaveformRaster? _bitmapSource;
    public static readonly StyledProperty<WaveformData?> WaveformProperty =
        AvaloniaProperty.Register<WaveformView, WaveformData?>(nameof(Waveform));

    public static readonly StyledProperty<double> ProgressProperty =
        AvaloniaProperty.Register<WaveformView, double>(nameof(Progress));

    public static readonly StyledProperty<bool> IsLoadingProperty =
        AvaloniaProperty.Register<WaveformView, bool>(nameof(IsLoading));

    public static readonly StyledProperty<ICommand?> SeekCommandProperty =
        AvaloniaProperty.Register<WaveformView, ICommand?>(nameof(SeekCommand));
    public static readonly StyledProperty<IReadOnlyList<WaveformMarker>?> MarkersProperty =
        AvaloniaProperty.Register<WaveformView,IReadOnlyList<WaveformMarker>?>(nameof(Markers));
    public IReadOnlyList<WaveformMarker>? Markers {get=>GetValue(MarkersProperty);set=>SetValue(MarkersProperty,value);}

    private static readonly IBrush BackgroundBrush = new SolidColorBrush(Color.Parse("#08090B"));
    private static readonly IBrush LowBrush = new SolidColorBrush(Color.Parse("#CCFF5C5C"));
    private static readonly IBrush MidBrush = new SolidColorBrush(Color.Parse("#CC33E78C"));
    private static readonly IBrush HighBrush = new SolidColorBrush(Color.Parse("#CC4CC3FF"));
    private static readonly IBrush ProgressBrush = new SolidColorBrush(Colors.White);
    private static readonly IBrush EmptyBrush = new SolidColorBrush(Color.Parse("#2B2E35"));

    static WaveformView()
    {
        AffectsRender<WaveformView>(WaveformProperty, ProgressProperty, IsLoadingProperty,MarkersProperty);
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

        if(data?.Raster is { } raster && width>0)
        {
            if(!ReferenceEquals(_bitmapSource,raster))
            {
                _stripeBitmap?.Dispose();
                _stripeBitmap=new WriteableBitmap(new PixelSize(raster.Width,raster.Height),new Vector(96,96),PixelFormat.Rgba8888,AlphaFormat.Premul);
                using(var buffer=_stripeBitmap.Lock())
                    for(var row=0;row<raster.Height;row++) Marshal.Copy(raster.Rgba,row*raster.Width*4,buffer.Address+row*buffer.RowBytes,raster.Width*4);
                _bitmapSource=raster;
            }
            context.DrawImage(_stripeBitmap!,new Rect(0,0,raster.Width,raster.Height),bounds);
        }
        else if (data is null || data.Columns.Count == 0 || width <= 0)
        {
            // Ruhige, flache Linie statt leerer Fläche - signalisiert "keine Waveform", ohne wie ein Rendering-Fehler auszusehen.
            context.DrawLine(new Pen(EmptyBrush, 1), new Point(0, mid), new Point(width, mid));
        }
        else
        {
            var count = Math.Min(data.Columns.Count,Math.Max(1,(int)Math.Ceiling(width)));
            var barWidth = width / count;

            for (var i = 0; i < count; i++)
            {
                var from=(int)((long)i*data.Columns.Count/count);
                var to=(int)((long)(i+1)*data.Columns.Count/count);
                float low=0,midPeak=0,high=0;
                for(var j=from;j<to;j++)
                {
                    low=Math.Max(low,data.Columns[j].Low);midPeak=Math.Max(midPeak,data.Columns[j].Mid);high=Math.Max(high,data.Columns[j].High);
                }
                var column = new WaveformColumn(low,midPeak,high);
                var x = i * barWidth;
                DrawBand(context, LowBrush, x, mid, barWidth, column.Low, height);
                DrawBand(context, MidBrush, x, mid, barWidth, column.Mid, height * 0.72);
                DrawBand(context, HighBrush, x, mid, barWidth, column.High, height * 0.46);
            }
        }

        var progressX = width * Math.Clamp(Progress, 0, 1);
        context.DrawLine(new Pen(ProgressBrush, 2), new Point(progressX, 0), new Point(progressX, height));
        var markerIndex=0;
        foreach(var marker in Markers??[])
        {
            if(!double.IsFinite(marker.Progress))continue;
            var x=width*Math.Clamp(marker.Progress,0,1);var brush=new SolidColorBrush(Color.FromUInt32(marker.Color));
            context.DrawLine(new Pen(brush,1),new Point(x,0),new Point(x,height));
            if(marker.Draggable)context.FillRectangle(brush,new Rect(Math.Clamp(x-4,0,Math.Max(0,width-8)),Math.Max(0,height-9),8,9));
            if(marker.Label.Length>0)
            {
                var text=new FormattedText(marker.Label,System.Globalization.CultureInfo.CurrentCulture,FlowDirection.LeftToRight,Typeface.Default,10,brush);
                context.DrawText(text,new Point(Math.Clamp(x+3,0,Math.Max(0,width-text.Width)),(markerIndex++%3)*12));
            }
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _stripeBitmap?.Dispose(); _stripeBitmap=null; _bitmapSource=null;
        base.OnDetachedFromVisualTree(e);
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
        if(e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            var point=e.GetPosition(this);var markers=Markers??[];
            var labeled=markers.Where(m=>m.Label.Length>0).Select((m,i)=>(Marker:m,Row:i%3)).FirstOrDefault(m=>m.Marker.Draggable&&m.Marker.Id!=null&&point.Y>=m.Row*12&&point.Y<m.Row*12+12&&point.X>=Math.Clamp(m.Marker.Progress*Bounds.Width+3,0,Math.Max(0,Bounds.Width-m.Marker.Label.Length*7))&&point.X<=Math.Clamp(m.Marker.Progress*Bounds.Width+3,0,Math.Max(0,Bounds.Width-m.Marker.Label.Length*7))+m.Marker.Label.Length*7);
            var marker=labeled.Marker??markers.Where(m=>m.Draggable&&m.Id!=null&&Math.Abs(m.Progress*Bounds.Width-point.X)<=8).OrderBy(m=>Math.Abs(m.Progress*Bounds.Width-point.X)).FirstOrDefault();
            if(marker!=null){_dragMarker=marker.Id;_dragValue=marker.Progress;e.Pointer.Capture(this);e.Handled=true;return;}
        }
        if (SeekCommand?.CanExecute(fraction) == true)
        {
            SeekCommand.Execute(fraction);
        }
    }
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);if(_dragMarker==null||Bounds.Width<=0)return;
        _dragValue=Math.Clamp(e.GetPosition(this).X/Bounds.Width,0,1);MarkerDragged?.Invoke(_dragMarker,_dragValue);e.Handled=true;
    }
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);if(_dragMarker is not {} id)return;
        _dragMarker=null;e.Pointer.Capture(null);MarkerDragCompleted?.Invoke(id,_dragValue);e.Handled=true;
    }
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {base.OnPointerCaptureLost(e);if(_dragMarker is {} id){_dragMarker=null;MarkerDragCompleted?.Invoke(id,_dragValue);}}
}
