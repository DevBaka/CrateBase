using Cratebase.Core.Models;
using Cratebase.Import;
internal static class HistorySessionChecks
{
    public static void Audit()
    {
        var sessions=HistorySessionCatalog.Discover(HistorySessionCatalog.DefaultFolders());var entries=0;var failures=0;
        foreach(var session in sessions)
            try {entries+=HistorySessionCatalog.Read(session).Count;}
            catch(Exception ex){failures++;Console.WriteLine("History nicht lesbar: "+session.Path+" · "+ex.Message);}
        Console.WriteLine($"History-Leseprüfung: {sessions.Count} Sessions · {entries} Einträge · {failures} Lesefehler. Keine Dateien verändert.");
        if(failures>0)throw new Exception("History-Lesefehler; siehe Ausgabe.");
    }
    public static void Run()
    {
        var directory=Path.Combine(Path.GetTempPath(),"cratebase-history-check-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        try
        {
            var path=Path.Combine(directory,"history.nml");File.WriteAllText(path,"""
                <NML VERSION="19"><COLLECTION ENTRIES="1"><ENTRY TITLE="Test" ARTIST="DJ"><LOCATION VOLUME="M:" DIR="/:Musik/:" FILE="test.flac"/><INFO PLAYTIME="180" GENRE="Techno"/><TEMPO BPM="140"/></ENTRY></COLLECTION><PLAYLISTS><NODE TYPE="FOLDER" NAME="$ROOT"><SUBNODES><NODE TYPE="PLAYLIST" NAME="HISTORY"><PLAYLIST><ENTRY><PRIMARYKEY KEY="M:/:Musik/:test.flac"/><EXTENDEDDATA DURATION="12"/></ENTRY><ENTRY><PRIMARYKEY KEY="M:/:Musik/:test.flac"/><EXTENDEDDATA DURATION="120"/></ENTRY></PLAYLIST></NODE></SUBNODES></NODE></PLAYLISTS></NML>
                """);
            var before=File.ReadAllBytes(path);var session=HistorySessionCatalog.Discover([directory]).Single();var tracks=HistorySessionCatalog.Read(session);
            if(tracks.Count!=2||tracks[0].PlayedSeconds!=12||tracks[0].Track.DurationMs!=180000)throw new Exception("History sequence/timing incorrect");
            var rows=HistorySessionCatalog.Merge([tracks],false,false,new());
            if(rows.Count!=2||!rows[0].Review.Contains("Kurz"))throw new Exception("Audition review missing");
            if(HistorySessionCatalog.Merge([tracks,tracks],true,true,new()).Count!=1)throw new Exception("Deduplication failed");
            HistorySessionTrack Song(string name,double bpm)=>new(name,name,new Track {Id=name[0],DurationMs=180000,Title=name,Bpm=bpm,GenreRaw="Techno",MusicalKey="8A"},90);
            var a=Song("a",130);var b=Song("b",150);var c=Song("c",132);
            var ordered=HistorySessionCatalog.Merge([[a],[b],[c]],true,false,new(60,4,true));
            if(ordered[1].Source!=c||ordered[2].Review.Length==0)throw new Exception("Boundary matching/review failed");
            if(!before.SequenceEqual(File.ReadAllBytes(path)))throw new Exception("History source modified");
            Console.WriteLine("PASS: History discovery, order/repeats, duration, audition flags, optional dedup, session boundary matching and source preservation.");
        }
        finally {Directory.Delete(directory,true);}
    }
}
