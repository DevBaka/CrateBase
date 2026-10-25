namespace Cratebase.Core.Abstractions;
public interface IWorkspaceStateStore
{
    Task<string?> ReadAsync(string key, CancellationToken ct = default);
    Task WriteAsync(string key, string json, CancellationToken ct = default);
}
