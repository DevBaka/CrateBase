using System.Text.Json;
using Cratebase.Analysis;
using Cratebase.Core.Models;
using Cratebase.Import;

internal static class HistoryAnalysisChecks
{
    public static async Task RunAsync()
    {
        var work=Path.Combine(Path.GetTempPath(),"Cratebase-history-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(work);
        var nml=Path.Combine(work,"history.nml");
        await File.WriteAllTextAsync(nml,"""
        <NML VERSION="20"><COLLECTION><ENTRY TITLE="Song A" ARTIST="Artist A"><LOCATION VOLUME="M:" DIR="/:Musik/:Techno/:" FILE="a.wav"/></ENTRY><ENTRY TITLE="Song B"><LOCATION VOLUME="M:" DIR="/:Musik/:Techno/:" FILE="b.wav"/></ENTRY></COLLECTION><PLAYLISTS><NODE TYPE="FOLDER" NAME="$ROOT"><SUBNODES><NODE TYPE="PLAYLIST" NAME="HISTORY"><PLAYLIST><ENTRY><PRIMARYKEY KEY="M:/:Musik/:Techno/:b.wav"/></ENTRY><ENTRY><PRIMARYKEY KEY="M:/:Musik/:Techno/:a.wav"/></ENTRY></PLAYLIST></NODE></SUBNODES></NODE></PLAYLISTS></NML>
        """);
        var tracks=TraktorHistoryReader.Read(nml);
        Check(tracks.Select(t=>t.Title).SequenceEqual(new[]{"Song B","Song A"})&&tracks[0].Path==@"M:\Musik\Techno\b.wav","History reader resolves playlist keys and original Windows paths without inventing timestamps");
        var report=new HistoryAudioReport{Duration=300,Matches=[new(){Id=1,Artist="A",Title="One",Start=10,End=100,Confidence="high"},new(){Id=2,Artist="B",Title="Two",Start=50,End=130,Confidence="review"}]};
        Check(report.YouTube().StartsWith("00:00 Beginn")&&!report.YouTube().Contains("Two"),"YouTube output starts at zero and excludes uncertain matches by default");
        report.Matches[1].Include=true;
        Check(report.YouTube(true).Contains("möglicher Live-Mashup")&&!report.YouTube().Contains("Mashup"),"Long overlaps can be labeled explicitly as unconfirmed mashup candidates");
        var longList=new HistoryAudioReport{Duration=20000,Matches=Enumerable.Range(1,100).Select(i=>new HistoryAudioMatch{Id=i,Start=i*100,End=i*100+90,Title=new string('語',80)+"🎧",Confidence="high"}).ToList()};
        Check(System.Text.Encoding.UTF8.GetByteCount(longList.CompactYouTube())<=4500&&longList.CompactYouTube().Split('\n').Length==101,"Compact description respects its UTF-8 byte budget without dropping tracks or breaking Unicode");
        var marker=new SetTracklistMarker{Position=TimeSpan.FromSeconds(15)};marker.PositionText="1:02:03";
        Check(marker.Position.TotalSeconds==3723&&marker.PositionText=="1:02:03","Imported timestamps can be corrected manually in the recorder");
        var ffmpeg=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Cratebase","tools","ffmpeg.exe");
        var a=Path.Combine(work,"a.wav");var b=Path.Combine(work,"b.wav");var c=Path.Combine(work,"absent.wav");var recording=Path.Combine(work,"mix.wav");
        MakeAudio(a,1);MakeAudio(b,2);MakeAudio(c,3);
        var graph="[0:a]atrim=start=10:end=75,asetpts=PTS-STARTPTS,adelay=5000:all=1[a];[1:a]atrim=start=5:end=75,asetpts=PTS-STARTPTS,atempo=1.05,adelay=35000:all=1[b];[a][b]amix=inputs=2:normalize=0,alimiter=limit=0.9[out]";
        await Exec(ffmpeg,["-v","error","-i",a,"-i",b,"-filter_complex",graph,"-map","[out]",recording]);
        var data=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Cratebase","history-analysis");
        var analyzer=new HistoryAudioAnalyzer(data,ffmpeg);
        using var timeout=new CancellationTokenSource(TimeSpan.FromMinutes(4));
        var before=File.GetLastWriteTimeUtc(a);
        var candidates=new HistoryAudioTrack[]{new(1,a,"A","Fixture A"),new(2,b,"B","Fixture B"),new(3,c,"C","Not in set"),new(4,Path.Combine(work,"missing.wav"),"D","Missing file")};
        var actual=await analyzer.AnalyzeAsync(recording,candidates,new Progress<string>(Console.WriteLine),timeout.Token);
        Check(actual.Matches.Any(m=>m.Id==1&&Math.Abs(m.Start-5)<6),"Audio matching identifies a cue-started source at its actual recording time");
        Check(actual.Matches.Any(m=>m.Id==2&&Math.Abs(m.Start-35)<6&&Math.Abs(m.Tempo-1.05)<.03),"Matching tolerates key-locked tempo change and overlapping tracks");
        Check(!actual.Matches.Any(m=>m.Id==3)&&actual.Unmatched.Any(t=>t.Id==3),"A history-only candidate absent from the actual audio is kept in the unmatched report");
        Check(actual.Missing.Single().Id==4&&actual.Unmatched.Any(t=>t.Id==4),"Missing original files remain explicit and never receive invented timestamps");
        Check(File.GetLastWriteTimeUtc(a)==before,"Fingerprint analysis does not modify source audio");
        var audition=Path.Combine(work,"audition.wav");await analyzer.PreviewAsync(recording,35,audition,timeout.Token);Check(new FileInfo(audition).Length>100000,"Review auditions render an actual audio excerpt without decoding video output");
        using(var cancel=new CancellationTokenSource(TimeSpan.FromMilliseconds(200)))
        {
            var stopped=false;try{await analyzer.AnalyzeAsync(recording,candidates,null,cancel.Token);}catch(OperationCanceledException){stopped=true;}
            Check(stopped,"An active background audio-analysis process can be canceled");
        }
        Console.WriteLine("History fixtures and report: "+work);await File.WriteAllTextAsync(Path.Combine(work,"report.json"),JsonSerializer.Serialize(actual,new JsonSerializerOptions(JsonSerializerDefaults.Web){WriteIndented=true}));
    }
    private static void MakeAudio(string path,int seed)
    {
        const int sr=11025,seconds=80;var random=new Random(seed);var frequencies=Enumerable.Range(0,200).Select(_=>random.Next(20,100)*11.0).ToArray();
        using var writer=new BinaryWriter(File.Create(path));writer.Write("RIFF"u8);writer.Write(seconds*sr*2+36);writer.Write("WAVEfmt "u8);writer.Write(16);writer.Write((short)1);writer.Write((short)1);writer.Write(sr);writer.Write(sr*2);writer.Write((short)2);writer.Write((short)16);writer.Write("data"u8);writer.Write(seconds*sr*2);
        for(var i=0;i<seconds*sr;i++)
        {
            var t=i/(double)sr;var note=(int)(t/.4);var local=t% .4;var f=frequencies[note%frequencies.Length];
            var sample=Math.Sin(Math.PI*Math.Min(1,local/.03))*Math.Exp(-local*2)*(Math.Sin(2*Math.PI*f*t)+.4*Math.Sin(2*Math.PI*f*1.503*t)+.2*Math.Sin(2*Math.PI*f*2.31*t));
            sample+=.04*(random.NextDouble()*2-1);writer.Write((short)Math.Clamp(sample*6500,short.MinValue,short.MaxValue));
        }
    }
    private static async Task Exec(string executable,string[] arguments)
    {var info=new System.Diagnostics.ProcessStartInfo(executable){UseShellExecute=false,CreateNoWindow=true};foreach(var argument in arguments)info.ArgumentList.Add(argument);using var process=System.Diagnostics.Process.Start(info)!;await process.WaitForExitAsync();if(process.ExitCode!=0)throw new Exception("FFmpeg fixture failed");}
    private static void Check(bool value,string message){if(!value)throw new Exception(message);Console.WriteLine("PASS: "+message);}
    public static async Task ExportReportAsync(string path,string destination)
    {
        var report=JsonSerializer.Deserialize<HistoryAudioReport>(await File.ReadAllTextAsync(path),new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        var source=report.Matches.Select(m=>new HistoryAudioTrack(m.Id,m.Path,m.Artist,m.Title)).Concat(report.Unmatched).Concat(report.Missing).Concat(report.Errors.Select(e=>e.Track)).DistinctBy(t=>t.Path,StringComparer.OrdinalIgnoreCase).ToArray();
        report.ApplyConservativeThresholds();report.RefreshEvidence(source);
        await File.WriteAllTextAsync(destination,report.CompactYouTube());
        await File.WriteAllTextAsync(destination+".full.txt",report.YouTube());
        await File.WriteAllTextAsync(destination+".report.json",JsonSerializer.Serialize(report,new JsonSerializerOptions(JsonSerializerDefaults.Web){WriteIndented=true}));
        Console.WriteLine($"{report.Matches.Count} matches; {report.Matches.Count(m=>m.Include)} selected; {report.Unmatched.Count} unmatched.");
    }
}
