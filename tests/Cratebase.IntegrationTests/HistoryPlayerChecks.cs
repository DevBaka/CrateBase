using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Cratebase.App.Desktop.Controls;
using Cratebase.App.Desktop.Views;
using Cratebase.Core.Abstractions;
using Cratebase.Core.Models;

internal static class HistoryPlayerChecks
{
    public static void Run(bool native=false)
    {
        if(native)
        {
            Exception? error=null;var thread=new Thread(()=>{try{RunNative();}catch(Exception ex){error=ex;}});thread.SetApartmentState(ApartmentState.STA);thread.Start();thread.Join();if(error!=null)throw new Exception("Native history player check failed",error);return;
        }
        AppBuilder.Configure<Cratebase.App.Desktop.App>().UseHeadless(new()).SetupWithoutStarting();
        var work=Path.Combine(Path.GetTempPath(),"Cratebase-review-player-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(work);
        var wave=Path.Combine(work,"source.wav");MakeWave(wave);
        var owner=new Window{Width=1000,Height=600};var audios=new List<Audio>();var pauseCount=0;
        using var player=new HistoryReviewPlayer(owner,work,Ffmpeg(),()=>pauseCount++,Console.WriteLine,()=>{var audio=new Audio();audios.Add(audio);return audio;});
        var panel=new StackPanel();panel.Children.Add(player.Controls);panel.Children.Add(player.VideoPanel);owner.Content=panel;owner.Show();
        player.Configure(wave,12,[new(){Id=1,Start=2,End=5,Title="One",Confidence="high"},new(){Id=2,Start=5,End=10,Title="Two",Confidence="high"}]);
        var timeline=player.Controls.GetLogicalDescendants().OfType<WaveformView>().Single();
        Until(()=>timeline.Waveform?.HasData==true);Check(timeline.Markers!.Count==2,"Audio review shows a generated RGB waveform with track markers");
        Wait(player.PlayAtAsync(2));Until(()=>player.PositionSeconds==2);
        Check(audios.Single().Path==wave&&pauseCount==1,"Review plays the full original recording instead of a short rendered excerpt");
        Click("HistoryReviewPlay");Check(player.State==PlaybackState.Paused,"Review transport pauses playback");
        Click("HistoryReviewPlay");Check(player.State==PlaybackState.Playing,"Review transport resumes the same player");
        timeline.SeekCommand!.Execute(.75d);Until(()=>player.PositionSeconds==9);Check(audios.Single().Position.TotalSeconds==9,"Timeline seek updates actual audio playback");
        Wait(player.SeekAsync(2));ClickContent("Marker ⏭");Until(()=>player.PositionSeconds==5);Check(player.PositionSeconds==5,"Next marker jumps to the exact next track start");
        var video=Path.Combine(work,"source.mp4");File.WriteAllBytes(video,[]);player.Configure(video,12,[]);Wait(player.PlayAtAsync(2));Until(()=>player.PositionSeconds==2);
        var current=audios.Last();player.DetachVideo();Dispatcher.UIThread.RunJobs();Check(player.IsDetached&&player.State==PlaybackState.Playing&&audios.Count==2,"Detaching video retains the playback session and position");
        player.RedockVideo();Dispatcher.UIThread.RunJobs();Check(!player.IsDetached&&ReferenceEquals(current,audios.Last())&&!current.Disposed,"Redocking video does not stop or recreate the media player");
        player.SetVideoVisible(false);Check(player.State==PlaybackState.Playing,"Collapsing video keeps audio playback running");
        player.Stop();Check(current.Disposed&&player.State==PlaybackState.Stopped,"Review stop disposes native/audio resources");owner.Close();
        void Click(string name){player.Controls.GetLogicalDescendants().OfType<Button>().Single(b=>b.Name==name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Dispatcher.UIThread.RunJobs();}
        void ClickContent(string content){player.Controls.GetLogicalDescendants().OfType<Button>().Single(b=>b.Content as string==content).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Dispatcher.UIThread.RunJobs();}
    }
    private static void RunNative()
    {
        if(!OperatingSystem.IsWindows())throw new PlatformNotSupportedException();
        AppBuilder.Configure<Cratebase.App.Desktop.App>().UsePlatformDetect().SetupWithoutStarting();LibVLCSharp.Shared.Core.Initialize();
        var work=Path.Combine(Path.GetTempPath(),"Cratebase-review-video-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(work);
        var mp4=Path.Combine(work,"fixture.mp4");
        Exec(["-v","error","-f","lavfi","-i","testsrc2=size=640x360:rate=25","-f","lavfi","-i","sine=frequency=500:sample_rate=44100","-af","volume=0.000001","-t","12","-c:v","mpeg4","-c:a","aac",mp4]);
        foreach(var extension in new[]{"mkv","mov"})Exec(["-v","error","-i",mp4,"-c","copy",Path.Combine(work,"fixture."+extension)]);
        var owner=new Window{Title="Cratebase automated video check",Width=900,Height=650,Position=new PixelPoint(-12000,-12000),ShowInTaskbar=false};
        using var player=new HistoryReviewPlayer(owner,work,Ffmpeg(),()=>{},Console.WriteLine);
        var panel=new Grid{RowDefinitions=new("*,Auto")};panel.Children.Add(player.VideoPanel);Grid.SetRow(player.Controls,1);panel.Children.Add(player.Controls);owner.Content=panel;owner.Show();
        foreach(var extension in new[]{"mp4","mkv","mov"})
        {
            player.Configure(Path.Combine(work,"fixture."+extension),12,[]);Wait(player.PlayAtAsync(2));
            Until(()=>player.PositionSeconds>=1.8&&player.HasVideoOutput);Check(player.HasVideoOutput&&player.VideoHandle!=IntPtr.Zero,"Real LibVLC video and audio playback works for "+extension.ToUpperInvariant());
        }
        var handle=player.VideoHandle;var position=player.PositionSeconds;player.DetachVideo();
        foreach(var popup in owner.OwnedWindows){popup.ShowInTaskbar=false;popup.Position=new PixelPoint(-12000,-12000);}
        Until(()=>player.PositionSeconds>position+.4);Check(player.VideoHandle==handle&&player.HasVideoOutput,"Actual Windows video surface is reparented into pop-out while playback continues");
        player.RedockVideo();Dispatcher.UIThread.RunJobs();Check(player.VideoHandle==handle&&player.HasVideoOutput,"Actual native video surface survives redocking without a decoder restart");
        player.SetVideoVisible(false);position=player.PositionSeconds;Until(()=>player.PositionSeconds>position+.3);Check(player.State==PlaybackState.Playing,"Native video collapse does not interrupt the audio clock");
        Wait(player.SeekAsync(7));Until(()=>player.PositionSeconds>=6.8);Wait(player.ToggleAsync());Until(()=>player.State==PlaybackState.Paused);Check(player.State==PlaybackState.Paused,"Native review seek and pause work after docking changes");
        Wait(player.ToggleAsync());Until(()=>player.State==PlaybackState.Playing);player.Stop();owner.Close();Console.WriteLine("Native video fixtures: "+work);
    }
    private static string Ffmpeg()=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Cratebase","tools","ffmpeg.exe");
    private static void Exec(string[] args)
    {var info=new System.Diagnostics.ProcessStartInfo(Ffmpeg()){UseShellExecute=false,CreateNoWindow=true};foreach(var arg in args)info.ArgumentList.Add(arg);using var process=System.Diagnostics.Process.Start(info)!;process.WaitForExit();if(process.ExitCode!=0)throw new Exception("Video fixture failed");}
    private static void MakeWave(string path)
    {using var w=new BinaryWriter(File.Create(path));const int sr=11025,count=sr*12;w.Write("RIFF"u8);w.Write(count*2+36);w.Write("WAVEfmt "u8);w.Write(16);w.Write((short)1);w.Write((short)1);w.Write(sr);w.Write(sr*2);w.Write((short)2);w.Write((short)16);w.Write("data"u8);w.Write(count*2);for(var i=0;i<count;i++)w.Write((short)(4000*Math.Sin(2*Math.PI*440*i/sr)));}
    private static void Wait(Task task){Until(()=>task.IsCompleted);task.GetAwaiter().GetResult();}
    private static void Until(Func<bool> done){var end=DateTime.UtcNow.AddSeconds(15);while(!done()){if(DateTime.UtcNow>end)throw new TimeoutException();Dispatcher.UIThread.RunJobs();Thread.Sleep(10);}Dispatcher.UIThread.RunJobs();}
    private static void Check(bool value,string message){if(!value)throw new Exception(message);Console.WriteLine("PASS: "+message);}
    private sealed class Audio : IAudioPlayer
    {
        public bool Disposed;public string Path="";
        public PlaybackState State{get;private set;}public TimeSpan Position{get;private set;}public TimeSpan Duration=>TimeSpan.FromSeconds(12);public float Volume{get;set;}
        public event EventHandler? PlaybackEnded{add{}remove{}}public event EventHandler? PositionChanged;
        public void Load(string path){Path=path;Position=TimeSpan.Zero;}public void Play(){State=PlaybackState.Playing;PositionChanged?.Invoke(this,EventArgs.Empty);}public void Pause()=>State=PlaybackState.Paused;
        public void Stop()=>State=PlaybackState.Stopped;public void Seek(TimeSpan position){Position=position;PositionChanged?.Invoke(this,EventArgs.Empty);}public void Dispose()=>Disposed=true;
    }
}
