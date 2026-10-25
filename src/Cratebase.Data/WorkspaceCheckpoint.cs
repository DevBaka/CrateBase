using Cratebase.Core.Abstractions;
using Microsoft.Data.Sqlite;

namespace Cratebase.Data;

/// <summary>
/// Repository operations use independent connections. A durable SQLite backup provides
/// rollback across all of them, including after a process interruption. The caller must
/// exclude concurrent editing while the checkpoint is active.
/// </summary>
public sealed class WorkspaceCheckpoint(SqliteConnectionFactory factory) : IWorkspaceCheckpoint
{
    private readonly SemaphoreSlim _gate=new(1);
    public async Task ExecuteAsync(Func<Task> change,CancellationToken ct=default)
    {
        await _gate.WaitAsync(ct);
        var backup=factory.DatabasePath+".sync-rollback.db";
        var marker=factory.DatabasePath+".sync-pending";
        try
        {
            ct.ThrowIfCancellationRequested();
            using(var source=factory.CreateConnection())
            using(var destination=new SqliteConnection(new SqliteConnectionStringBuilder { DataSource=backup,Pooling=false }.ToString()))
            { destination.Open(); source.BackupDatabase(destination); }
            await File.WriteAllTextAsync(marker,"Rollback required until checkpoint is committed.",ct);
            try { await change(); File.Delete(marker); }
            catch { Recover(factory); throw; }
            finally { if(!File.Exists(marker) && File.Exists(backup)) File.Delete(backup); }
        }
        finally { _gate.Release(); }
    }
    public static void Recover(SqliteConnectionFactory factory)
    {
        var marker=factory.DatabasePath+".sync-pending";
        var backup=factory.DatabasePath+".sync-rollback.db";
        if(!File.Exists(marker)) return;
        if(!File.Exists(backup)) throw new IOException("Abgleich wurde unterbrochen; die Wiederherstellungsdatei fehlt: "+backup);
        using(var source=new SqliteConnection(new SqliteConnectionStringBuilder { DataSource=backup,Pooling=false }.ToString()))
        using(var destination=factory.CreateConnection())
        { source.Open(); source.BackupDatabase(destination); }
        File.Delete(marker); File.Delete(backup);
    }
}
