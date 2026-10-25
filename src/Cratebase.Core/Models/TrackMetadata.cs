using System.Globalization;
namespace Cratebase.Core.Models;

public enum MetadataKind { Text,Number,Date,Boolean }
public enum MetadataOperator { Contains,NotContains,Equals,NotEquals,GreaterThan,GreaterOrEqual,LessThan,LessOrEqual,IsEmpty,IsNotEmpty }
public sealed record MetadataField(string Key,string Label,string Binding,MetadataKind Kind=MetadataKind.Text,double Width=130,bool DefaultVisible=false)
{ public override string ToString()=>Label; }
public sealed record MetadataFilter(string Field,MetadataOperator Operator,string Value="");

public static class TrackMetadata
{
    public static IReadOnlyList<MetadataField> Fields { get; } = new MetadataField[]
    {
        new("club","Club-tauglich","ClubReady",MetadataKind.Boolean,75,true),
        new("extended","Extended","Extended",MetadataKind.Boolean,85,true),
        new("intro","Intro","Intro",MetadataKind.Boolean,65,true),
        new("trash","Trash","Trash",MetadataKind.Boolean,70,true),
        new("title","Titel","DisplayTitle",Width:260,DefaultVisible:true), new("artist","Artist","DisplayArtist",Width:180,DefaultVisible:true),
        new("album","Album","Album"),new("albumartist","Album-Artist","AlbumArtist"),new("tracknumber","Track-Nr.","TrackNumber",MetadataKind.Number),
        new("bpm","BPM","Bpm",MetadataKind.Number,85,true),new("key","Key","MusicalKey",Width:80,DefaultVisible:true),
        new("duration","Dauer (Sekunden im Filter)","DisplayDuration",MetadataKind.Number,100,true),
        new("format","Format","DisplayFormat",Width:85,DefaultVisible:true),new("bitrate","Bitrate (kbps)","DisplayBitrate",MetadataKind.Number,110,true),
        new("samplerate","Samplerate (Hz)","SampleRateHz",MetadataKind.Number),new("channels","Kanäle","Channels",MetadataKind.Number),
        new("filesize","Dateigröße (MB)","FileSizeMb",MetadataKind.Number),new("genre","Genre","GenreRaw",DefaultVisible:true),new("subgenre","Subgenre","SubGenre"),
        new("year","Jahr","Year",MetadataKind.Number),new("rating","Bewertung (0–5)","Rating",MetadataKind.Number),new("playcount","Wiedergaben","PlayCount",MetadataKind.Number),
        new("comment","Kommentar","Comment",Width:180,DefaultVisible:true),new("comment2","Kommentar 2","Comment2"),new("color","Farbe","ColorTag"),
        new("lastplayed","Zuletzt gespielt","LastPlayedAtUtc",MetadataKind.Date,170),new("added","Hinzugefügt","DateAddedUtc",MetadataKind.Date,170),
        new("modified","Metadaten geändert","DateModifiedUtc",MetadataKind.Date,170),new("filemodified","Datei geändert","FileModifiedAtUtc",MetadataKind.Date,170),
        new("availability","Dateistatus","FileAvailability",Width:180),
        new("missing","Nicht erreichbar seit","MissingSinceUtc",MetadataKind.Date,170),new("path","Dateipfad","RelativePath",Width:270)
    };
    public static MetadataField Get(string key)=>Fields.FirstOrDefault(f=>f.Key==key)??throw new ArgumentException("Unbekanntes Metadatenfeld: "+key);
    public static IReadOnlyList<MetadataOperator> Operators(MetadataKind kind)=>kind switch
    {
        MetadataKind.Text=>[MetadataOperator.Contains,MetadataOperator.NotContains,MetadataOperator.Equals,MetadataOperator.NotEquals,MetadataOperator.IsEmpty,MetadataOperator.IsNotEmpty],
        MetadataKind.Boolean=>[MetadataOperator.Equals,MetadataOperator.NotEquals],
        _=>[MetadataOperator.Equals,MetadataOperator.NotEquals,MetadataOperator.GreaterThan,MetadataOperator.GreaterOrEqual,MetadataOperator.LessThan,MetadataOperator.LessOrEqual,MetadataOperator.IsEmpty,MetadataOperator.IsNotEmpty]
    };
    public static string Label(MetadataOperator op)=>op switch { MetadataOperator.Contains=>"enthält",MetadataOperator.NotContains=>"enthält nicht",MetadataOperator.Equals=>"ist gleich",MetadataOperator.NotEquals=>"ist ungleich",MetadataOperator.GreaterThan=>"größer als / nach",MetadataOperator.GreaterOrEqual=>"mindestens / ab",MetadataOperator.LessThan=>"kleiner als / vor",MetadataOperator.LessOrEqual=>"höchstens / bis",MetadataOperator.IsEmpty=>"ist leer",_=>"ist nicht leer" };
    public static object Parse(MetadataField field,string value)
    {
        if(field.Kind==MetadataKind.Text) return value;
        if(field.Kind==MetadataKind.Boolean) return value.Trim().ToLowerInvariant() switch { "1" or "true" or "ja"=>1,"0" or "false" or "nein"=>0,_=>throw new ArgumentException(field.Label+": bitte Ja oder Nein eingeben.") };
        if(field.Kind==MetadataKind.Date)
        {
            if(DateTime.TryParse(value,CultureInfo.CurrentCulture,DateTimeStyles.None,out var date)||DateTime.TryParse(value,CultureInfo.InvariantCulture,DateTimeStyles.None,out date)) return date.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture);
            throw new ArgumentException(field.Label+": bitte ein Datum eingeben, z. B. 2026-10-23.");
        }
        if(double.TryParse(value.Replace(',','.'),NumberStyles.Float,CultureInfo.InvariantCulture,out var number)&&double.IsFinite(number)) return number;
        throw new ArgumentException(field.Label+": bitte eine gültige Zahl eingeben.");
    }
    public static void Validate(MetadataFilter filter)
    {
        var field=Get(filter.Field);
        if(!Operators(field.Kind).Contains(filter.Operator)) throw new ArgumentException("Dieser Vergleich ist für "+field.Label+" nicht verfügbar.");
        if(filter.Operator is not (MetadataOperator.IsEmpty or MetadataOperator.IsNotEmpty)) _=Parse(field,filter.Value);
    }
}
