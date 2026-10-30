using System.Globalization;
using System.Xml.Linq;
using Cratebase.Core.Models;
namespace Cratebase.Import;

public sealed record HistorySession(string Path,string Name,DateTime ModifiedUtc);
public sealed record HistorySessionTrack(string Session,string FullPath,Track Track,double? PlayedSeconds);
public sealed record HistoryMergeRow(HistorySessionTrack Source,string Review);
public static class HistorySessionCatalog
{
    public static IReadOnlyList<string> DefaultFolders()
    {
        var ni=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),"Native Instruments");
        return Directory.Exists(ni)?Directory.GetDirectories(ni,"Traktor*").Select(p=>Path.Combine(p,"History")).Where(Directory.Exists).ToArray():[];
    }
    public static IReadOnlyList<HistorySession> Discover(IEnumerable<string> folders)
    {
        var files=new Dictionary<string,HistorySession>(StringComparer.OrdinalIgnoreCase);
        foreach(var folder in folders.Distinct(StringComparer.OrdinalIgnoreCase))
            try {foreach(var path in Directory.EnumerateFiles(folder,"*.nml"))files[path]=new(path,Path.GetFileNameWithoutExtension(path),File.GetLastWriteTimeUtc(path));}
            catch(IOException){}catch(UnauthorizedAccessException){}
        var unique=new List<HistorySession>();var hashes=new HashSet<string>(StringComparer.Ordinal);
        foreach(var session in files.Values.OrderByDescending(s=>s.ModifiedUtc))
            try {using var input=File.OpenRead(session.Path);if(hashes.Add(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(input))))unique.Add(session);}
            catch(IOException){}catch(UnauthorizedAccessException){}
        return unique;
    }
    private static double? Seconds(string? text)=>double.TryParse(text,NumberStyles.Float,CultureInfo.InvariantCulture,out var n)&&double.IsFinite(n)&&n>=0?n:null;
    public static IReadOnlyList<HistorySessionTrack> Read(HistorySession session)
    {
        var entries=new TraktorNmlReader().ReadCollection(session.Path).GroupBy(e=>e.Path.ToCombinedKey(),StringComparer.OrdinalIgnoreCase).ToDictionary(g=>g.Key,g=>g.First(),StringComparer.OrdinalIgnoreCase);
        var xml=XDocument.Load(session.Path);var playlist=xml.Descendants("PLAYLIST").FirstOrDefault();
        var keys=playlist?.Elements("ENTRY").Select(e=>(Key:(string?)e.Element("PRIMARYKEY")?.Attribute("KEY"),Played:Seconds((string?)e.Element("EXTENDEDDATA")?.Attribute("DURATION")))).ToArray()??entries.Keys.Select(k=>(Key:(string?)k,Played:(double?)null)).ToArray();
        var durations=new Dictionary<string,double?>(StringComparer.OrdinalIgnoreCase);
        foreach(var e in xml.Root?.Element("COLLECTION")?.Elements("ENTRY")??[])
            if(e.Element("LOCATION") is {} l)durations[TraktorPath.FromLocation((string?)l.Attribute("VOLUME")??"",(string?)l.Attribute("DIR")??"",(string?)l.Attribute("FILE")??"").ToCombinedKey()]=Seconds((string?)e.Element("INFO")?.Attribute("PLAYTIME"));
        var result=new List<HistorySessionTrack>();
        foreach(var item in keys)
        {
            if(item.Key==null)continue;entries.TryGetValue(item.Key,out var e);var p=e?.Path??TraktorPath.FromCombinedKey(item.Key);if(p==null)continue;
            var path=Path.Combine(new[]{p.Volume.EndsWith(':')?p.Volume+Path.DirectorySeparatorChar:p.Volume}.Concat(p.DirSegments).Append(p.FileName).ToArray());
            var track=new Track {Artist=e?.Artist,Title=e?.Title??Path.GetFileNameWithoutExtension(p.FileName),GenreRaw=e?.Genre,Bpm=e?.Bpm,MusicalKey=e?.MusicalKey,Format=Path.GetExtension(path).TrimStart('.'),DurationMs=(int)Math.Clamp((durations.GetValueOrDefault(item.Key)??0)*1000,0,int.MaxValue)};
            track.Id=-Math.Max(1,BitConverter.ToInt64(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(path.ToUpperInvariant())))&long.MaxValue);
            result.Add(new(session.Name,path,track,item.Played));
        }
        return result;
    }
    public static IReadOnlyList<HistoryMergeRow> Merge(IReadOnlyList<IReadOnlyList<HistorySessionTrack>> sessions,bool match,bool dedup,SetPlanningOptions options)
    {
        var remaining=sessions.Where(s=>s.Count>0).ToList();var result=new List<HistoryMergeRow>();var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while(remaining.Count>0)
        {
            var next=match&&result.Count>0?remaining.OrderBy(s=>ClubSetPlanner.Assess(result[^1].Source.Track,s[0].Track,options).Cost).First():remaining[0];remaining.Remove(next);
            foreach(var source in next)
            {
                if(!seen.Add(source.FullPath)&&dedup)continue;var issues=new List<string>();
                if(source.PlayedSeconds is <30)issues.Add("Kurz angespielt (<30 s); prüfen");
                if(source.Track.Bpm==null||string.IsNullOrWhiteSpace(source.Track.MusicalKey))issues.Add("BPM/Key unvollständig");
                if(result.Count>0){var fit=ClubSetPlanner.Assess(result[^1].Source.Track,source.Track,options);if(!fit.Allowed||fit.Cost>=10)issues.Add(fit.Explanation);}
                result.Add(new(source,string.Join(" · ",issues)));
            }
        }
        return result;
    }
}
