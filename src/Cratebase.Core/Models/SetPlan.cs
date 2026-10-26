using System.Globalization;
using System.Text.RegularExpressions;

namespace Cratebase.Core.Models;

public sealed class SetPlan
{
    public string Id {get;set;}=Guid.NewGuid().ToString();
    public string Name {get;set;}="Neues Set";
    public List<SetPlanEntry> Entries {get;set;}=[];
    public List<SetReference> References {get;set;}=[];
    public const string StateKey="set-plans-v1";
}
public sealed record SetReference(string Url,string Tracklist);
public sealed class SetPlanEntry
{
    public long TrackId {get;set;}
    public long PlayMs {get;set;}
    public long BassTradeMs {get;set;}
    public long FadeOutMs {get;set;}
    public string Note {get;set;}="";
    public static SetPlanEntry For(Track track)
    {
        var end=Math.Max(0,track.DurationMs);
        return new() {TrackId=track.Id,PlayMs=0,BassTradeMs=Math.Max(0,end-16*BeatMs(track)),FadeOutMs=end};
    }
    private static long BeatMs(Track t)=>(long)(60000/(t.Bpm is >0? t.Bpm.Value:128));
    public void Validate(Track track)
    {
        if(PlayMs<0||BassTradeMs<PlayMs||FadeOutMs<=BassTradeMs||FadeOutMs>track.DurationMs)
            throw new ArgumentException("Marker müssen innerhalb des Tracks liegen: Play ≤ Basswechsel < Fade-out.");
    }
}

public sealed record SetPlanningOptions(int Minutes=60,double MaxBpmStep=4,bool AllowGenreChanges=false,int MaxTracks=100);
public sealed record TransitionAssessment(bool Allowed,double Cost,string Explanation);

