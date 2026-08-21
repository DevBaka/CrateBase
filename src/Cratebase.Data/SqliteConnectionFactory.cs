using Microsoft.Data.Sqlite;

namespace Cratebase.Data;

/// <summary>
/// Öffnet pro Aufruf eine eigene, kurzlebige Connection statt eine geteilte Connection
/// über Threads hinweg zu benutzen (Microsoft.Data.Sqlite-Connections sind nicht
/// thread-safe). WAL-Modus erlaubt parallele Leser neben einem Schreiber (Scanner
/// schreibt, UI liest) ohne sich gegenseitig zu blockieren – Kernvoraussetzung dafür,
/// dass Scans im Hintergrund laufen können, während die Bibliothek durchsuchbar bleibt.
/// </summary>
public sealed class SqliteConnectionFactory
{
    private readonly string _connectionString;

    public SqliteConnectionFactory(string databasePath)
    {
        DatabasePath = databasePath;
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Cache = SqliteCacheMode.Shared,
        }.ToString();
    }

    public string DatabasePath { get; }

    public SqliteConnection CreateConnection()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var pragmaCommand = connection.CreateCommand();
        pragmaCommand.CommandText = "PRAGMA journal_mode = WAL; PRAGMA foreign_keys = ON; PRAGMA busy_timeout = 5000;";
        pragmaCommand.ExecuteNonQuery();

        return connection;
    }
}
