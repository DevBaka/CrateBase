using Cratebase.Core.Models;

namespace Cratebase.Import;

public static class TraktorHistoryReader
{
    public static IReadOnlyList<HistoryAudioTrack> Read(string path)
    {
        var reader=new TraktorNmlReader();var collection=reader.ReadCollection(path).ToDictionary(e=>e.Path.ToCombinedKey(),StringComparer.OrdinalIgnoreCase);
        var tree=reader.ReadPlaylistTree(path);var keys=new List<string>();
        void Walk(TraktorPlaylistNode node)
        {if(node.NodeType==TraktorNodeType.Playlist&&node.Name.Contains("history",StringComparison.OrdinalIgnoreCase))keys.AddRange(node.TrackKeys.Select(k=>k.ToCombinedKey()));foreach(var child in node.Children)Walk(child);}
        if(tree!=null)Walk(tree);if(keys.Count==0)keys.AddRange(collection.Keys);
        var result=new List<HistoryAudioTrack>();
        foreach(var key in keys.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            collection.TryGetValue(key,out var entry);
            var p=entry?.Path??TraktorPath.FromCombinedKey(key);if(p==null)continue;
            var root=p.Volume.EndsWith(':')?p.Volume+Path.DirectorySeparatorChar:p.Volume;
            var full=Path.Combine(new[]{root}.Concat(p.DirSegments).Append(p.FileName).ToArray());
            var artist=entry?.Artist??"";var title=entry?.Title??Path.GetFileNameWithoutExtension(p.FileName);
            if(string.IsNullOrWhiteSpace(artist)&&title.IndexOf(" - ",StringComparison.Ordinal) is >0 and var split){artist=title[..split];title=title[(split+3)..];}
            if(!string.IsNullOrWhiteSpace(artist)&&title.StartsWith(artist+" - ",StringComparison.OrdinalIgnoreCase))title=title[(artist.Length+3)..];
            result.Add(new(result.Count+1,full,artist,title));
        }
        if(result.Count==0)throw new InvalidDataException("Die History enthält keine auflösbaren Tracks. Bitte NML mit Collection-Einträgen exportieren.");
        return result;
    }
}
