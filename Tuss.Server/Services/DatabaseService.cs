using Microsoft.Data.Sqlite;

namespace Tuss.Server.Services;

public class DatabaseService
{
    private readonly string _connectionString;

    public DatabaseService(string databasePath)
    {
        _connectionString = $"Data Source={databasePath}";
    }

    /// <summary>
    /// Skapar tabellerna om de inte redan finns.
    /// Anropas vid serveruppstart.
    /// </summary>
    public void Initialize()
    {
        using var connection = CreateConnection();

        var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS Files (
                Name      TEXT NOT NULL PRIMARY KEY,
                Content   TEXT NOT NULL DEFAULT '',
                Created   TEXT NOT NULL,
                Changed   TEXT NOT NULL,
                IsFile    INTEGER NOT NULL DEFAULT 1,
                Bytes     INTEGER NOT NULL DEFAULT 0,
                Extension TEXT NOT NULL DEFAULT ''
            );
            """;
        command.ExecuteNonQuery();
    }

    /// <summary>
    /// Skapar och returnerar en öppen SQLite-anslutning.
    /// Den som anropar ansvarar för att dispose:a den (using).
    /// </summary>
    public SqliteConnection CreateConnection()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }
}