/// <summary>Metadata-based suggestions, not audio/phrase analysis. No genre popularity prior.</summary>
public static class ClubSetPlanner
{
    public static string GenreFamily(Track track)
    {
        var genre=(track.SubGenre+" "+track.GenreRaw).ToLowerInvariant();
        // Most specific families first; keep hard dance and techno separate unless explicitly allowed.
        foreach(var family in new[]{"hardstyle","hardcore","hands up","drum and bass","dubstep","trance","techno","house","breakbeat","aggrotech","ebm","goth","industrial","electro"})
            if(genre.Contains(family,StringComparison.Ordinal))return family;
        return (track.GenreRaw??"").Trim().ToLowerInvariant();
    }
    public static (int Number,char Mode)? Camelot(string? value)
    {
        if(string.IsNullOrWhiteSpace(value))return null;
        var key=value.Trim().Replace("♯","#").Replace("♭","b");
        var camelot=Regex.Match(key,@"^(1[0-2]|[1-9])([AB])$",RegexOptions.IgnoreCase);
        if(camelot.Success)return(int.Parse(camelot.Groups[1].Value,CultureInfo.InvariantCulture),char.ToUpperInvariant(camelot.Groups[2].Value[0]));
        var open=Regex.Match(key,@"^(1[0-2]|[1-9])([md])$",RegexOptions.IgnoreCase);
        if(open.Success)return((int.Parse(open.Groups[1].Value,CultureInfo.InvariantCulture)+6)%12+1,char.ToLowerInvariant(open.Groups[2].Value[0])=='m'?'A':'B');
        key=key.Replace(" minor","m",StringComparison.OrdinalIgnoreCase).Replace(" major","",StringComparison.OrdinalIgnoreCase).Replace("min","m",StringComparison.OrdinalIgnoreCase).Replace("maj","",StringComparison.OrdinalIgnoreCase).Replace(" ","");
        var minor=new[]{"Abm|G#m","Ebm|D#m","Bbm|A#m","Fm","Cm","Gm","Dm","Am","Em","Bm","F#m|Gbm","C#m|Dbm"};
        var major=new[]{"B|Cb","F#|Gb","Db|C#","Ab|G#","Eb|D#","Bb|A#","F","C","G","D","A","E"};
        for(var i=0;i<12;i++){if(minor[i].Split('|').Contains(key,StringComparer.OrdinalIgnoreCase))return(i+1,'A');if(major[i].Split('|').Contains(key,StringComparer.OrdinalIgnoreCase))return(i+1,'B');}
        return null;
    }
    public static TransitionAssessment Assess(Track a,Track b,SetPlanningOptions options)
    {
        if(a.Id==b.Id||b.Trash||b.MissingSinceUtc!=null||b.DurationMs<=0)return new(false,double.PositiveInfinity,"Track fehlt, ist Trash oder hat keine bekannte Dauer.");
        var ga=GenreFamily(a);var gb=GenreFamily(b);
        if(!options.AllowGenreChanges&&ga!=gb)return new(false,double.PositiveInfinity,"Genre-Grenze: "+ga+" → "+gb);
        var delta=a.Bpm is >0&&b.Bpm is >0?Math.Abs(a.Bpm.Value-b.Bpm.Value):(double?)null;
        if(delta>options.MaxBpmStep)return new(false,double.PositiveInfinity,$"BPM-Sprung {delta:0.0} überschreitet das Limit.");
        var ka=Camelot(a.MusicalKey);var kb=Camelot(b.MusicalKey);var harmonic=false;
        if(ka.HasValue&&kb.HasValue){var distance=Math.Abs(ka.Value.Number-kb.Value.Number);harmonic=ka==kb||(ka.Value.Mode==kb.Value.Mode&&(distance==1||distance==11))||(ka.Value.Number==kb.Value.Number);}
        var cost=(delta??options.MaxBpmStep)*2+(ka==null||kb==null?5:harmonic?0:10)+(ga==gb?0:12)+(string.Equals(a.Artist,b.Artist,StringComparison.OrdinalIgnoreCase)?4:0);
        return new(true,cost,(delta.HasValue?$"Δ {delta:0.0} BPM":"BPM unbekannt")+" · "+(ka==null||kb==null?"Key unbekannt":harmonic?"harmonisch kompatibel":"Key-Konflikt; vorhören")+(ga==gb?"":" · Genre-Wechsel"));
    }
    public static IReadOnlyList<Track> Generate(IReadOnlyList<Track> candidates,SetPlanningOptions options,CancellationToken ct=default)
    {
        if(options.Minutes is <1 or >1440||options.MaxBpmStep is <0 or >50||!double.IsFinite(options.MaxBpmStep)||options.MaxTracks is <1 or >200)throw new ArgumentException("Ungültige Set-Länge oder BPM-Grenze.");
        var pool=candidates.Where(t=>!t.Trash&&t.MissingSinceUtc==null&&t.DurationMs>0).DistinctBy(t=>t.Id).ToArray();
        if(pool.Length==0)return [];
        // Several starting points prevent the result depending solely on alphabetical database order.
        List<Track>? best=null;double bestScore=double.NegativeInfinity;
        foreach(var seed in pool.OrderBy(t=>t.Bpm??double.MaxValue).ThenByDescending(t=>t.Rating).Where((_,i)=>i%Math.Max(1,pool.Length/16)==0).Take(16))
        {
            ct.ThrowIfCancellationRequested();var sequence=new List<Track>{seed};var used=new HashSet<long>{seed.Id};long duration=seed.DurationMs;double cost=0;
            while(duration<options.Minutes*60000L&&sequence.Count<options.MaxTracks)
            {
                ct.ThrowIfCancellationRequested();var previous=sequence[^1];
                var next=pool.Where(t=>!used.Contains(t.Id)).Select(t=>(Track:t,Fit:Assess(previous,t,options))).Where(t=>t.Fit.Allowed).OrderBy(t=>t.Fit.Cost).ThenByDescending(t=>t.Track.Rating).ThenBy(t=>t.Track.Id).FirstOrDefault();
                if(next.Track==null)break;sequence.Add(next.Track);used.Add(next.Track.Id);duration+=next.Track.DurationMs;cost+=next.Fit.Cost;
            }
            var score=Math.Min(duration,options.Minutes*60000L)/60000.0*100-cost;
            if(score>bestScore){best=sequence;bestScore=score;}
        }
        return best??[];
    }
}
