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
        _storageRoot = Path.Combine(env.ContentRootPath, "storage");
        Directory.CreateDirectory(_storageRoot);
    }

    // ─── Headers ─────────────────────────────────────────────────────────────

    public static void ApplyHeaders(HttpContext context, StoredFile file)
    {
        context.Response.Headers["X-Created-At"]      = file.Created;
        context.Response.Headers["X-Changed-At"]      = file.Changed;
        context.Response.Headers["X-Type"]            = file.IsFile ? "file" : "folder";
        context.Response.Headers["X-Bytes"]           = file.Bytes.ToString();
        context.Response.Headers["X-Extension"]       = file.Extension;
        context.Response.Headers["X-Current-Version"] = (file.CurrentVersion ?? 1).ToString();
    }

    // ─── Mapping ─────────────────────────────────────────────────────────────

    private static StoredFile MapRow(SqliteDataReader r) => new()
    {
        Name           = r.GetString(0),
        DiskPath       = r.GetString(1),
        Created        = r.GetString(2),
        Changed        = r.GetString(3),
        IsFile         = r.GetBoolean(4),
        Bytes          = r.GetInt64(5),
        Extension      = r.GetString(6),
        CurrentVersion = r.IsDBNull(7) ? 1 : r.GetInt32(7),
    };

    private const string SelectColumns =
        "Name, DiskPath, Created, Changed, IsFile, Bytes, Extension, CurrentVersion";

    // ─── Queries ─────────────────────────────────────────────────────────────

    /// <summary>Hämtar alla poster (filer + mappar) utan innehåll.</summary>
    public List<StoredFile> GetAll()
    {
        using var con = _db.CreateConnection();
        var cmd = con.CreateCommand();
        cmd.CommandText = $"SELECT {SelectColumns} FROM Files ORDER BY IsFile, Name";
        var list = new List<StoredFile>();
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(MapRow(r));
        return list;
    }

    /// <summary>Hämtar alla poster under en viss mapp-prefix.</summary>
    public List<StoredFile> GetByFolder(string folderPath)
    {
        // Normalisera: ta bort ledande/avslutande slash
        var prefix = folderPath.Trim('/') + "/";
        using var con = _db.CreateConnection();
        var cmd = con.CreateCommand();
        cmd.CommandText = $"SELECT {SelectColumns} FROM Files WHERE Name LIKE $prefix ORDER BY IsFile, Name";
        cmd.Parameters.AddWithValue("$prefix", prefix + "%");
        var list = new List<StoredFile>();
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(MapRow(r));
        return list;
    }

    public StoredFile? GetByName(string name)
    {
        using var con = _db.CreateConnection();
        var cmd = con.CreateCommand();
        cmd.CommandText = $"SELECT {SelectColumns} FROM Files WHERE Name = $name";
        cmd.Parameters.AddWithValue("$name", name);
        using var r = cmd.ExecuteReader();
        return r.Read() ? MapRow(r) : null;
    }

    public Stream OpenRead(StoredFile file) => File.OpenRead(file.DiskPath);

    // ─── Mappar ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Skapar en mapp (och alla föräldersmappar) om de inte redan finns.
    /// Returnerar false om mappen redan existerar.
    /// </summary>
    public bool CreateFolder(string path)
    {
        // Normalisera: ta bort ledande slash, säkra upp avslutande slash
        path = path.Trim('/') + "/";

        if (GetByName(path) is not null)
            return false;

        var now = Now();

        // Skapa alla föräldersmappar rekursivt om de saknas
        EnsureParentFolders(path, now);

        using var con = _db.CreateConnection();
        var cmd = con.CreateCommand();
        cmd.CommandText = """
            INSERT OR IGNORE INTO Files (Name, DiskPath, Created, Changed, IsFile, Bytes, Extension, CurrentVersion)
            VALUES ($name, '', $now, $now, 0, 0, '', 0);
            SELECT changes();
            """;
        cmd.Parameters.AddWithValue("$name", path);
        cmd.Parameters.AddWithValue("$now", now);
        var changed = (long)(cmd.ExecuteScalar() ?? 0L);
        return changed > 0;
    }

    /// <summary>
    /// Tar bort en mapp och allt som finns inuti den (filer + undermappar).
    /// </summary>
    public void DeleteFolder(string path)
    {
        path = path.Trim('/') + "/";

        // Hämta alla filer i mappen för att kunna radera diskfilerna
        var children = GetByFolder(path.TrimEnd('/'));
        foreach (var child in children.Where(c => c.IsFile))
            DeleteFileFromDisk(child);

        using var con = _db.CreateConnection();
        var cmd = con.CreateCommand();
        // Ta bort mappen och allt innehåll med LIKE-prefix
        cmd.CommandText = """
            DELETE FROM Files WHERE Name = $path OR Name LIKE $prefix;
            """;
        cmd.Parameters.AddWithValue("$path", path);
        cmd.Parameters.AddWithValue("$prefix", path + "%");
        cmd.ExecuteNonQuery();
    }

    // ─── Filer ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Skapar en ny fil. Returnerar false om filen redan finns.
    /// Skapar version 1 i Versions-tabellen.
    /// </summary>
    public async Task<bool> TryCreateAsync(string name, Stream body)
    {
        if (GetByName(name) is not null)
            return false;

        var now       = Now();
        var extension = Path.GetExtension(name);
        var diskPath  = GetVersionDiskPath(name, 1);

        EnsureParentFolders(name, now);
        Directory.CreateDirectory(Path.GetDirectoryName(diskPath)!);
        await using (var fs = File.Create(diskPath))
            await body.CopyToAsync(fs);

        var bytes = new FileInfo(diskPath).Length;

        using var con = _db.CreateConnection();
        using var tx  = con.BeginTransaction();
        try
        {
            var cmdFile = con.CreateCommand();
            cmdFile.Transaction = tx;
            cmdFile.CommandText = """
                INSERT OR IGNORE INTO Files (Name, DiskPath, Created, Changed, IsFile, Bytes, Extension, CurrentVersion)
                VALUES ($name, $diskPath, $now, $now, 1, $bytes, $extension, 1);
                SELECT changes();
                """;
            cmdFile.Parameters.AddWithValue("$name",     name);
            cmdFile.Parameters.AddWithValue("$diskPath", diskPath);
            cmdFile.Parameters.AddWithValue("$now",      now);
            cmdFile.Parameters.AddWithValue("$bytes",    bytes);
            cmdFile.Parameters.AddWithValue("$extension", extension);
            var rows = (long)(cmdFile.ExecuteScalar() ?? 0L);

            if (rows == 0)
            {
                tx.Rollback();
                File.Delete(diskPath);
                return false;
            }

            var cmdVer = con.CreateCommand();
            cmdVer.Transaction = tx;
            cmdVer.CommandText = """
                INSERT INTO Versions (FileName, Version, DiskPath, CreatedAt, Bytes)
                VALUES ($name, 1, $diskPath, $now, $bytes);
                """;
            cmdVer.Parameters.AddWithValue("$name",     name);
            cmdVer.Parameters.AddWithValue("$diskPath", diskPath);
            cmdVer.Parameters.AddWithValue("$now",      now);
            cmdVer.Parameters.AddWithValue("$bytes",    bytes);
            cmdVer.ExecuteNonQuery();

            tx.Commit();
            return true;
        }
        catch
        {
            tx.Rollback();
            if (File.Exists(diskPath)) File.Delete(diskPath);
            throw;
        }
    }

    /// <summary>
    /// Skapar en ny version av filen (eller skapar filen om den inte finns).
    /// Sparar gammal version, skriver ny version till disk.
    /// </summary>
    public async Task UpsertAsync(string name, Stream body)
    {
        var now       = Now();
        var extension = Path.GetExtension(name);

        var existing   = GetByName(name);
        var nextVersion = (existing?.CurrentVersion ?? 0) + 1;
        var diskPath    = GetVersionDiskPath(name, nextVersion);

        EnsureParentFolders(name, now);
        Directory.CreateDirectory(Path.GetDirectoryName(diskPath)!);
        await using (var fs = File.Create(diskPath))
            await body.CopyToAsync(fs);

        var bytes = new FileInfo(diskPath).Length;

        using var con = _db.CreateConnection();
        using var tx  = con.BeginTransaction();

        var cmdFile = con.CreateCommand();
        cmdFile.Transaction = tx;
        cmdFile.CommandText = """
            INSERT INTO Files (Name, DiskPath, Created, Changed, IsFile, Bytes, Extension, CurrentVersion)
            VALUES ($name, $diskPath, $now, $now, 1, $bytes, $extension, $version)
            ON CONFLICT(Name) DO UPDATE SET
                DiskPath       = $diskPath,
                Changed        = $now,
                Bytes          = $bytes,
                CurrentVersion = $version;
            """;
        cmdFile.Parameters.AddWithValue("$name",     name);
        cmdFile.Parameters.AddWithValue("$diskPath", diskPath);
        cmdFile.Parameters.AddWithValue("$now",      now);
        cmdFile.Parameters.AddWithValue("$bytes",    bytes);
        cmdFile.Parameters.AddWithValue("$extension", extension);
        cmdFile.Parameters.AddWithValue("$version",  nextVersion);
        cmdFile.ExecuteNonQuery();

        var cmdVer = con.CreateCommand();
        cmdVer.Transaction = tx;
        cmdVer.CommandText = """
            INSERT OR IGNORE INTO Versions (FileName, Version, DiskPath, CreatedAt, Bytes)
            VALUES ($name, $version, $diskPath, $now, $bytes);
            """;
        cmdVer.Parameters.AddWithValue("$name",     name);
        cmdVer.Parameters.AddWithValue("$version",  nextVersion);
        cmdVer.Parameters.AddWithValue("$diskPath", diskPath);
        cmdVer.Parameters.AddWithValue("$now",      now);
        cmdVer.Parameters.AddWithValue("$bytes",    bytes);
        cmdVer.ExecuteNonQuery();

        tx.Commit();
    }

    /// <summary>
    /// Tar bort en fil och alla dess versioner.
    /// </summary>
    public void Delete(string name)
    {
        var versions = GetVersions(name);
        foreach (var v in versions)
            if (File.Exists(v.DiskPath)) File.Delete(v.DiskPath);

        using var con = _db.CreateConnection();
        var cmd = con.CreateCommand();
        // Versions raderas automatiskt via ON DELETE CASCADE
        cmd.CommandText = "DELETE FROM Files WHERE Name = $name";
        cmd.Parameters.AddWithValue("$name", name);
        cmd.ExecuteNonQuery();
    }

    // ─── Versioner ────────────────────────────────────────────────────────────

    /// <summary>Hämtar alla versioner för en fil, nyast först.</summary>
    public List<FileVersion> GetVersions(string fileName)
    {
        using var con = _db.CreateConnection();
        var cmd = con.CreateCommand();
        cmd.CommandText = """
            SELECT Id, FileName, Version, DiskPath, CreatedAt, Bytes
            FROM Versions
            WHERE FileName = $name
            ORDER BY Version DESC
            """;
        cmd.Parameters.AddWithValue("$name", fileName);
        var list = new List<FileVersion>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new FileVersion
            {
                Id        = r.GetInt64(0),
                FileName  = r.GetString(1),
                Version   = r.GetInt32(2),
                DiskPath  = r.GetString(3),
                CreatedAt = r.GetString(4),
                Bytes     = r.GetInt64(5),
            });
        return list;
    }

    /// <summary>Hämtar en specifik version av en fil.</summary>
    public FileVersion? GetVersion(string fileName, int version)
    {
        using var con = _db.CreateConnection();
        var cmd = con.CreateCommand();
        cmd.CommandText = """
            SELECT Id, FileName, Version, DiskPath, CreatedAt, Bytes
            FROM Versions
            WHERE FileName = $name AND Version = $version
            """;
        cmd.Parameters.AddWithValue("$name",    fileName);
        cmd.Parameters.AddWithValue("$version", version);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;
        return new FileVersion
        {
            Id        = r.GetInt64(0),
            FileName  = r.GetString(1),
            Version   = r.GetInt32(2),
            DiskPath  = r.GetString(3),
            CreatedAt = r.GetString(4),
            Bytes     = r.GetInt64(5),
        };
    }

    /// <summary>
    /// Återställer en fil till en tidigare version genom att skapa en ny version
    /// med samma innehåll som den gamla (bevarar versionshistoriken).
    /// </summary>
    public async Task RestoreVersionAsync(string fileName, int version)
    {
        var target = GetVersion(fileName, version)
            ?? throw new FileNotFoundException($"Version {version} av '{fileName}' hittades inte.");

        await using var stream = File.OpenRead(target.DiskPath);
        await UpsertAsync(fileName, stream);
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private static string Now() => DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");

    /// <summary>
    /// Bygger en unik diskväg för en specifik version av en fil.
    /// Exempel: storage/projekt_README.md_v3
    /// </summary>
    private string GetVersionDiskPath(string name, int version)
    {
        var safeName = name.Replace("/", "_").Replace("\\", "_");
        return Path.Combine(_storageRoot, $"{safeName}_v{version}");
    }

    /// <summary>
    /// Skapar alla föräldersmappar i databasen om de inte redan finns.
    /// Exempel: "projekt/sub/fil.txt" → skapar "projekt/" och "projekt/sub/"
    /// </summary>
    private void EnsureParentFolders(string path, string now)
    {
        var parts = path.TrimEnd('/').Split('/');
        for (var i = 1; i < parts.Length; i++)
        {
            var folderPath = string.Join("/", parts[..i]) + "/";
            using var con = _db.CreateConnection();
            var cmd = con.CreateCommand();
            cmd.CommandText = """
                INSERT OR IGNORE INTO Files (Name, DiskPath, Created, Changed, IsFile, Bytes, Extension, CurrentVersion)
                VALUES ($name, '', $now, $now, 0, 0, '', 0);
                """;
            cmd.Parameters.AddWithValue("$name", folderPath);
            cmd.Parameters.AddWithValue("$now",  now);
            cmd.ExecuteNonQuery();
        }
    }

    private void DeleteFileFromDisk(StoredFile file)
    {
        if (file.IsFile && File.Exists(file.DiskPath))
            File.Delete(file.DiskPath);
    }
}
