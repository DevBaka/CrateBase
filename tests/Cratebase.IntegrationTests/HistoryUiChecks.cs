using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Cratebase.App.Desktop.ViewModels;
using Cratebase.App.Desktop.Views;
using Cratebase.Core.Models;

internal static class HistoryUiChecks
{
    public static void Run(string? reportPath=null)
    {
        AppBuilder.Configure<Cratebase.App.Desktop.App>().UseHeadless(new(){UseHeadlessDrawing=false}).UseSkia().SetupWithoutStarting();
        var vm=new MainViewModel(null!,null!,null!,null!,null!,null!,null!,null!,null!,null!,null!,null!,null!,null!,false);
        vm.MergeHistoryMarkers([new(){Position=TimeSpan.FromSeconds(2),Artist="Existing",Title="Manual marker"}]);
        var work=Path.Combine(Path.GetTempPath(),"Cratebase-history-ui-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(work);
        var owner=new Window();owner.Show();
        var window=new HistoryAnalysisWindow(work,"ffmpeg",markers=>{vm.MergeHistoryMarkers(markers);return Task.CompletedTask;},()=>{});
        window.Show(owner);
        var report=new HistoryAudioReport{Recording="fixture.mp4",Duration=300,Limitations="Zeitstempel bitte prüfen.",Matches=[new(){Id=1,Artist="A",Title="Known",Start=20,End=150,Confidence="high"},new(){Id=2,Artist="B",Title="Uncertain",Start=80,End=130,Confidence="review"}]};
        window.ApplyReport(report);Dispatcher.UIThread.RunJobs();window.UpdateLayout();
        var table=window.GetLogicalDescendants().OfType<DataGrid>().Single();
        Check(table.ItemsSource!.Cast<HistoryAudioMatch>().Count()==1&&report.Matches[0].Include&&!report.Matches[1].Include,"History review initially shows strong matches without weak false candidates cluttering the list");
        var showUncertain=window.GetLogicalDescendants().OfType<CheckBox>().Single(c=>c.Name=="HistoryShowUncertain");showUncertain.IsChecked=true;
        Check(table.ItemsSource!.Cast<HistoryAudioMatch>().Count()==2,"Uncertain candidates remain available through an explicit review toggle");
        Check(table.Columns.OfType<DataGridTextColumn>().Single(c=>c.Header as string=="Start (Sekunden, editierbar)").IsReadOnly==false,"History start times are editable before publishing");
        var fragments=new HistoryAudioReport{Duration=600,Matches=[
            new(){Id=1,Path="a.wav",Artist="A",Title="Same song",Start=20,End=100,Confidence="high",SupportSeconds=60,Density=.9},
            new(){Id=3,Path="copy.flac",Artist="A",Title="Same song",Start=20,End=100,Confidence="high",SupportSeconds=60,Density=.9},
            new(){Id=1,Path="a.wav",Artist="A",Title="Same song",Start=5,End=45,Confidence="review",SupportSeconds=12,Density=.3},
            new(){Id=1,Path="a.wav",Artist="A",Title="Same song",Start=250,End=300,Confidence="high",SupportSeconds=40,Density=.8},
            new(){Id=2,Path="b.wav",Artist="B",Title="Other song",Start=60,End=140,Confidence="review"}]};
        window.ApplyReport(fragments);
        Check(fragments.Matches.Count==3&&fragments.RawMatches.Count==5,"Track copies and overlapping fragments become one review row per occurrence while raw evidence is retained");
        var combined=fragments.Matches.Single(m=>m.Title=="Same song"&&m.Start<100);
        Check(combined.Start==20&&combined.EvidenceCount==3&&fragments.Matches.Any(m=>m.Title=="Same song"&&m.Start==250),"Weak early fragments do not move a strong start, and later replays remain separate");
        combined.Start=24;combined.Include=false;
        var reopened=System.Text.Json.JsonSerializer.Deserialize<HistoryAudioReport>(System.Text.Json.JsonSerializer.Serialize(fragments))!;
        window.ApplyReport(reopened);
        Check(reopened.Matches.Any(m=>m.Start==24&&!m.Include)&&reopened.RawMatches.Count==5,"Reopening a grouped report preserves edited timestamps and selections");
        window.ApplyReport(report);
        Click("Auswahl in Recorder-Tracklist übernehmen");
        Check(vm.TracklistMarkers.Count==3&&vm.TracklistMarkers.Any(m=>m.Title=="Manual marker")&&!vm.TracklistPreview.Contains("Uncertain"),"Import preserves existing markers and excludes unchecked matches");
        Click("Auswahl in Recorder-Tracklist übernehmen");Check(vm.TracklistMarkers.Count==3,"Repeated history import does not duplicate identical markers");
        Click("Alle abwählen");Check(report.Matches.All(m=>!m.Include),"Review selection can be cleared without losing evidence");
        Click("Nur starke Treffer auswählen");Check(report.Matches.Count(m=>m.Include)==1,"Strong-match selection can be restored explicitly");
        var history=Path.Combine(work,"thread-check.nml");
        File.WriteAllText(history,"""
        <NML VERSION="20"><COLLECTION><ENTRY TITLE="Thread fixture"><LOCATION VOLUME="M:" DIR="/:Music/:" FILE="fixture.wav"/></ENTRY></COLLECTION></NML>
        """);
        window.GetLogicalDescendants().OfType<TextBox>().Single(t=>t.Name=="HistoryNmlPath").Text=history;
        window.GetLogicalDescendants().OfType<TextBox>().Single(t=>t.Name=="HistoryRecordingPath").Text=Path.Combine(work,"not-present.wav");
        var start=window.GetLogicalDescendants().OfType<Button>().Single(b=>b.Content as string=="Audio-Abgleich starten");
        Click("Audio-Abgleich starten");var deadline=DateTime.UtcNow.AddSeconds(10);
        while(!start.IsEnabled){if(DateTime.UtcNow>deadline)throw new TimeoutException();Dispatcher.UIThread.RunJobs();Thread.Sleep(5);}
        var status=window.GetLogicalDescendants().OfType<TextBlock>().Single(t=>t.Name=="HistoryAnalysisStatus").Text;
        Check(status=="Aufnahme nicht erreichbar.","Actual Analyze button reads NML in the background without accessing UI controls from a worker: "+status);
        Check(table.ItemsSource!.Cast<HistoryAudioMatch>().Count()==2,"Failed analysis keeps the previous review results intact");
        if(reportPath!=null)
        {
            var real=System.Text.Json.JsonSerializer.Deserialize<HistoryAudioReport>(File.ReadAllText(reportPath),new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!;
            window.ApplyReport(real);Dispatcher.UIThread.RunJobs();window.UpdateLayout();
            showUncertain.IsChecked=false;
            Check(table.ItemsSource!.Cast<HistoryAudioMatch>().Count()==real.Matches.Count(m=>m.Confidence=="high"||m.Include)&&table.Bounds.Height>50,"Full real-world report remains reviewable with long track names and uncertainty notes");
        }
        using(var bitmap=new Avalonia.Media.Imaging.RenderTargetBitmap(new PixelSize((int)window.Bounds.Width,(int)window.Bounds.Height),new Vector(96,96))){bitmap.Render(window);bitmap.Save(Path.Combine(work,"history-review.png"),new Avalonia.Media.Imaging.PngBitmapEncoderOptions());}
        Console.WriteLine("History UI screenshot: "+Path.Combine(work,"history-review.png"));window.Close();owner.Close();
        void Click(string content){window.GetLogicalDescendants().OfType<Button>().Single(b=>b.Content as string==content).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Dispatcher.UIThread.RunJobs();}
    }
    private static void Check(bool value,string message){if(!value)throw new Exception(message);Console.WriteLine("PASS: "+message);}
}
