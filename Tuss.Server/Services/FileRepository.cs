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
        context.Response.Headers["X-Created-At"] = file.Created;
        context.Response.Headers["X-Changed-At"] = file.Changed;
        context.Response.Headers["X-Type"]       = file.IsFile ? "file" : "folder";
        context.Response.Headers["X-Bytes"]      = file.Bytes.ToString();
        context.Response.Headers["X-Extension"]  = file.Extension;
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
        CurrentVersion = r.IsDBNull(7) ? null : r.GetInt32(7),
    };

    private const string SelectColumns =
        "Name, DiskPath, Created, Changed, IsFile, Bytes, Extension, CurrentVersion";

    // ─── Queries ─────────────────────────────────────────────────────────────

    /// <summary>Hämtar alla poster (filer + mappar).</summary>
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

    /// <summary>Hämtar direkta barn av en mapp (ett steg ner).</summary>
    public List<StoredFile> GetDirectChildren(string folderName)
    {
        // Barn av "mapp 1" har namn som börjar med "mapp 1/"
        var prefix = folderName + "/";
        using var con = _db.CreateConnection();
        var cmd = con.CreateCommand();
        cmd.CommandText = $"SELECT {SelectColumns} FROM Files WHERE Name LIKE $prefix ORDER BY IsFile, Name";
        cmd.Parameters.AddWithValue("$prefix", prefix + "%");
        var list = new List<StoredFile>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var item = MapRow(r);
            // Filtrera: bara direkta barn (max 1 nivå ner)
            var relativeName = item.Name[(prefix.Length)..];
            var slashIndex = relativeName.IndexOf('/');
            // Det är ett direkt barn om det inte finns fler / i relativnamnet
            // (eller bara ett trailing / för mappar)
            if (slashIndex == -1 || (slashIndex == relativeName.Length - 1))
                list.Add(item);
        }
        return list;
    }

    /// <summary>Hämtar alla poster rekursivt under en mapp.</summary>
    public List<StoredFile> GetAllInFolder(string folderName)
    {
        var prefix = folderName + "/";
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
    /// Skapar en mapp. Returnerar false om den redan finns.
    /// Mapp-namn lagras utan trailing slash, t.ex. "mapp 1" eller "mapp 1/sub".
    /// </summary>
    public bool CreateFolder(string name)
    {
        if (GetByName(name) is not null)
            return false;

        var now = Now();
        EnsureParentFolders(name, now);

        using var con = _db.CreateConnection();
        var cmd = con.CreateCommand();
        cmd.CommandText = """
            INSERT OR IGNORE INTO Files (Name, DiskPath, Created, Changed, IsFile, Bytes, Extension, CurrentVersion)
            VALUES ($name, '', $now, $now, 0, 0, '', 0);
            SELECT changes();
            """;
        cmd.Parameters.AddWithValue("$name", name);
        cmd.Parameters.AddWithValue("$now", now);
        return (long)(cmd.ExecuteScalar() ?? 0L) > 0;
    }

    /// <summary>Skapar en mapp om den inte finns, gör inget om den finns (PUT-beteende).</summary>
    public void UpsertFolder(string name)
    {
        var now = Now();
        EnsureParentFolders(name, now);

        using var con = _db.CreateConnection();
        var cmd = con.CreateCommand();
        cmd.CommandText = """
            INSERT INTO Files (Name, DiskPath, Created, Changed, IsFile, Bytes, Extension, CurrentVersion)
            VALUES ($name, '', $now, $now, 0, 0, '', 0)
            ON CONFLICT(Name) DO UPDATE SET Changed = $now;
            """;
        cmd.Parameters.AddWithValue("$name", name);
        cmd.Parameters.AddWithValue("$now", now);
        cmd.ExecuteNonQuery();
    }

    /// <summary>Tar bort en mapp (eller fil) och allt under den.</summary>
    public void DeleteEntry(string name)
    {
        var entry = GetByName(name);
        if (entry is null) return;

        if (entry.IsFile)
        {
            // Radera alla versioner av filen från disk
            var versions = GetVersions(name);
            foreach (var v in versions)
                if (File.Exists(v.DiskPath)) File.Delete(v.DiskPath);
        }
        else
        {
            // Radera alla filer i mappen från disk
            var children = GetAllInFolder(name);
            foreach (var child in children.Where(c => c.IsFile))
            {
                var versions = GetVersions(child.Name);
                foreach (var v in versions)
                    if (File.Exists(v.DiskPath)) File.Delete(v.DiskPath);
            }
        }

        using var con = _db.CreateConnection();
        var cmd = con.CreateCommand();
        cmd.CommandText = "DELETE FROM Files WHERE Name = $name OR Name LIKE $prefix";
        cmd.Parameters.AddWithValue("$name", name);
        cmd.Parameters.AddWithValue("$prefix", name + "/%");
        cmd.ExecuteNonQuery();
    }

    // ─── Filer ───────────────────────────────────────────────────────────────

    /// <summary>Skapar en ny fil. Returnerar false om den redan finns.</summary>
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
            cmdFile.Parameters.AddWithValue("$name",      name);
            cmdFile.Parameters.AddWithValue("$diskPath",  diskPath);
            cmdFile.Parameters.AddWithValue("$now",       now);
            cmdFile.Parameters.AddWithValue("$bytes",     bytes);
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

    /// <summary>Skapar en ny version av filen (eller skapar filen om den inte finns).</summary>
    public async Task UpsertAsync(string name, Stream body)
    {
        var now       = Now();
        var extension = Path.GetExtension(name);

        var existing    = GetByName(name);
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
        cmdFile.Parameters.AddWithValue("$name",      name);
        cmdFile.Parameters.AddWithValue("$diskPath",  diskPath);
        cmdFile.Parameters.AddWithValue("$now",       now);
        cmdFile.Parameters.AddWithValue("$bytes",     bytes);
        cmdFile.Parameters.AddWithValue("$extension", extension);
        cmdFile.Parameters.AddWithValue("$version",   nextVersion);
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

    // ─── Versioner ────────────────────────────────────────────────────────────

    public List<FileVersion> GetVersions(string fileName)
    {
        using var con = _db.CreateConnection();
        var cmd = con.CreateCommand();
        cmd.CommandText = """
            SELECT Id, FileName, Version, DiskPath, CreatedAt, Bytes
            FROM Versions WHERE FileName = $name ORDER BY Version DESC
            """;
        cmd.Parameters.AddWithValue("$name", fileName);
        var list = new List<FileVersion>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new FileVersion
            {
                Id = r.GetInt64(0), FileName = r.GetString(1), Version = r.GetInt32(2),
                DiskPath = r.GetString(3), CreatedAt = r.GetString(4), Bytes = r.GetInt64(5),
            });
        return list;
    }

    public FileVersion? GetVersion(string fileName, int version)
    {
        using var con = _db.CreateConnection();
        var cmd = con.CreateCommand();
        cmd.CommandText = """
            SELECT Id, FileName, Version, DiskPath, CreatedAt, Bytes
            FROM Versions WHERE FileName = $name AND Version = $version
            """;
        cmd.Parameters.AddWithValue("$name",    fileName);
        cmd.Parameters.AddWithValue("$version", version);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;
        return new FileVersion
        {
            Id = r.GetInt64(0), FileName = r.GetString(1), Version = r.GetInt32(2),
            DiskPath = r.GetString(3), CreatedAt = r.GetString(4), Bytes = r.GetInt64(5),
        };
    }

    public async Task RestoreVersionAsync(string fileName, int version)
    {
        var target = GetVersion(fileName, version)
            ?? throw new FileNotFoundException($"Version {version} av '{fileName}' hittades inte.");
        await using var stream = File.OpenRead(target.DiskPath);
        await UpsertAsync(fileName, stream);
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private static string Now() => DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");

    private string GetVersionDiskPath(string name, int version)
    {
        var safeName = name.Replace("/", "_").Replace("\\", "_");
        return Path.Combine(_storageRoot, $"{safeName}_v{version}");
    }

    /// <summary>
    /// Skapar föräldersmappar i databasen.
    /// "a/b/c/fil.txt" → skapar "a" och "a/b" och "a/b/c"
    /// </summary>
    private void EnsureParentFolders(string path, string now)
    {
        var parts = path.Split('/');
        // Allt utom sista segmentet är föräldrar
        for (var i = 1; i < parts.Length; i++)
        {
            var folderName = string.Join("/", parts[..i]);
            using var con = _db.CreateConnection();
            var cmd = con.CreateCommand();
            cmd.CommandText = """
                INSERT OR IGNORE INTO Files (Name, DiskPath, Created, Changed, IsFile, Bytes, Extension, CurrentVersion)
                VALUES ($name, '', $now, $now, 0, 0, '', 0);
                """;
            cmd.Parameters.AddWithValue("$name", folderName);
            cmd.Parameters.AddWithValue("$now",  now);
            cmd.ExecuteNonQuery();
        }
    }

    /// <summary>
    /// Beräknar total storlek i bytes av allt i en mapp.
    /// </summary>
    public long GetFolderSize(string folderName)
    {
        using var con = _db.CreateConnection();
        var cmd = con.CreateCommand();
        cmd.CommandText = "SELECT COALESCE(SUM(Bytes), 0) FROM Files WHERE Name LIKE $prefix AND IsFile = 1";
        cmd.Parameters.AddWithValue("$prefix", folderName + "/%");
        return (long)(cmd.ExecuteScalar() ?? 0L);
    }
}
