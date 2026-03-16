using Microsoft.Data.Sqlite;
using Tuss.Server.Models;

namespace Tuss.Server.Services;

public class FileRepository
{
    private readonly DatabaseService _db;
    private readonly string _storageRoot;

    public FileRepository(DatabaseService db, IWebHostEnvironment env)
    {
        _db = db;
        // Alla filer sparas under wwwroot/storage på disk
        _storageRoot = Path.Combine(env.ContentRootPath, "storage");
        Directory.CreateDirectory(_storageRoot);
    }

    // Sätter metadata-headers på responsen för en given fil
    public static void ApplyHeaders(HttpContext context, StoredFile file)
    {
        context.Response.Headers["X-Created-At"] = file.Created;
        context.Response.Headers["X-Changed-At"] = file.Changed;
        context.Response.Headers["X-Type"]       = "file";
        context.Response.Headers["X-Bytes"]      = file.Bytes.ToString();
        context.Response.Headers["X-Extension"]  = file.Extension;
    }

    // Läser en rad från reader och mappar den till ett StoredFile-objekt
    private static StoredFile MapRow(SqliteDataReader reader) => new()
    {
        Name      = reader.GetString(0),
        DiskPath  = reader.GetString(1),
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
        command.CommandText = "SELECT Name, DiskPath, Created, Changed, IsFile, Bytes, Extension FROM Files";

        var files = new List<StoredFile>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
            files.Add(MapRow(reader));

        return files;
    }

    // Hämtar metadata för en specifik fil, eller null om den inte finns
    public StoredFile? GetByName(string name)
    {
        using var connection = _db.CreateConnection();
        var command = connection.CreateCommand();
        command.CommandText = "SELECT Name, DiskPath, Created, Changed, IsFile, Bytes, Extension FROM Files WHERE Name = $name";
        command.Parameters.AddWithValue("$name", name);

        using var reader = command.ExecuteReader();
        return reader.Read() ? MapRow(reader) : null;
    }

    // Öppnar en läsström till filens innehåll på disk
    public Stream OpenRead(StoredFile file)
    {
        return File.OpenRead(file.DiskPath);
    }

    // Försöker skapa en fil – returnerar false om den redan finns
    public async Task<bool> TryCreateAsync(string name, Stream body)
    {
        var now       = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
        var extension = Path.GetExtension(name);
        var diskPath  = GetDiskPath(name);

        // Kontrollera om filen redan finns i databasen innan vi skriver till disk
        if (GetByName(name) is not null)
            return false;

        Directory.CreateDirectory(Path.GetDirectoryName(diskPath)!);
        await using (var fs = File.Create(diskPath))
            await body.CopyToAsync(fs);

        var bytes = new FileInfo(diskPath).Length;

        using var connection = _db.CreateConnection();
        var command = connection.CreateCommand();
        command.CommandText = """
            INSERT OR IGNORE INTO Files (Name, DiskPath, Created, Changed, IsFile, Bytes, Extension)
            VALUES ($name, $diskPath, $now, $now, 1, $bytes, $extension);
            SELECT changes();
            """;
        command.Parameters.AddWithValue("$name", name);
        command.Parameters.AddWithValue("$diskPath", diskPath);
        command.Parameters.AddWithValue("$now", now);
        command.Parameters.AddWithValue("$bytes", bytes);
        command.Parameters.AddWithValue("$extension", extension);

        var rowsChanged = (long)(command.ExecuteScalar() ?? 0L);

        // Om raden inte lades in (race condition) – ta bort diskfilen igen
        if (rowsChanged == 0)
        {
            File.Delete(diskPath);
            return false;
        }

        return true;
    }

    // Skapar filen om den inte finns, ersätter innehållet om den redan finns
    public async Task UpsertAsync(string name, Stream body)
    {
        var now       = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
        var extension = Path.GetExtension(name);
        var diskPath  = GetDiskPath(name);

        Directory.CreateDirectory(Path.GetDirectoryName(diskPath)!);
        await using (var fs = File.Create(diskPath))
            await body.CopyToAsync(fs);

        var bytes = new FileInfo(diskPath).Length;

        using var connection = _db.CreateConnection();
        var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO Files (Name, DiskPath, Created, Changed, IsFile, Bytes, Extension)
            VALUES ($name, $diskPath, $now, $now, 1, $bytes, $extension)
            ON CONFLICT(Name) DO UPDATE SET
                DiskPath  = $diskPath,
                Changed   = $now,
                Bytes     = $bytes;
            """;
        command.Parameters.AddWithValue("$name", name);
        command.Parameters.AddWithValue("$diskPath", diskPath);
        command.Parameters.AddWithValue("$now", now);
        command.Parameters.AddWithValue("$bytes", bytes);
        command.Parameters.AddWithValue("$extension", extension);

        command.ExecuteNonQuery();
    }

    // Tar bort en fil – gör ingenting om den inte finns
    public void Delete(string name)
    {
        using var connection = _db.CreateConnection();
        var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM Files WHERE Name = $name";
        command.Parameters.AddWithValue("$name", name);
        command.ExecuteNonQuery();
    }

    // Bygger en unik diskväg för en fil baserat på dess namn
    private string GetDiskPath(string name)
    {
        // Ersätt / med _ för att hålla allt platt på disk
        var safeName = name.Replace("/", "_").Replace("\\", "_");
        return Path.Combine(_storageRoot, safeName);
    }
}
