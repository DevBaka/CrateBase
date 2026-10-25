using Cratebase.Core.Models;
namespace Cratebase.Core.Abstractions;

public sealed record RemotePlaybackState(long? TrackId,bool Playing,long PositionMs,long DurationMs,double Volume,IReadOnlyList<long> Queue,int Index);
public sealed record RemotePlaybackCommand(string Action,long? PositionMs=null,double? Volume=null,long? ExpectedTrackId=null);
public sealed record RemotePlaybackLoad(long[] Ids,int Index=0,long PositionMs=0,bool Playing=true);
public interface IRemotePlayback
{
    Task<RemotePlaybackState> ReadAsync(CancellationToken ct);
    Task LoadAsync(IReadOnlyList<Track> queue,int index,long positionMs,bool playing,CancellationToken ct);
    Task CommandAsync(RemotePlaybackCommand command,CancellationToken ct);
}
