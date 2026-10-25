using System.Text.Json;
using Cratebase.Core.Abstractions;
using Cratebase.Core.Models;

namespace Cratebase.Import;

/// <summary>Indexes AUDIO_ID values once per source revision; never modifies the NML or its caches.</summary>
public sealed class TraktorAnalysisLookup(IWorkspaceStateStore store,ILibraryRootRepository roots) : ITraktorAnalysisLookup
{
    private readonly SemaphoreSlim _gate=new(1);
    private (string Path,long Length,long Mtime)? _revision;
    private Dictionary<string,string> _audioIds=new(StringComparer.OrdinalIgnoreCase);

    public async Task<TraktorAnalysisReference?> FindAsync(Track track,CancellationToken ct=default)
    {
        var source=JsonSerializer.Deserialize<string>(await store.ReadAsync("traktor-nml-source",ct)??"null");
        if(source==null) return null;
        var info=new FileInfo(source); if(!info.Exists) return null;
        var revision=(Path.GetFullPath(source),info.Length,info.LastWriteTimeUtc.Ticks);
        var root=(await roots.GetAllAsync(ct)).FirstOrDefault(r=>r.Id==track.LibraryRootId);
        if(root==null) return null;
        var segments=track.RelativePath.Replace('\\','/').Split('/',StringSplitOptions.RemoveEmptyEntries);
        if(segments.Length==0) return null;
        var volume=root.TraktorVolume??Path.GetPathRoot(root.Path)?.TrimEnd('\\','/')??"";
        var prefix=root.TraktorVolume==null
            ? root.Path[(Path.GetPathRoot(root.Path)?.Length??0)..].Replace('\\','/').Split('/',StringSplitOptions.RemoveEmptyEntries)
            : (root.TraktorDirPrefix??"").Split("/:",StringSplitOptions.RemoveEmptyEntries);
        var key=new TraktorPath(volume,prefix.Concat(segments[..^1]).ToArray(),segments[^1]).ToCombinedKey();
        await _gate.WaitAsync(ct);
        try
        {
            if(_revision!=revision)
            {
                var index=await Task.Run(()=>
                {
                    var result=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
                    foreach(var entry in new TraktorNmlReader().ReadCollection(source))
                    {
                        ct.ThrowIfCancellationRequested();
                        if(!string.IsNullOrWhiteSpace(entry.AudioId)) result[entry.Path.ToCombinedKey()]=entry.AudioId;
                    }
                    return result;
                },ct);
                _audioIds=index; _revision=revision;
            }
            return _audioIds.TryGetValue(key,out var audioId)?new(Path.GetDirectoryName(source)!,audioId):null;
        }
        finally { _gate.Release(); }
    }
}
