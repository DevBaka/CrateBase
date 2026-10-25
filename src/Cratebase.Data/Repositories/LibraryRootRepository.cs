using Cratebase.Core.Abstractions;
using Cratebase.Core.Models;
using Dapper;

namespace Cratebase.Data.Repositories;

public sealed class LibraryRootRepository(SqliteConnectionFactory connectionFactory) : ILibraryRootRepository
{
    private const string SelectColumns = """
        id AS Id, path AS Path, label AS Label,
        watch_enabled AS WatchEnabled, last_scanned_at AS LastScannedAtUtc,
        traktor_volume AS TraktorVolume, traktor_dir_prefix AS TraktorDirPrefix
        """;

    public async Task<IReadOnlyList<LibraryRoot>> GetAllAsync(CancellationToken ct = default)
    {
        using var connection = connectionFactory.CreateConnection();
        var command = new CommandDefinition(
            $"SELECT {SelectColumns} FROM library_root ORDER BY label;",
            cancellationToken: ct);
        var rows = await connection.QueryAsync<LibraryRoot>(command);
        return rows.ToList();
    }

    public async Task<LibraryRoot> AddOrGetAsync(string path, string label, CancellationToken ct = default)
    {
        using var connection = connectionFactory.CreateConnection();

        var existing = await connection.QuerySingleOrDefaultAsync<LibraryRoot>(
            new CommandDefinition($"SELECT {SelectColumns} FROM library_root WHERE path = @path;", new { path }, cancellationToken: ct));
        if (existing is not null)
        {
            return existing;
        }

        var newId = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            """
            INSERT INTO library_root (path, label, watch_enabled, last_scanned_at)
            VALUES (@path, @label, 1, NULL)
            RETURNING id;
            """,
            new { path, label },
            cancellationToken: ct));

        return new LibraryRoot { Id = newId, Path = path, Label = label, WatchEnabled = true };
    }

    public async Task UpdateLastScannedAsync(long id, DateTime whenUtc, CancellationToken ct = default)
    {
        using var connection = connectionFactory.CreateConnection();
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE library_root SET last_scanned_at = @whenUtc WHERE id = @id;",
            new { id, whenUtc = SqliteDateTime.ToText(whenUtc) },
            cancellationToken: ct));
    }

    public async Task RelocateAsync(long id,string localPath,string relativePrefix="",CancellationToken ct=default)
    {
        using var connection=connectionFactory.CreateConnection();
        localPath=Path.GetFullPath(localPath).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
        relativePrefix=relativePrefix.Replace('\\','/').Trim('/');
        if(relativePrefix.Split('/').Any(s=>s is ".." or ".")) throw new ArgumentException("Bitte einen relativen Unterordner ohne .. angeben.");
        if(string.IsNullOrEmpty(relativePrefix))
        {
            await connection.ExecuteAsync(new CommandDefinition("UPDATE library_root SET path=@localPath WHERE id=@id",new { id,localPath },cancellationToken:ct)); return;
        }
        using var transaction=connection.BeginTransaction();
        var source=await connection.QuerySingleAsync<LibraryRoot>(new CommandDefinition($"SELECT {SelectColumns} FROM library_root WHERE id=@id",new { id },transaction,cancellationToken:ct));
        var mappingPrefix=string.Join("/:",new[]{source.TraktorDirPrefix??""}.Concat(relativePrefix.Split('/')).Where(s=>s.Length>0));
        var newId=await connection.ExecuteScalarAsync<long>(new CommandDefinition("INSERT INTO library_root(path,label,traktor_volume,traktor_dir_prefix) VALUES(@localPath,@label,@volume,@prefix) RETURNING id",new { localPath,label=Path.GetFileName(localPath.TrimEnd(Path.DirectorySeparatorChar)),volume=source.TraktorVolume,prefix=mappingPrefix },transaction,cancellationToken:ct));
        var prefix=relativePrefix+"/";
        var escaped=prefix.Replace("~","~~").Replace("%","~%").Replace("_","~_");
        await connection.ExecuteAsync(new CommandDefinition("UPDATE track SET library_root_id=@newId, relative_path=substr(relative_path,@start), missing_since=NULL WHERE library_root_id=@id AND relative_path LIKE @escaped || '%' ESCAPE '~'",new { id,newId,start=prefix.Length+1,escaped },transaction,cancellationToken:ct));
        transaction.Commit();
    }

    public async Task SetTraktorMappingAsync(long id, string volume, string dirPrefix, CancellationToken ct = default)
    {
        using var connection = connectionFactory.CreateConnection();
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE library_root SET traktor_volume = @volume, traktor_dir_prefix = @dirPrefix WHERE id = @id;",
            new { id, volume, dirPrefix },
            cancellationToken: ct));
    }
}
