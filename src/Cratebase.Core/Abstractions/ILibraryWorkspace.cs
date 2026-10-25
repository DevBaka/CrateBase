using Cratebase.Core.Models;

namespace Cratebase.Core.Abstractions;

public interface ILibraryQuery
{
    Task<IReadOnlyList<Track>> QueryAsync(TrackQuery query, CancellationToken ct = default);
    Task<TrackTotals> TotalsAsync(TrackQuery query, CancellationToken ct = default);
    Task SetClubReadyAsync(IReadOnlyCollection<long> ids, bool value, CancellationToken ct = default);
    Task SetFlagAsync(IReadOnlyCollection<long> ids,string field,bool value,CancellationToken ct=default);
}

public enum CopyCollision { Skip, KeepBoth }
public sealed record TransferProgress(int Completed, int Total, string Message);
public interface ITargetActions
{
    Task AddAsync(LibraryTarget target, IReadOnlyList<Track> tracks, CopyCollision collision, IProgress<TransferProgress>? progress, CancellationToken ct);
}
