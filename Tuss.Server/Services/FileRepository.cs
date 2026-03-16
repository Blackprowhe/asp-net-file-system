using Microsoft.Data.Sqlite;
using Tuss.Server.Models;

namespace Tuss.Server.Services;

public class FileRepository
{
    private readonly DatabaseService _db;

    public FileRepository(DatabaseService db)
    {
        _db = db;
    }

    // Läser en rad från reader och mappar den till ett StoredFile-objekt
    private static StoredFile MapRow(SqliteDataReader reader) => new()
    {
        Name      = reader.GetString(0),
        Content   = reader.GetString(1),
        Created   = reader.GetString(2),
        Changed   = reader.GetString(3),
        IsFile    = reader.GetBoolean(4),
        Bytes     = reader.GetInt64(5),
        Extension = reader.GetString(6),
    };

    // Hämtar alla filer utan innehåll
    public List<StoredFile> GetAll()
    {
        using var connection = _db.CreateConnection();
        var command = connection.CreateCommand();
        command.CommandText = "SELECT Name, Content, Created, Changed, IsFile, Bytes, Extension FROM Files";

        var files = new List<StoredFile>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
            files.Add(MapRow(reader));

        return files;
    }

    // Hämtar en specifik fil, eller null om den inte finns
    public StoredFile? GetByName(string name)
    {
        using var connection = _db.CreateConnection();
        var command = connection.CreateCommand();
        command.CommandText = "SELECT Name, Content, Created, Changed, IsFile, Bytes, Extension FROM Files WHERE Name = $name";
        command.Parameters.AddWithValue("$name", name);

        using var reader = command.ExecuteReader();
        return reader.Read() ? MapRow(reader) : null;
    }

    // Försöker skapa en fil – returnerar false om den redan finns
    public bool TryCreate(string name, string content)
    {
        var now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
        var extension = Path.GetExtension(name);
        var bytes = content.Length;

        using var connection = _db.CreateConnection();
        var command = connection.CreateCommand();
        command.CommandText = """
            INSERT OR IGNORE INTO Files (Name, Content, Created, Changed, IsFile, Bytes, Extension)
            VALUES ($name, $content, $now, $now, 1, $bytes, $extension);
            SELECT changes();
            """;
        command.Parameters.AddWithValue("$name", name);
        command.Parameters.AddWithValue("$content", content);
        command.Parameters.AddWithValue("$now", now);
        command.Parameters.AddWithValue("$bytes", bytes);
        command.Parameters.AddWithValue("$extension", extension);

        var rowsChanged = (long)(command.ExecuteScalar() ?? 0L);
        return rowsChanged > 0;
    }
}

