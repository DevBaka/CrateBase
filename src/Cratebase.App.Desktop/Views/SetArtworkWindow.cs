using System.Globalization;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Input.Platform;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Cratebase.Analysis;

namespace Cratebase.App.Desktop.Views;

public sealed partial class SetArtworkWindow : Window
{
    private readonly string _directory;
    private readonly LocalPlaylistAi _ai;
    private readonly SetVideoFrames _frames;
    private readonly CancellationTokenSource _lifetime=new();
    private CancellationTokenSource? _job;
    private readonly List<Button> _buttons=[];
    private readonly List<Bitmap> _images=[];
    private readonly TextBox _video=new(){Name="ArtworkVideo",PlaceholderText="Videoaufnahme (.mp4, .mkv, .mov …)"};
    private readonly TextBox _brief=new(){Name="ArtworkBrief",AcceptsReturn=true,Height=85,TextWrapping=TextWrapping.Wrap,PlaceholderText="Set-Thema, Genres, Stimmung, gewünschte Sprache …"};
    private readonly TextBox _tracks=new(){AcceptsReturn=true,Height=100,TextWrapping=TextWrapping.Wrap};
    private readonly TextBox _brand=new(){Name="ArtworkBrand",PlaceholderText="Kanal / DJ-Name (optional)"};
    private readonly TextBox _headline=new(){Name="ArtworkHeadline",Text="DEIN DJ SET",MaxLength=55};
    private readonly TextBox _subtitle=new(){Name="ArtworkSubtitle",Text="LIVE MIX",MaxLength=85};
    private readonly TextBox _accent=new(){Name="ArtworkAccent",Text="#36D9E8",MaxLength=7};
    private readonly TextBox _second=new(){Name="ArtworkSeconds",Text="0",Width=100};
    private readonly ComboBox _style=new(){ItemsSource=new[]{"Je nach Set unterschiedlich","Dunkel / Neon / Club","Natürlich / wenig Text","Emotional / atmosphärisch"},SelectedIndex=0,HorizontalAlignment=HorizontalAlignment.Stretch};
    private readonly ComboBox _side=new(){ItemsSource=new[]{"Text links","Text rechts"},SelectedIndex=0};
    private readonly Slider _textX=new(){Name="ArtworkTextX",Minimum=0,Maximum=100,Value=0};
    private readonly Slider _textY=new(){Name="ArtworkTextY",Minimum=0,Maximum=100,Value=50};
    private bool _draggingText;
    private readonly CheckBox _cpu=new(){Content="KI im CPU-Modus (falls GPU belegt)"};
    private readonly ComboBox _titles=new(){Name="ArtworkTitles",PlaceholderText="KI-Titelvorschläge",HorizontalAlignment=HorizontalAlignment.Stretch};
    private readonly TextBox _title=new(){Name="ArtworkTitle",MaxLength=100,PlaceholderText="YouTube-Titel (editierbar)"};
    private readonly TextBlock _status=new(){Name="ArtworkStatus",TextWrapping=TextWrapping.Wrap};
    private readonly StackPanel _choices=new(){Orientation=Orientation.Horizontal,Spacing=8};
    private readonly Viewbox _preview=new(){Stretch=Stretch.Uniform};
    private readonly TextBlock _frameInfo=new(){TextWrapping=TextWrapping.Wrap};
    private SetVideoFrame? _selected;
    private Bitmap? _image;
    public SetArtworkWindow(string directory,string ffmpeg,string source="",string tracklist="")
    {
        _directory=directory;_ai=new(Path.Combine(directory,"ai"));_frames=new(ffmpeg);_video.Text=source;_tracks.Text=tracklist;
        Title="Thumbnail & Titel · Set-Veröffentlichung";Width=1280;Height=880;MinWidth=950;MinHeight=680;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        var layout=new Grid{ColumnDefinitions=new("350,*"),Margin=new Thickness(16),ColumnSpacing=16};
        var editor=new StackPanel{Spacing=8};
        editor.Children.Add(new TextBlock{Text="Thumbnail & Titel",FontSize=22,FontWeight=FontWeight.Bold});
        editor.Children.Add(new TextBlock{Text="Texte und Bilder frei gestalten. Titel-KI liefert Text und Farben; die zusätzliche Bild-KI erzeugt Varianten deines Frames. Die automatische Frame-Auswahl prüft Helligkeit und Detail.",TextWrapping=TextWrapping.Wrap,FontSize=12});
        editor.Children.Add(_video);editor.Children.Add(Action("Video auswählen …",PickAsync));editor.Children.Add(Action("Gute Frames suchen",FindAsync));
        var position=new WrapPanel();position.Children.Add(new TextBlock{Text="Zeit in Sekunden: ",VerticalAlignment=VerticalAlignment.Center});position.Children.Add(_second);position.Children.Add(Action("Frame holen",ExtractAsync));editor.Children.Add(position);
        editor.Children.Add(new TextBlock{Text="Thema & Stil"});editor.Children.Add(_brief);editor.Children.Add(_style);editor.Children.Add(_brand);
        editor.Children.Add(new Expander{Header="Tracklist als KI-Kontext",Content=_tracks,HorizontalAlignment=HorizontalAlignment.Stretch});
        editor.Children.Add(_cpu);editor.Children.Add(Action("Lokale KI: Titel & Gestaltung",GenerateAsync));editor.Children.Add(Action("KI installieren / prüfen",()=>_ai.InstallAsync(new Progress<string>(s=>_status.Text=s),_job!.Token)));
        editor.Children.Add(new TextBlock{Text="Gestaltung bearbeiten"});editor.Children.Add(_headline);editor.Children.Add(_subtitle);editor.Children.Add(_accent);editor.Children.Add(_side);
        editor.Children.Add(new TextBlock{Text="Textposition · direkt in der Vorschau ziehen",TextWrapping=TextWrapping.Wrap});
        editor.Children.Add(new TextBlock{Text="Links ↔ Rechts"});editor.Children.Add(_textX);
        editor.Children.Add(new TextBlock{Text="Oben ↔ Unten"});editor.Children.Add(_textY);
        editor.Children.Add(Action("Position zurücksetzen",()=>{_textX.Value=_side.SelectedIndex==1?100:0;_textY.Value=50;return Task.CompletedTask;}));
        BuildDesignTools(editor);
        var left=new ScrollViewer{Content=editor};layout.Children.Add(left);
        var right=new Grid{RowDefinitions=new("*,Auto,Auto,Auto,Auto,Auto,Auto"),RowSpacing=10};Grid.SetColumn(right,1);layout.Children.Add(right);
        var background=new Border{Background=Brushes.Black,Child=_preview};right.Children.Add(background);
        var choices=new ScrollViewer{Content=_choices,HorizontalScrollBarVisibility=Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,VerticalScrollBarVisibility=Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled};Grid.SetRow(choices,1);right.Children.Add(choices);
        Grid.SetRow(_frameInfo,2);right.Children.Add(_frameInfo);Grid.SetRow(_titles,3);right.Children.Add(_titles);Grid.SetRow(_title,4);right.Children.Add(_title);
        var export=new WrapPanel();export.Children.Add(Action("Thumbnail als PNG speichern …",SaveAsync));export.Children.Add(Action("Titel kopieren",async()=>{if(Clipboard!=null)await Clipboard.SetTextAsync(_title.Text??"");}));
        var cancel=new Button{Content="Abbrechen",Margin=new Thickness(3)};cancel.Click+=(_,_)=>_job?.Cancel();export.Children.Add(cancel);Grid.SetRow(export,5);right.Children.Add(export);Grid.SetRow(_status,6);right.Children.Add(_status);Content=layout;
        foreach(var box in new[]{_headline,_subtitle,_accent,_brand})box.TextChanged+=(_,_)=>Refresh();_side.SelectionChanged+=(_,_)=>{_textX.Value=_side.SelectedIndex==1?100:0;Refresh();};
        foreach(var slider in new[]{_textX,_textY})slider.PropertyChanged+=(_,e)=>{if(e.Property==Slider.ValueProperty&&!_draggingText)Refresh();};
        _titles.SelectionChanged+=(_,_)=>{if(_titles.SelectedItem is string title)_title.Text=title;};Refresh();
        Closed+=(_,_)=>{_lifetime.Cancel();_job?.Cancel();if(_job==null)DisposeResources();};
    }
    private bool _disposed;
    private void DisposeResources(){if(_disposed)return;_disposed=true;_ai.Dispose();foreach(var image in _images)image.Dispose();}
    private Button Action(string label,Func<Task> work)
    {
        var button=new Button{Content=label,Margin=new Thickness(3),HorizontalAlignment=HorizontalAlignment.Stretch};_buttons.Add(button);
        button.Click+=async(_,_)=>
        {
            if(_job!=null)return;using var job=_job=CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);foreach(var b in _buttons)b.IsEnabled=false;
            try{await work();}catch(OperationCanceledException){if(!_lifetime.IsCancellationRequested)_status.Text="Abgebrochen. Vorhandene Gestaltung bleibt erhalten.";}catch(Exception ex){if(!_lifetime.IsCancellationRequested)_status.Text=ex.Message;}
            finally{_job=null;if(_lifetime.IsCancellationRequested)DisposeResources();else foreach(var b in _buttons)b.IsEnabled=true;}
        };return button;
    }
    private async Task PickAsync()
    {
        var files=await StorageProvider.OpenFilePickerAsync(new(){Title="Set-Video auswählen",AllowMultiple=false,FileTypeFilter=[new("Video"){Patterns=["*.mp4","*.mkv","*.mov","*.webm","*.avi","*.m4v"]}]});
        if(files.FirstOrDefault()?.TryGetLocalPath() is {} path)_video.Text=path;
    }
    private async Task FindAsync()
    {
        var source=_video.Text??"";var folder=Path.Combine(_directory,"artwork","frames",Guid.NewGuid().ToString("N"));
        var frames=await _frames.FindAsync(source,folder,new Progress<string>(s=>_status.Text=s),_job!.Token);
        _choices.Children.Clear();foreach(var frame in frames)AddFrame(frame);SelectFrame(frames[0]);
        _status.Text="9 Stellen über das Video verteilt geprüft; bestbewertetes Frame ausgewählt. Andere Frames anklicken oder einen eigenen Zeitpunkt wählen.";
    }
    private async Task ExtractAsync()
    {
        if(!double.TryParse(_second.Text?.Replace(',','.'),NumberStyles.Float,CultureInfo.InvariantCulture,out var seconds))throw new ArgumentException("Bitte Sekunden eingeben, beispielsweise 123,5.");
        var frame=await _frames.ExtractAsync(_video.Text??"",seconds,Path.Combine(_directory,"artwork","frames",Guid.NewGuid().ToString("N")),_job!.Token);AddFrame(frame);SelectFrame(frame);
    }
    private void AddFrame(SetVideoFrame frame)
    {
        var image=new Bitmap(frame.Path);_images.Add(image);var panel=new StackPanel();panel.Children.Add(new Image{Source=image,Width=142,Height=80,Stretch=Stretch.Uniform});panel.Children.Add(new TextBlock{Text=(frame.AiGenerated?"KI · ":"")+TimeSpan.FromSeconds(frame.Seconds).ToString(@"hh\:mm\:ss"),HorizontalAlignment=HorizontalAlignment.Center});
        var button=new Button{Content=panel};button.Click+=(_,_)=>SelectFrame(frame);_choices.Children.Add(button);
    }
    private void SelectFrame(SetVideoFrame frame)
    {
        _selected=frame;_displayPath=frame.Path;_image=new Bitmap(frame.Path);_images.Add(_image);_second.Text=frame.Seconds.ToString("0.###",CultureInfo.InvariantCulture);
        _brightness.Value=0;_contrast.Value=100;_saturation.Value=100;_gamma.Value=100;
        _frameInfo.Text=$"Ausgewähltes Frame: {TimeSpan.FromSeconds(frame.Seconds):hh\\:mm\\:ss} · 1280 × 720 · Text direkt mit der Maus verschieben";Refresh();
    }
    private async Task GenerateAsync()
    {
        var brief=_brief.Text??"";if(string.IsNullOrWhiteSpace(brief))throw new ArgumentException("Bitte Set-Thema oder Genres beschreiben.");
        _status.Text="Lokale KI lädt und entwickelt Titel und Gestaltung …";
        if(_ai.CpuOnly!=(_cpu.IsChecked==true)){_ai.Stop();_ai.CpuOnly=_cpu.IsChecked==true;}
        var idea=await _ai.CreateArtworkIdeaAsync(brief,_tracks.Text??"",_style.SelectedItem as string??"",_job!.Token);
        _headline.Text=idea.Headline;_subtitle.Text=idea.Subtitle;_accent.Text=idea.Accent;_side.SelectedIndex=idea.Layout=="right"?1:0;
        _titles.ItemsSource=idea.Titles;_titles.SelectedIndex=0;Refresh();_status.Text=$"{idea.Titles.Length} Titelvarianten und Set-Gestaltung erstellt. Texte, Farben und Frame lassen sich weiter bearbeiten.";
    }
    private void Refresh()=>_preview.Child=Compose(_image,_headline.Text??"",_subtitle.Text??"",_brand.Text??"",_accent.Text??"",_side.SelectedIndex==1,_textX.Value/100,_textY.Value/100,(x,y)=>
    {
        _draggingText=true;try{_textX.Value=x*100;_textY.Value=y*100;}finally{_draggingText=false;}
    },CurrentDesign());
    public static Control Compose(Bitmap? frame,string headline,string subtitle,string brand,string accent,bool right,double horizontal=double.NaN,double vertical=.5,Action<double,double>? moved=null,ArtworkDesign? design=null)
    {
        design??=new();
        var color=Color.Parse(SetArtworkIdea.ReadableAccent(accent));var brush=new SolidColorBrush(color);
        var root=new Grid{Width=1280,Height=720,Background=Brush.Parse("#11131D"),ClipToBounds=true};
        if(frame!=null)
        {
            var zoom=Math.Clamp(design.ImageZoom,1,2);var transform=new TransformGroup();transform.Children.Add(new ScaleTransform(zoom,zoom));transform.Children.Add(new TranslateTransform((.5-design.ImageX)*1280*(zoom-1),(.5-design.ImageY)*720*(zoom-1)));
            root.Children.Add(new Image{Source=frame,Stretch=Stretch.UniformToFill,RenderTransformOrigin=RelativePoint.Center,RenderTransform=transform});
        }
        var shade=new LinearGradientBrush{StartPoint=new RelativePoint(right?1:0,0,RelativeUnit.Relative),EndPoint=new RelativePoint(right?0:1,0,RelativeUnit.Relative),GradientStops=new(){new GradientStop(Color.Parse("#F0080B15"),0),new GradientStop(Color.Parse("#C8080B15"),.45),new GradientStop(Color.Parse("#18080B15"),1)}};
        if(design.Background=="Verlauf")root.Children.Add(new Border{Background=shade,Opacity=Math.Clamp(design.Shade/.8,0,1)});
        var panel=new StackPanel{Width=740,Spacing=design.Spacing,Background=design.Background=="Textbox"?new SolidColorBrush(Color.FromArgb((byte)(Math.Clamp(design.Shade,0,1)*255),8,11,21)):Brushes.Transparent};
        if(!string.IsNullOrWhiteSpace(brand))panel.Children.Add(StyledText(brand,design.BrandSize,brush,1,design,color));
        panel.Children.Add(new Border{Background=brush,Width=110,Height=8,HorizontalAlignment=HorizontalAlignment.Left});
        panel.Children.Add(StyledText(headline.ToUpperInvariant(),design.HeadlineSize,Color.TryParse(design.TextColor,out var textColor)?new SolidColorBrush(textColor):Brushes.White,3,design,color));
        panel.Children.Add(StyledText(subtitle,design.SubtitleSize,brush,2,design,color));
        var frameWidth=Math.Clamp(design.FrameSize,1,30);
        var insetY=design.Frame switch{"Letterbox"=>frameWidth*4+16,"Linie" or "Neon"=>20+frameWidth+12,"Ecken"=>24+frameWidth+12,_=>24};
        panel.Measure(new Size(740,double.PositiveInfinity));var scale=Math.Clamp(design.Scale,.4,1.5);scale=Math.Min(scale,(720-2*insetY)/Math.Max(1,panel.DesiredSize.Height));
        var movable=new LayoutTransformControl{Name="ArtworkTextBlock",Child=panel,LayoutTransform=new ScaleTransform(scale,scale)};movable.Measure(new Size(double.PositiveInfinity,double.PositiveInfinity));
        var height=movable.DesiredSize.Height;var width=movable.DesiredSize.Width;
        var rangeX=Math.Max(0,1280-128-width);var rangeY=Math.Max(0,720-2*insetY-height);
        double Unit(double value,double fallback)=>double.IsFinite(value)?Math.Clamp(value,0,1):fallback;
        var x=Unit(horizontal,right?1:0);var y=Unit(vertical,.5);
        if(design.Background=="Band")
        {
            var band=new Border{Height=Math.Min(720,height+32),Background=new SolidColorBrush(Color.FromArgb((byte)(Math.Clamp(design.Shade,0,1)*255),8,11,21))};root.Children.Add(band);
            band.VerticalAlignment=VerticalAlignment.Top;band.Margin=new Thickness(0,Math.Max(0,insetY+y*rangeY-16),0,0);
            // Track the band when the draggable text changes position.
            movable.PropertyChanged+=(_,e)=>{if(e.Property==Canvas.TopProperty)band.Margin=new Thickness(0,Math.Max(0,Canvas.GetTop(movable)-16),0,0);};
        }
        var canvas=new Canvas();canvas.Children.Add(movable);root.Children.Add(canvas);
        void Place(){Canvas.SetLeft(movable,64+x*rangeX);Canvas.SetTop(movable,insetY+y*rangeY);}Place();
        if(moved!=null)
        {
            movable.Cursor=new Cursor(StandardCursorType.SizeAll);ToolTip.SetTip(movable,"Textblock mit gedrückter linker Maustaste verschieben");
            Point? start=null;double originalX=0,originalY=0;
            movable.PointerPressed+=(_,e)=>{if(!e.GetCurrentPoint(movable).Properties.IsLeftButtonPressed)return;start=e.GetPosition(root);originalX=x;originalY=y;e.Pointer.Capture(movable);e.Handled=true;};
            movable.PointerMoved+=(_,e)=>{if(start is not {} origin)return;var point=e.GetPosition(root);x=rangeX>0?Unit(originalX+(point.X-origin.X)/rangeX,x):.5;y=rangeY>0?Unit(originalY+(point.Y-origin.Y)/rangeY,y):.5;Place();moved(x,y);e.Handled=true;};
            movable.PointerReleased+=(_,e)=>{start=null;e.Pointer.Capture(null);e.Handled=true;};
            movable.PointerCaptureLost+=(_,_)=>start=null;
        }
        AddArtworkFrame(root,design,brush,color);
        return root;
    }
    private async Task SaveAsync()
    {
        if(_selected==null||_image==null)throw new InvalidOperationException("Zuerst ein Videoframe auswählen.");
        var file=await StorageProvider.SaveFilePickerAsync(new(){Title="Thumbnail speichern",SuggestedFileName="Set-Thumbnail.png",DefaultExtension="png",FileTypeChoices=[new("PNG"){Patterns=["*.png"]}]});if(file?.TryGetLocalPath() is not {} path)return;
        if(string.Equals(Path.GetFullPath(path),Path.GetFullPath(_selected.Source),StringComparison.OrdinalIgnoreCase)||string.Equals(Path.GetFullPath(path),Path.GetFullPath(_selected.Path),StringComparison.OrdinalIgnoreCase))throw new IOException("Bitte eine separate Ausgabedatei wählen.");
        ExportPng(path,_image,_headline.Text??"",_subtitle.Text??"",_brand.Text??"",_accent.Text??"",_side.SelectedIndex==1,_textX.Value/100,_textY.Value/100,CurrentDesign());
        // Unique sidecars avoid silently replacing independently edited text/project files.
        var sidecar=path+"."+DateTime.Now.ToString("yyyyMMdd-HHmmss-fff");
        await File.WriteAllTextAsync(sidecar+".txt",(_title.Text??"")+Environment.NewLine+Environment.NewLine+string.Join(Environment.NewLine,(_titles.ItemsSource as IEnumerable<string>)??[]),_job!.Token);
        await File.WriteAllTextAsync(sidecar+".json",JsonSerializer.Serialize(new{source=_selected.Source,frame=_selected.Seconds,image=_displayPath,aiGenerated=_selected.AiGenerated,originalImage=_selected.OriginalPath,headline=_headline.Text,subtitle=_subtitle.Text,brand=_brand.Text,accent=_accent.Text,right=_side.SelectedIndex==1,textX=_textX.Value/100,textY=_textY.Value/100,design=CurrentDesign(),title=_title.Text,brief=_brief.Text},new JsonSerializerOptions{WriteIndented=true}),_job.Token);
        _status.Text="Thumbnail (1280 × 720 PNG), Titeltext und Gestaltungsdaten gespeichert: "+path;
    }
    public static void ExportPng(string path,Bitmap image,string headline,string subtitle,string brand,string accent,bool right,double horizontal=double.NaN,double vertical=.5,ArtworkDesign? design=null)
    {
        var visual=Compose(image,headline,subtitle,brand,accent,right,horizontal,vertical,design:design);visual.Measure(new Size(1280,720));visual.Arrange(new Rect(0,0,1280,720));
        using var bitmap=new RenderTargetBitmap(new PixelSize(1280,720),new Vector(96,96));bitmap.Render(visual);bitmap.Save(path,new PngBitmapEncoderOptions());
    }
}
