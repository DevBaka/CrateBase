namespace Cratebase.Core.Abstractions;

/// <summary>Monotonic change token covering writes from all connections to the shared library.</summary>
public interface ILibraryRevision : IDisposable
{
    Task<long> ReadAsync(CancellationToken ct = default);
}
