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

        using (var checkCommand = connection.CreateCommand())
        {
            checkCommand.Transaction = transaction;
            checkCommand.CommandText = "SELECT COUNT(*) FROM schema_version;";
            var hasVersion = (long)checkCommand.ExecuteScalar()! > 0;
            if (!hasVersion)
            {
                using var insertCommand = connection.CreateCommand();
                insertCommand.Transaction = transaction;
                insertCommand.CommandText = "INSERT INTO schema_version (version) VALUES (@version);";
                insertCommand.Parameters.AddWithValue("@version", Schema.CurrentVersion);
                insertCommand.ExecuteNonQuery();
            }
        }

        transaction.Commit();
    }
}
