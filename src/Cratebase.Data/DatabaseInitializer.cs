namespace Cratebase.Data;

public static class DatabaseInitializer
{
    /// <summary>Erstellt das Schema, falls es noch nicht existiert. Idempotent, sicher bei jedem App-Start aufzurufen.</summary>
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

        // Alle CREATE TABLE-Statements in Schema.Sql sind additiv (IF NOT EXISTS) – neue Tabellen
        // für eine bestehende DB kosten also nur diesen Aufruf, keine echte Migration. Die
        // schema_version-Zeile wird trotzdem gepflegt (insert-or-update), als Ansatzpunkt für den
        // Tag, an dem eine Änderung mal *nicht* additiv ist und echte Migrationslogik braucht.
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
}
