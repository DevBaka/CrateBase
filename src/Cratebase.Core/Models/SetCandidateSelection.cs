using System.Text.RegularExpressions;

namespace Cratebase.Core.Models;

public static class SetCandidateSelection
{
    public static IReadOnlyList<Track> FocusGenreFolders(IReadOnlyList<Track> source,string theme,CancellationToken ct=default)
    {
        var genres=new[]{"hardtekk","tekk","hard techno","dark techno","techno","hardstyle","hardcore","hands up","house","trance","drum and bass","dubstep","goth","ebm","aggrotech","darkwave","synthpop","pop","rock","schlager"};
        var terms=genres.Where(g=>Regex.IsMatch(theme,@"(?<!\p{L})"+Regex.Escape(g)+@"(?!\p{L})",RegexOptions.IgnoreCase)
            &&!Regex.IsMatch(theme,@"(?:kein\w*|ohne|no)\s+"+Regex.Escape(g)+@"(?!\p{L})",RegexOptions.IgnoreCase)).ToArray();
        if(terms.Length==0)return source;
        static string Normalize(string value)=>Regex.Replace(value.ToLowerInvariant(),@"[^\p{L}\p{N}]","");
        var keys=terms.Select(Normalize).ToArray();var matches=new List<Track>();
        foreach(var track in source)
        {
            ct.ThrowIfCancellationRequested();
            var folder=Normalize(Path.GetDirectoryName(track.RelativePath.Replace('/',Path.DirectorySeparatorChar))??"");
            var genre=Normalize($"{track.GenreRaw} {track.SubGenre}");
            if(keys.Any(k=>folder.Contains(k)||genre.Contains(k)))matches.Add(track);
        }
        return matches.Count==0?source:matches;
    }
    // Examine the entire source before limiting expensive model calls. Never take just
    // the first alphabetically sorted page; even a match at the end must be eligible.
    public static IReadOnlyList<Track> ForAi(IReadOnlyList<Track> source,string theme,int maximum=250,CancellationToken ct=default)
    {
        if(maximum<1)throw new ArgumentOutOfRangeException(nameof(maximum));
        var terms=Regex.Matches(theme.ToLowerInvariant(),@"[\p{L}\p{N}]{3,}").Select(m=>m.Value).Distinct().ToArray();
        var scored=new List<(Track Track,int Score,double Tie)>();var ids=new HashSet<long>();
        foreach(var track in source)
        {
            ct.ThrowIfCancellationRequested();
            if(track.Trash||track.MissingSinceUtc!=null||track.DurationMs<=0||!ids.Add(track.Id))continue;
            var genre=$"{track.GenreRaw} {track.SubGenre}".ToLowerInvariant();
            var text=$"{track.Artist} {track.Title} {track.Album} {track.Comment} {track.Comment2} {track.RelativePath}".ToLowerInvariant();
            var score=terms.Sum(term=>(genre.Contains(term)?4:0)+(text.Contains(term)?1:0));
            scored.Add((track,score,Random.Shared.NextDouble()));
        }
        return scored.OrderByDescending(x=>x.Score).ThenBy(x=>x.Tie).Take(maximum).Select(x=>x.Track).ToArray();
    }
}
