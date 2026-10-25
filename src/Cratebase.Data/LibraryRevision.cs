using Cratebase.Core.Abstractions;
using Microsoft.Data.Sqlite;

namespace Cratebase.Data;

/// <summary>PRAGMA data_version must be read on the same connection to observe external commits.</summary>
public sealed class LibraryRevision : ILibraryRevision
{
    private readonly SqliteConnection _connection;
    private readonly SemaphoreSlim _gate=new(1,1);
    public LibraryRevision(SqliteConnectionFactory factory) { _connection=factory.CreateConnection(); }
    public async Task<long> ReadAsync(CancellationToken ct=default)
    {
        await _gate.WaitAsync(ct);
        try { using var command=_connection.CreateCommand();command.CommandText="PRAGMA data_version";return Convert.ToInt64(await command.ExecuteScalarAsync(ct)); }
        finally {_gate.Release();}
    }
    public void Dispose()=>_connection.Dispose();
}
