using Microsoft.Data.Sqlite;

namespace Cratebase.Data;

public static class DatabaseInitializer
{
    /// <summary>Erstellt das Schema, falls es noch nicht existiert, und wendet ausstehende Spalten-
    /// Migrationen an. Idempotent, sicher bei jedem App-Start aufzurufen.</summary>
    public static void EnsureCreated(SqliteConnectionFactory connectionFactory)
    {
        using var connection = connectionFactory.CreateConnection();
        using var transaction = connection.BeginTransaction();

        using (var createCommand = connection.CreateCommand())
        {
            createCommand.Transaction = transaction;
            createCommand.CommandText = Schema.Sql;
            createCommand.ExecuteNonQuery();
        }

        ApplyTrackColumnMigrations(connection, transaction);

        // schema_version wird gepflegt (insert-or-update), primär informativ - die eigentliche
        // Idempotenz läuft über "IF NOT EXISTS" (Tabellen) bzw. PRAGMA table_info (Spalten), nicht
        // über einen Vergleich mit dieser Nummer.
        using (var upsertVersionCommand = connection.CreateCommand())
        {
            upsertVersionCommand.Transaction = transaction;
            upsertVersionCommand.CommandText = """
                DELETE FROM schema_version;
                INSERT INTO schema_version (version) VALUES (@version);
                """;
            upsertVersionCommand.Parameters.AddWithValue("@version", Schema.CurrentVersion);
            upsertVersionCommand.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    /// <summary>
    /// SQLite kennt kein "ALTER TABLE ... ADD COLUMN IF NOT EXISTS" - ohne diese Prüfung würde
    /// jeder zweite Start (Spalte existiert schon) mit einem SQL-Fehler abbrechen. PRAGMA
    /// table_info listet die tatsächlich vorhandenen Spalten, unabhängig davon, ob die DB frisch
    /// (Spalten kamen direkt aus dem CREATE TABLE) oder eine ältere, bereits befüllte DB ist
    /// (Spalten fehlen noch, ALTER TABLE muss laufen).
    /// </summary>
    private static void ApplyTrackColumnMigrations(SqliteConnection connection, SqliteTransaction transaction)
    {
        var existingColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using (var pragmaCommand = connection.CreateCommand())
        {
            pragmaCommand.Transaction = transaction;
            pragmaCommand.CommandText = "PRAGMA table_info(track);";
            using var reader = pragmaCommand.ExecuteReader();
            var nameOrdinal = reader.GetOrdinal("name");
            while (reader.Read())
            {
                existingColumns.Add(reader.GetString(nameOrdinal));
            }
        }

        foreach (var (column, ddl) in Schema.TrackColumnMigrations)
        {
            if (existingColumns.Contains(column))
            {
                continue;
            }

            using var alterCommand = connection.CreateCommand();
            alterCommand.Transaction = transaction;
            alterCommand.CommandText = ddl;
            alterCommand.ExecuteNonQuery();
        }
    }
}
