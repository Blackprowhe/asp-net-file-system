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
    /// Skapar/migrerar tabellerna vid serveruppstart.
    /// </summary>
    public void Initialize()
    {
        using var connection = CreateConnection();
        var cmd = connection.CreateCommand();

        // Files-tabell – CurrentVersion håller reda på vilken version som är aktiv
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS Files (
                Name           TEXT NOT NULL PRIMARY KEY,
                DiskPath       TEXT NOT NULL,
                Created        TEXT NOT NULL,
                Changed        TEXT NOT NULL,
                IsFile         INTEGER NOT NULL DEFAULT 1,
                Bytes          INTEGER NOT NULL DEFAULT 0,
                Extension      TEXT NOT NULL DEFAULT '',
                CurrentVersion INTEGER NOT NULL DEFAULT 1
            );

            -- Versions-tabell: en rad per sparad version av en fil
            CREATE TABLE IF NOT EXISTS Versions (
                Id        INTEGER PRIMARY KEY AUTOINCREMENT,
                FileName  TEXT NOT NULL REFERENCES Files(Name) ON DELETE CASCADE,
                Version   INTEGER NOT NULL,
                DiskPath  TEXT NOT NULL,
                CreatedAt TEXT NOT NULL,
                Bytes     INTEGER NOT NULL DEFAULT 0,
                UNIQUE(FileName, Version)
            );

            -- Migration: lägg till CurrentVersion om den inte finns ännu
            -- (körs ofarligt om kolumnen redan existerar, tack vare SQLite-feltolerans)
            """;
        cmd.ExecuteNonQuery();

        // Lägg till CurrentVersion på befintlig databas om kolumnen saknas
        try
        {
            var migrate = connection.CreateCommand();
            migrate.CommandText = "ALTER TABLE Files ADD COLUMN CurrentVersion INTEGER NOT NULL DEFAULT 1;";
            migrate.ExecuteNonQuery();
        }
        catch (SqliteException)
        {
            // Kolumnen finns redan – inget att göra
        }
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
