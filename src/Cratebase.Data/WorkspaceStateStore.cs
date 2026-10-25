using Cratebase.Core.Abstractions;
using Dapper;
namespace Cratebase.Data;
public sealed class WorkspaceStateStore(SqliteConnectionFactory factory) : IWorkspaceStateStore
{
    public async Task<string?> ReadAsync(string key, CancellationToken ct = default)
    {
        using var connection = factory.CreateConnection();
        return await connection.QuerySingleOrDefaultAsync<string>(new CommandDefinition("SELECT json FROM workspace_state WHERE key=@key", new { key }, cancellationToken: ct));
    }
    public async Task WriteAsync(string key, string json, CancellationToken ct = default)
    {
        using var connection = factory.CreateConnection();
        await connection.ExecuteAsync(new CommandDefinition("INSERT INTO workspace_state(key,json) VALUES(@key,@json) ON CONFLICT(key) DO UPDATE SET json=excluded.json", new { key, json }, cancellationToken: ct));
    }
}
