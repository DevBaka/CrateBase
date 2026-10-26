using System.Text.Json.Serialization;

namespace Cratebase.Core.Models;

public sealed record HistoryAudioTrack(int Id,string Path,string Artist,string Title);
public sealed record HistoryAudioError(HistoryAudioTrack Track,string Error);
public sealed class HistoryAudioMatch : System.ComponentModel.INotifyPropertyChanged
{
    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    public int Id {get;set;}
    public string Path {get;set;}="";
    public string Artist {get;set;}="";
    public string Title {get;set;}="";
    private double _start;
    public double Start {get=>_start;set{_start=value;PropertyChanged?.Invoke(this,new(nameof(Start)));PropertyChanged?.Invoke(this,new(nameof(Timestamp)));}}
    public double End {get;set;}
    public int SupportSeconds {get;set;}
    public double Density {get;set;}
    public double Tempo {get;set;}
    public string Confidence {get;set;}="review";
    public int[] Overlaps {get;set;}=[];
    public int EvidenceCount {get;set;}=1;
    private bool? _include;
    public bool Include {get=>_include??Confidence=="high";set{_include=value;PropertyChanged?.Invoke(this,new(nameof(Include)));}}
    [JsonIgnore] public string Timestamp=>HistoryAudioReport.Timestamp(Start);
    [JsonIgnore] public string Assessment=>(Confidence=="high"?"Starker Audio-Treffer":"Bitte prüfen")+(Overlaps.Length>0?" · mögliche Überlagerung":"");
}
public sealed class HistoryAudioReport
{
    public string Version {get;set;}="";
    public string Recording {get;set;}="";
    public double Duration {get;set;}
    public List<HistoryAudioMatch> Matches {get;set;}=[];
    public List<HistoryAudioMatch> RawMatches {get;set;}=[];
    public List<HistoryAudioTrack> Missing {get;set;}=[];
    public List<HistoryAudioTrack> Unmatched {get;set;}=[];
    public List<HistoryAudioError> Errors {get;set;}=[];
    public string Limitations {get;set;}="";
    public void ConsolidateMatches()
    {
        // Saved review rows may contain edited times/selections. Never rebuild
        // those from the audit data when reopening a saved report.
        if(RawMatches.Count>0||Matches.Count==0)return;
        RawMatches=Matches.Select(Copy).ToList();var result=new List<HistoryAudioMatch>();
        static string Key(HistoryAudioMatch m)=>System.Text.RegularExpressions.Regex.Replace((m.Artist+"\n"+m.Title).Normalize(System.Text.NormalizationForm.FormKC).Trim(),@"\s+"," ").ToUpperInvariant();
        foreach(var track in Matches.GroupBy(m=>string.IsNullOrWhiteSpace(m.Title)?m.Path:Key(m)))
        {
            var group=new List<HistoryAudioMatch>();var end=double.NegativeInfinity;
            foreach(var match in track.OrderBy(m=>m.Start))
            {
                if(group.Count>0&&match.Start>end+20){result.Add(Merge(group));group.Clear();end=double.NegativeInfinity;}
                group.Add(match);end=Math.Max(end,match.End);
            }
            if(group.Count>0)result.Add(Merge(group));
        }
        Matches=result.OrderBy(m=>m.Start).ToList();
    }
    private static HistoryAudioMatch Copy(HistoryAudioMatch m)=>new(){Id=m.Id,Path=m.Path,Artist=m.Artist,Title=m.Title,Start=m.Start,End=m.End,SupportSeconds=m.SupportSeconds,Density=m.Density,Tempo=m.Tempo,Confidence=m.Confidence,Include=m.Include,Overlaps=m.Overlaps.ToArray(),EvidenceCount=m.EvidenceCount};
    private static HistoryAudioMatch Merge(List<HistoryAudioMatch> matches)
    {
        var strong=matches.Where(m=>m.Confidence=="high").ToArray();
        var selected=matches.Where(m=>m.Include).ToArray();
        var timing=selected.Length>0?selected:strong.Length>0?strong:matches.ToArray();
        var best=(strong.Length>0?strong:matches.ToArray()).OrderByDescending(m=>m.SupportSeconds*m.Density).First();
        var row=Copy(best);row.Start=timing.Min(m=>m.Start);row.End=timing.Max(m=>m.End);
        row.Include=selected.Length>0;row.EvidenceCount=matches.Sum(m=>m.EvidenceCount);
        // Do not add correlated votes from copies, or upgrade weak fragments.
        row.Confidence=strong.Length>0?"high":"review";return row;
    }
    public void ApplyConservativeThresholds()
    {foreach(var match in Matches)match.Confidence=match.SupportSeconds>=30&&match.Density>=.60?"high":"review";}
    public void RefreshEvidence(IReadOnlyList<HistoryAudioTrack> tracks)
    {
        var evidence=RawMatches.Count>0?RawMatches:Matches;
        Unmatched=tracks.Where(t=>!evidence.Any(m=>m.Confidence=="high"&&string.Equals(m.Path,t.Path,StringComparison.OrdinalIgnoreCase))).ToList();
        foreach(var match in Matches)match.Overlaps=match.Confidence!="high"?[]:Matches.Where(other=>other.Confidence=="high"&&other.Id!=match.Id&&!(string.Equals(match.Artist,other.Artist,StringComparison.OrdinalIgnoreCase)&&string.Equals(match.Title,other.Title,StringComparison.OrdinalIgnoreCase))&&Math.Min(match.End,other.End)-Math.Max(match.Start,other.Start)>=20).Select(other=>other.Id).Distinct().ToArray();
    }
    public static string Timestamp(double seconds)
    {var span=TimeSpan.FromSeconds(Math.Max(0,Math.Floor(seconds)));return span.TotalHours>=1?$"{(int)span.TotalHours}:{span.Minutes:00}:{span.Seconds:00}":$"{(int)span.TotalMinutes:00}:{span.Seconds:00}";}
    public IReadOnlyList<SetTracklistMarker> Markers(bool labelMashups=false)
    {
        var intervals=new List<HistoryAudioMatch>();
        foreach(var track in Matches.Where(m=>m.Include&&double.IsFinite(m.Start)&&m.Start>=0&&m.Start<Duration&&m.End>m.Start).GroupBy(m=>(m.Artist.Trim().ToUpperInvariant(),m.Title.Trim().ToUpperInvariant())))
        {
            HistoryAudioMatch? previous=null;
            foreach(var item in track.OrderBy(m=>m.Start))
            {
                if(previous!=null&&item.Start<=previous.End){previous.End=Math.Max(previous.End,item.End);continue;}
                previous=new(){Id=item.Id,Artist=item.Artist,Title=item.Title,Start=item.Start,End=item.End,Include=true};intervals.Add(previous);
            }
        }
        var chosen=intervals.OrderBy(m=>m.Start).ToArray();
        var markers=new List<SetTracklistMarker>();
        if(chosen.Length>0&&chosen[0].Start>=1)markers.Add(new(){Position=TimeSpan.Zero,Title="Beginn der Aufnahme"});
        foreach(var group in chosen.GroupBy(m=>(long)Math.Floor(m.Start)))
        {
            var titles=group.Select(m=>string.IsNullOrWhiteSpace(m.Artist)?m.Title:m.Artist+" – "+m.Title).ToList();
            if(labelMashups)
            {
                var current=group.First();
                foreach(var other in chosen.Where(m=>m.Start<current.Start&&m.End-current.Start>=20&&current.End-current.Start>=20&&m.Id!=current.Id))
                    titles.Add(string.IsNullOrWhiteSpace(other.Artist)?other.Title:other.Artist+" – "+other.Title);
            }
            titles=titles.Distinct().ToList();
            markers.Add(new(){Position=TimeSpan.FromSeconds(group.Key),Title=(string.Join(" + ",titles)+(labelMashups&&titles.Count>1?" [möglicher Live-Mashup]":"")).Replace('\r',' ').Replace('\n',' ').Replace('<','‹').Replace('>','›')});
        }
        return markers;
    }
    public string YouTube(bool labelMashups=false)=>string.Join(Environment.NewLine,Markers(labelMashups).Select(m=>Timestamp(m.Position.TotalSeconds)+" "+m.Title));
    public string CompactYouTube(bool labelMashups=false,int maxUtf8Bytes=4500)
    {
        var markers=Markers(labelMashups);var full=YouTube(labelMashups);
        if(System.Text.Encoding.UTF8.GetByteCount(full)<=maxUtf8Bytes)return full;
        for(var length=markers.Max(m=>m.Title.Length);length>=8;length--)
        {
            var lines=markers.Select(m=>
            {
                var runes=m.Title.EnumerateRunes().ToArray();var title=runes.Length<=length?m.Title:string.Concat(runes.Take(length-1).Select(r=>r.ToString()))+"…";
                return Timestamp(m.Position.TotalSeconds)+" "+title;
            });
            var result=string.Join(Environment.NewLine,lines);if(System.Text.Encoding.UTF8.GetByteCount(result)<=maxUtf8Bytes)return result;
        }
        return full; // Never silently omit tracks when even timestamps do not fit.
    }
    public string ChapterHint()
    {
        var markers=Markers();if(markers.Count==0)return "Noch keine Treffer ausgewählt.";
        if(markers.Count<3)return "Für YouTube-Kapitel werden mindestens drei Zeitstempel benötigt. Die Zeitstempel bleiben als Tracklist nutzbar.";
        if(markers.Zip(markers.Skip(1)).Any(pair=>(pair.Second.Position-pair.First.Position).TotalSeconds<10)||Duration-markers[^1].Position.TotalSeconds<10)
            return "Einige Kapitel wären kürzer als zehn Sekunden. YouTube blendet dann möglicherweise keine Kapitel ein; Zeitstempel werden nicht automatisch verschoben.";
        return "YouTube-Kapitelformat: Start bei 00:00, aufsteigend und mindestens zehn Sekunden Abstand. Inhaltliche Hörprüfung bleibt erforderlich.";
    }
}
