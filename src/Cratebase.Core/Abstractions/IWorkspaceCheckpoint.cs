namespace Cratebase.Core.Abstractions;

/// <summary>A durable rollback boundary for multi-repository library changes.</summary>
public interface IWorkspaceCheckpoint
{
    Task ExecuteAsync(Func<Task> change, CancellationToken ct = default);
}
