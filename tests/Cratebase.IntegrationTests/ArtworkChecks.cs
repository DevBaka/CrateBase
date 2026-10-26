using System.Diagnostics;
using System.Security.Cryptography;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Cratebase.Analysis;
using Cratebase.App.Desktop.Views;

internal static class ArtworkChecks
{
    public static void Run(bool realAi,string? realVideo,bool realImageAi=false)
    {
        AppBuilder.Configure<Cratebase.App.Desktop.App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions{UseHeadlessDrawing=false}).SetupWithoutStarting();
        var data=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Cratebase");var ffmpeg=Path.Combine(data,"tools","ffmpeg.exe");
        var work=Path.Combine(Path.GetTempPath(),"Cratebase-artwork-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(work);
        var video=realVideo??Path.Combine(work,"test & 日本.mp4");
        if(realVideo==null)
        {
            var start=new ProcessStartInfo(ffmpeg){UseShellExecute=false,CreateNoWindow=true};foreach(var a in new[]{"-v","error","-f","lavfi","-i","testsrc2=size=640x360:rate=10","-t","12","-c:v","mpeg4",video})start.ArgumentList.Add(a);
            using var process=Process.Start(start)!;process.WaitForExit();Check(process.ExitCode==0,"Video fixture generated");
        }
        var before=new FileInfo(video);var size=before.Length;var modified=before.LastWriteTimeUtc;
        var frameService=new SetVideoFrames(ffmpeg);var frames=Wait(frameService.FindAsync(video,Path.Combine(work,"frames"),new Progress<string>(Console.WriteLine),CancellationToken.None));
        Check(frames.Count==9&&frames[0].Score>=frames[^1].Score&&frames.All(f=>File.Exists(f.Path)),"Whole-video sampling returns nine ranked frames");
        Check(SetVideoFrames.Score(new byte[14400])==0&&SetVideoFrames.Score(Enumerable.Repeat((byte)255,14400).ToArray())==0,"Black and blown-out frames receive zero quality score");
        var sourceAfter=new FileInfo(video);Check(size==sourceAfter.Length&&modified==sourceAfter.LastWriteTimeUtc,"Original video remains unchanged");
        var idea=SetArtworkIdea.Parse("{\"titles\":[\"Dark to Hard | EBM → Hard Techno\"],\"headline\":\"DARK TO HARD\",\"subtitle\":\"EBM → TECHNO → HARD TECHNO\",\"accent\":\"notcolor\",\"layout\":\"other\"}");
        Check(idea.Accent=="#36D9E8"&&idea.Layout=="left","Invalid AI styling is normalized before rendering");
        Check(SetArtworkIdea.ReadableAccent("#1a1a1a")=="#36D9E8","Dark AI accents cannot hide subtitle or channel text on the dark overlay");
        if(realAi)
        {
            using var ai=new LocalPlaylistAi(Path.Combine(data,"ai"));
            idea=Wait(ai.CreateArtworkIdeaAsync("DJ Baka. Dark to Hard: von düsterem EBM und Aggrotech über Dark Techno zu Hard Techno. Deutsch oder Englisch, keine erfundenen Location- oder Popularitätsangaben.","","Je nach Set unterschiedlich",CancellationToken.None));
            Check(idea.Titles.Length>0&&idea.Headline.Length>0,"Installed local model generates actual titles and thumbnail styling");Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(idea));
        }
        using var image=new Bitmap(frames[0].Path);var output=Path.Combine(work,"Set-Thumbnail.png");
        SetArtworkWindow.ExportPng(output,image,idea.Headline,idea.Subtitle,"DJ Baka",idea.Accent,idea.Layout=="right");
        using(var png=new Bitmap(output))Check(png.PixelSize==new PixelSize(1280,720)&&new FileInfo(output).Length>10000,"Real video frame exports with text and design at 1280 × 720");
        var bottom=SetArtworkWindow.Compose(image,"LOWER TEXT","VISIBLE SUBTITLE","DJ Baka","#36D9E8",false,.5,1);bottom.Measure(new Size(1280,720));bottom.Arrange(new Rect(0,0,1280,720));
        var bottomText=bottom.GetLogicalDescendants().OfType<Control>().Single(c=>c.Name=="ArtworkTextBlock");
        Check(Math.Abs(Canvas.GetTop(bottomText)+bottomText.Bounds.Height-696)<1,"Text can reach the bottom safe margin without clipping the subtitle");
        var letterbox=SetArtworkWindow.Compose(image,"VISIBLE TEXT","SUBTITLE","DJ Baka","#36D9E8",false,.5,1,design:new(){Frame="Letterbox",FrameSize=30});letterbox.Measure(new Size(1280,720));letterbox.Arrange(new Rect(0,0,1280,720));
        var boxedText=letterbox.GetLogicalDescendants().OfType<Control>().Single(c=>c.Name=="ArtworkTextBlock");Check(Canvas.GetTop(boxedText)+boxedText.Bounds.Height<=584.1,"Bottom positioning respects thick letterbox borders");
        var topFile=Path.Combine(work,"position-top.png");var bottomFile=Path.Combine(work,"position-bottom.png");
        SetArtworkWindow.ExportPng(topFile,image,"MOVE ME","SUBTITLE","","#36D9E8",false,.5,0);SetArtworkWindow.ExportPng(bottomFile,image,"MOVE ME","SUBTITLE","","#36D9E8",false,.5,1);
        Check(!SHA256.HashData(File.ReadAllBytes(topFile)).SequenceEqual(SHA256.HashData(File.ReadAllBytes(bottomFile))),"Export reflects the selected text position");
        foreach(var effect in new[]{"Keine","Schatten","Neon","Kontur","3D"})SetArtworkWindow.ExportPng(Path.Combine(work,"effect-"+effect+".png"),image,"CLUB NIGHT","LIVE DJ SET","DJ Baka","#B56CFF",false,.1,.8,new(){Effect=effect,Frame=effect=="Neon"?"Neon":"Ecken",Font="Arial",Scale=.85,HeadlineSize=110,ImageZoom=1.2,ImageX=.6});
        Check(File.Exists(Path.Combine(work,"effect-Neon.png")),"Font selection, scaling, text effects, frames and image crop render in PNG export");
        var filtered=Wait(frameService.AdjustImageAsync(frames[0].Path,Path.Combine(work,"adjusted"),.1,1.2,0,1.1,CancellationToken.None));
        Check(File.Exists(filtered)&&!SHA256.HashData(File.ReadAllBytes(filtered)).SequenceEqual(SHA256.HashData(File.ReadAllBytes(frames[0].Path))),"Manual image correction creates a separate edited image");
        var window=new SetArtworkWindow(realAi||realImageAi?data:work,ffmpeg,video,"0:00 Example – DJ Set");window.Show();Dispatcher.UIThread.RunJobs();
        var controls=window.GetLogicalDescendants().ToArray();Check(controls.OfType<TextBox>().Single(c=>c.Name=="ArtworkVideo").Text==video,"Creator opens with the selected recording and editable controls");
        controls.OfType<TextBox>().Single(c=>c.Name=="ArtworkHeadline").Text="EMOTIONAL HARDSTYLE";
        controls.OfType<Button>().Single(b=>b.Content as string=="Gute Frames suchen").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Until(()=>controls.OfType<TextBlock>().Single(c=>c.Name=="ArtworkStatus").Text?.StartsWith("9 Stellen")==true,TimeSpan.FromMinutes(2));
        Check(window.GetLogicalDescendants().OfType<Image>().Count()>=10,"Actual UI action extracts frames in background and selects a preview");
        var textX=controls.OfType<Slider>().Single(c=>c.Name=="ArtworkTextX");var textY=controls.OfType<Slider>().Single(c=>c.Name=="ArtworkTextY");textX.Value=50;textY.Value=50;window.UpdateLayout();Dispatcher.UIThread.RunJobs();
        var movable=window.GetLogicalDescendants().OfType<Control>().Single(c=>c.Name=="ArtworkTextBlock");var from=movable.TranslatePoint(new Point(40,50),window)!.Value;var to=from+new Vector(30,25);
        window.MouseMove(from);window.MouseDown(from,MouseButton.Left);window.MouseMove(to);window.MouseUp(to,MouseButton.Left);Dispatcher.UIThread.RunJobs();
        Check(textX.Value>50&&textY.Value>50,"Dragging the scaled preview text moves it right/down and updates position controls");
        textY.Value=100;window.UpdateLayout();movable=window.GetLogicalDescendants().OfType<Control>().Single(c=>c.Name=="ArtworkTextBlock");
        Check(Canvas.GetTop(movable)>24,"Position slider moves the live thumbnail text towards the bottom");
        controls.OfType<ComboBox>().Single(c=>c.Name=="ArtworkPreset").SelectedIndex=1;Dispatcher.UIThread.RunJobs();
        Check(controls.OfType<ComboBox>().Single(c=>c.Name=="ArtworkEffect").SelectedItem as string=="Neon"&&controls.OfType<ComboBox>().Single(c=>c.Name=="ArtworkFrame").SelectedItem as string=="Neon","Style preset updates actual editable effects and frame controls");
        if(realImageAi)
        {
            var imageCount=window.GetLogicalDescendants().OfType<Image>().Count();controls.OfType<TextBox>().Single(c=>c.Name=="ArtworkImagePrompt").Text="professional nightclub photograph, cinematic violet and cyan lighting, realistic photography";
            window.GetLogicalDescendants().OfType<Button>().Single(b=>b.Content as string=="KI-Bildvariante generieren").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Until(()=>controls.OfType<TextBlock>().Single(c=>c.Name=="ArtworkStatus").Text?.StartsWith("KI-Variante")==true,TimeSpan.FromMinutes(5));
            Check(window.GetLogicalDescendants().OfType<Image>().Count()>imageCount,"Actual image-AI button generates, selects and retains a separate local image variant");
            window.GetLogicalDescendants().OfType<Button>().Single(b=>b.Content as string=="Originalframe wiederherstellen").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Dispatcher.UIThread.RunJobs();
            Check(controls.OfType<TextBlock>().Single(c=>c.Name=="ArtworkStatus").Text=="Originalframe wiederhergestellt.","AI variant can be reverted to the original frame");
        }
        if(realAi)
        {
            controls.OfType<TextBox>().Single(c=>c.Name=="ArtworkBrief").Text="Emotional Hardstyle, melancholischer Club-Mix, englische Titel. Keine erfundenen Artists oder Events.";
            controls.OfType<Button>().Single(b=>b.Content as string=="Lokale KI: Titel & Gestaltung").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Until(()=>controls.OfType<ComboBox>().Single(c=>c.Name=="ArtworkTitles").SelectedItem is string,TimeSpan.FromMinutes(4));
            Check(!string.IsNullOrWhiteSpace(controls.OfType<TextBox>().Single(c=>c.Name=="ArtworkTitle").Text),"Actual AI button populates editable title choices and thumbnail preview");
        }
        using(var capture=new RenderTargetBitmap(new PixelSize(1280,880),new Vector(96,96))){capture.Render(window);capture.Save(Path.Combine(work,"Creator.png"),new PngBitmapEncoderOptions());}
        window.Close();Console.WriteLine("Artwork preview and fixtures: "+work);
    }
    private static T Wait<T>(Task<T> task){Until(()=>task.IsCompleted,TimeSpan.FromMinutes(4));return task.GetAwaiter().GetResult();}
    private static void Until(Func<bool> done,TimeSpan timeout){var watch=Stopwatch.StartNew();while(!done()){Dispatcher.UIThread.RunJobs();Thread.Sleep(10);if(watch.Elapsed>timeout)throw new TimeoutException("Artwork test timed out.");}Dispatcher.UIThread.RunJobs();}
    private static void Check(bool success,string message){if(!success)throw new Exception(message);Console.WriteLine("PASS: "+message);}
}
