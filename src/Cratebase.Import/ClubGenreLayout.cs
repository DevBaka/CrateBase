using System.Text;
using System.Text.RegularExpressions;

namespace Cratebase.Import;

public sealed record ClubGenreSuggestion(string Folder,string? SourceFolder,string? Warning);

/// <summary>Explicit aliases for the user's library; no inferred mood, quality or BPM classification.</summary>
public static class ClubGenreLayout
{
    private static string Normalize(string value)=>Regex.Replace(value.ToLowerInvariant(),"[^a-z0-9]","");
    private static readonly Dictionary<string,string> Aliases=Build();
    private static Dictionary<string,string> Build()
    {
        var map=new Dictionary<string,string>();
        void Add(string folder,params string[] names){foreach(var name in names)map[Normalize(name)]=folder;}
        Add("Techno/Allgemein","Techno","Tekkno");
        Add("Techno/Hard Techno","Hard Techno","Hardtechno","Deep Hardtechno");Add("Techno/Schranz","Schranz");
        Add("Techno/Peak Time - Driving","Techno (Peak Time / Driving)","Peak Time Techno","Driving Techno","Techno - PeakTime");
        Add("Techno/Raw - Deep - Hypnotic","Techno (Raw / Deep / Hypnotic)");Add("Techno/Raw Techno","Raw Techno");Add("Techno/Deep Techno","Deep Techno");Add("Techno/Hypnotic Techno","Hypnotic Techno","Forward Hypnotic Techno");
        Add("Techno/Acid Techno","Acid Techno");Add("Techno/Dark Techno","Dark Techno");Add("Techno/Industrial Techno","Industrial Techno","Hard Industrial Techno");
        Add("Techno/Minimal Techno","Minimal Techno","Deep Minimal Techno","Dark Minimal Techno");Add("Techno/Melodic Techno","Melodic Techno");
        Add("Techno/Groovy Techno","Groovy Techno","Groovy");Add("Techno/Hyper Techno","Hypertechno");
        Add("House/Allgemein","House");foreach(var style in new[]{"Deep House","Tech House","Bass House","Progressive House","Melodic House","Afro House","Organic House","Funky House","Disco House","Electro House","Future House","G-House","Hard House","Speed House"})Add("House/"+style,style);
        Add("House/Indie Dance","Indie Dance");Add("House/Nu Disco","Nu Disco");Add("House/Big Room","BigRoom","Big Room");
        Add("Drum & Bass/Allgemein","DnB","Drum & Bass","Drum and Bass");
        foreach(var style in new[]{"Liquid","Neurofunk","Jump Up","Jungle","Techstep","Darkstep","Halftime","Rollers","Drumstep"})Add("Drum & Bass/"+style,style,"Drum & Bass "+style,"DnB "+style);
        Add("Dubstep/Allgemein","Dubstep");Add("Dubstep/Deathstep","Deathstep");Add("Dubstep/Metalstep","Metalstep");
        Add("Psy-Trance/Allgemein","Psy-Trance","Psytrance","OnPsytrance");Add("Psy-Trance/Dark - Forest","Psy-Trance Dark & Forest","Dark and Forest","Dark Psytrance","Forest Psytrance");
        Add("Psy-Trance/Full-On","FullOn","Psy-Trance Full-On");Add("Psy-Trance/Progressive","Progressive Psytrance","Psy-Trance Progressive");Add("Psy-Trance/Goa","Goa","Goa Trance");
        Add("Psy-Trance/Hi-Tech - Psycore","Psy-Trance Psycore & Hi-Tech","Psycore and Hightech","Hi-Tech","Psycore");
        Add("Trance/Allgemein","Trance","Trance (Main Floor)");foreach(var style in new[]{"Hard Trance","Progressive Trance","Tech Trance","Neo Trance"})Add("Trance/"+style,style,"Trance (Main Floor) "+style);
        Add("Hardstyle/Allgemein","Hardstyle","Hard Dance / Hardcore / Neo Rave Hardstyle");Add("Hardstyle/Rawstyle","Rawstyle","Raw Hardstyle");Add("Hardstyle/Classics","Classic Hardstyle","Hardstyle Classics");
        Add("Hardcore/Allgemein","Hardcore");foreach(var style in new[]{"Frenchcore","Uptempo","Speedcore","Terror","Gabber","Doomcore"})Add("Hardcore/"+style,style);
        Add("Hardcore/UK - Happy Hardcore","UK Hardcore","Happy Hardcore","Hard Dance / Hardcore / Neo Rave UK / Happy Hardcore");Add("Hardcore/Industrial Hardcore","Industrial Hardcore");
        Add("Hard Dance/Neo Rave","Neo Rave","Hard Dance / Hardcore / Neo Rave Neo Rave");Add("Hard Dance/Hard Bounce","Hard Bounce");
        Add("HardTekk/Allgemein","HardTekk","Hard Tekk");Add("Hands Up/Allgemein","HandsUp","Hands Up");Add("Jumpstyle/Allgemein","Jumpstyle");
        Add("Industrial & EBM/EBM","EBM");Add("Industrial & EBM/Aggrotech","Aggrotech");Add("Industrial & EBM/Dark Electro","Dark Electro");Add("Industrial & EBM/Futurepop","Futurepop","Future Pop");
        Add("Gothic & Wave/Darkwave","Dark Wave","Darkwave");Add("Gothic & Wave/Post-Punk","Post Punk","Dark Post-Punk");Add("Gothic & Wave/Gothic Rock","Gothic Rock");Add("Gothic & Wave/Coldwave","Coldwave");
        Add("Hip-Hop/Allgemein","Hip Hop","Hip-Hop","HipHop","Rap");Add("Hip-Hop/Deutschrap","Deutschrap");Add("RnB/Allgemein","RnB","R&B");
        Add("Pop & Dance/Pop","Pop","Top 40");Add("Pop & Dance/Dance","Dance","Dance / Pop");Add("Pop & Dance/K-Pop","K-Pop");
        Add("DJ Tools/Acapellas","Acapellas","Acapella");Add("DJ Tools/Instrumentals","Instrumentals","Instrumental");Add("DJ Tools/Intros","Intro Songs");Add("DJ Tools/BPM Transitions","bpm transition tracks");
        return map;
    }
    public static ClubGenreSuggestion Suggest(string? genre,string source)
    {
        string? sourceFolder=null;
        foreach(var segment in (Path.GetDirectoryName(source)??"").Replace('\\','/').Split('/').AsEnumerable().Reverse())
            if(Aliases.TryGetValue(Normalize(segment),out sourceFolder))break;
        string folder;string? warning=null;
        if(string.IsNullOrWhiteSpace(genre))
        {folder=sourceFolder??"00 Genre prüfen/Ohne Genre";warning=sourceFolder==null?"Kein Genre-Tag und kein eindeutiger Genre-Ordner.":"Genre-Tag fehlt; Vorschlag aus dem Quellordner.";}
        else if(!Aliases.TryGetValue(Normalize(genre),out folder!))
        {folder="00 Genre prüfen/"+SafeName(genre,75);warning="Unbekannter oder mehrdeutiger Genre-Tag; Ziel bitte prüfen.";}
        if(sourceFolder!=null&&folder!=sourceFolder&&!folder.EndsWith("/Allgemein")&&!sourceFolder.EndsWith("/Allgemein"))
            warning=(warning==null?"":warning+" ")+"Genre-Tag und Quellordner unterscheiden sich ("+sourceFolder+").";
        if(sourceFolder!=null&&(!sourceFolder.EndsWith("/Allgemein")||folder.StartsWith("00 Genre prüfen/"))&&(!sourceFolder.StartsWith("DJ Tools/")||folder.StartsWith("00 Genre prüfen/")||folder.StartsWith("DJ Tools/")))folder=sourceFolder;
        return new(folder,sourceFolder,warning);
    }
    public static string SafeName(string value,int max=140)
    {
        var clean=new StringBuilder();foreach(var c in value.Normalize(NormalizationForm.FormC))clean.Append(c<32||"<>:\"/\\|?*".Contains(c)?'_':c);
        var name=Regex.Replace(clean.ToString(),@"\s+"," ").Trim().TrimEnd('.',' ');
        if(name.Length==0)name="Unbenannt";
        if(Regex.IsMatch(name,@"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])($|\.)",RegexOptions.IgnoreCase))name="_"+name;
        if(name.Length>max) {name=name[..max].TrimEnd('.',' ');if(name.Length>0&&char.IsHighSurrogate(name[^1]))name=name[..^1];}
        return name;
    }
    public static string ValidateFolder(string folder)
    {
        var parts=folder.Replace('\\','/').Split('/');
        if(parts.Length==0||parts.Any(p=>p.Length==0||p is "." or ".."||p!=SafeName(p,100)))throw new ArgumentException("Ungültiger relativer Zielordner: "+folder);
        return string.Join('/',parts);
    }
}
