using System.Text.Json;
using System.Text.RegularExpressions;

namespace Cratebase.Analysis;

public sealed record SetArtworkIdea(string[] Titles,string Headline,string Subtitle,string Accent,string Layout)
{
    public static SetArtworkIdea Parse(string json)
    {
        using var doc=JsonDocument.Parse(json);var root=doc.RootElement;
        string Text(string key,int length)=>root.TryGetProperty(key,out var v)&&v.ValueKind==JsonValueKind.String?Clean(v.GetString()??"",length):"";
        var titles=root.GetProperty("titles").EnumerateArray().Where(v=>v.ValueKind==JsonValueKind.String).Select(v=>Clean(v.GetString()??"",95)).Where(v=>v.Length>0).Distinct().Take(5).ToArray();
        var headline=Text("headline",55);if(titles.Length==0||headline.Length==0)throw new IOException("Die KI hat keine brauchbaren Titel oder Thumbnail-Zeile geliefert.");
        var accent=ReadableAccent(Text("accent",7));
        return new(titles,headline,Text("subtitle",85),accent,Text("layout",10)=="right"?"right":"left");
    }
    public static string ReadableAccent(string color)
    {
        if(!Regex.IsMatch(color,"^#[0-9a-fA-F]{6}$"))return "#36D9E8";
        var r=Convert.ToInt32(color.Substring(1,2),16);var g=Convert.ToInt32(color.Substring(3,2),16);var b=Convert.ToInt32(color.Substring(5,2),16);
        // Accent copy sits on a dark scrim: reject nearly black AI/user choices.
        return Math.Max(r,Math.Max(g,b))<80?"#36D9E8":color;
    }
    private static string Clean(string text,int max)=>string.Concat(text.Where(c=>!char.IsControl(c))).Trim() is var clean&&clean.Length>max?clean[..max]:clean;
}
