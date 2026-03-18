using Microsoft.Data.Sqlite;
using Tuss.Server.Models;

namespace Tuss.Server.Services;

/// <summary>
/// Ansvarar för CRUD mot Files-tabellen och grundläggande filoperationer.
/// Disk-I/O delegeras till <see cref="FileStorageService"/>,
/// versionsrader delegeras till <see cref="VersionRepository"/>.
/// </summary>
public class FileRepository(DatabaseService db, FileStorageService storage, VersionRepository versions)
{
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
        using var con = db.CreateConnection();
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
        using var con = db.CreateConnection();
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
        using var con = db.CreateConnection();
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
        using var con = db.CreateConnection();
        var cmd = con.CreateCommand();
        cmd.CommandText = $"SELECT {SelectColumns} FROM Files WHERE Name = $name";
        cmd.Parameters.AddWithValue("$name", name);
        using var r = cmd.ExecuteReader();
        return r.Read() ? MapRow(r) : null;
    }

    public Stream OpenRead(StoredFile file) => storage.OpenRead(file.DiskPath);

    // ─── Mappar ──────────────────────────────────────────────────────────────

    /// <summary>Skapar en mapp. Returnerar false om den redan finns.</summary>
    public bool CreateFolder(string name)
    {
        if (GetByName(name) is not null)
            return false;

        var now = Now();
        EnsureParentFolders(name, now);

        using var con = db.CreateConnection();
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

    /// <summary>Skapar en mapp om den inte finns, uppdaterar Changed om den finns.</summary>
    public void UpsertFolder(string name)
    {
        var now = Now();
        EnsureParentFolders(name, now);

        using var con = db.CreateConnection();
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

    /// <summary>Tar bort en fil/mapp och allt under den (inklusive versioner på disk).</summary>
    public void DeleteEntry(string name)
    {
        var entry = GetByName(name);
        if (entry is null) return;

        // Radera fysiska filer från disk
        var filesToClean = entry.IsFile
            ? new List<StoredFile> { entry }
            : GetAllInFolder(name).Where(c => c.IsFile).ToList();

        foreach (var file in filesToClean)
        {
            foreach (var v in versions.GetVersions(file.Name))
                storage.DeleteIfExists(v.DiskPath);
        }

        using var con = db.CreateConnection();
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
        var diskPath  = storage.GetVersionDiskPath(name, 1);

        EnsureParentFolders(name, now);
        var bytes = await storage.SaveToDiskAsync(diskPath, body);

        using var con = db.CreateConnection();
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
                storage.DeleteIfExists(diskPath);
                return false;
            }

            versions.InsertVersion(con, tx, name, 1, diskPath, now, bytes);
            tx.Commit();
            return true;
        }
        catch
        {
            tx.Rollback();
            storage.DeleteIfExists(diskPath);
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
        var diskPath    = storage.GetVersionDiskPath(name, nextVersion);

        EnsureParentFolders(name, now);
        var bytes = await storage.SaveToDiskAsync(diskPath, body);

        using var con = db.CreateConnection();
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

        versions.InsertVersion(con, tx, name, nextVersion, diskPath, now, bytes);
        tx.Commit();
    }

    // ─── Storlek ─────────────────────────────────────────────────────────────

    /// <summary>Beräknar total storlek i bytes av allt i en mapp.</summary>
    public long GetFolderSize(string folderName)
    {
        using var con = db.CreateConnection();
        var cmd = con.CreateCommand();
        cmd.CommandText = "SELECT COALESCE(SUM(Bytes), 0) FROM Files WHERE Name LIKE $prefix AND IsFile = 1";
        cmd.Parameters.AddWithValue("$prefix", folderName + "/%");
        return (long)(cmd.ExecuteScalar() ?? 0L);
    }

    // ─── Flytt & Bulk ────────────────────────────────────────────────────────

    public void MoveEntry(string oldName, string newName)
    {
        if (oldName == newName) return;
        var entry = GetByName(oldName);
        if (entry is null) return;

        var now = Now();
        EnsureParentFolders(newName, now);

        using var con = db.CreateConnection();
        using var tx  = con.BeginTransaction();
        try
        {
            var deferFk = con.CreateCommand();
            deferFk.Transaction = tx;
            deferFk.CommandText = "PRAGMA defer_foreign_keys = ON;";
            deferFk.ExecuteNonQuery();

            if (!entry.IsFile)
            {
                var cmdFiles = con.CreateCommand();
                cmdFiles.Transaction = tx;
                cmdFiles.CommandText = """
                    UPDATE Files 
                    SET Name = $newName || SUBSTR(Name, LENGTH($oldName) + 1),
                        Changed = $now
                    WHERE Name = $oldName OR Name LIKE $oldPrefix;
                    """;
                cmdFiles.Parameters.AddWithValue("$oldName", oldName);
                cmdFiles.Parameters.AddWithValue("$newName", newName);
                cmdFiles.Parameters.AddWithValue("$oldPrefix", oldName + "/%");
                cmdFiles.Parameters.AddWithValue("$now", now);
                cmdFiles.ExecuteNonQuery();

                var cmdVers = con.CreateCommand();
                cmdVers.Transaction = tx;
                cmdVers.CommandText = """
                    UPDATE Versions
                    SET FileName = $newName || SUBSTR(FileName, LENGTH($oldName) + 1)
                    WHERE FileName LIKE $oldPrefix OR FileName = $oldName;
                    """;
                cmdVers.Parameters.AddWithValue("$oldName", oldName);
                cmdVers.Parameters.AddWithValue("$newName", newName);
                cmdVers.Parameters.AddWithValue("$oldPrefix", oldName + "/%");
                cmdVers.ExecuteNonQuery();
            }
            else
            {
                var cmdFile = con.CreateCommand();
                cmdFile.Transaction = tx;
                cmdFile.CommandText = "UPDATE Files SET Name = $newName, Changed = $now WHERE Name = $oldName";
                cmdFile.Parameters.AddWithValue("$oldName", oldName);
                cmdFile.Parameters.AddWithValue("$newName", newName);
                cmdFile.Parameters.AddWithValue("$now", now);
                cmdFile.ExecuteNonQuery();

                var cmdVers = con.CreateCommand();
                cmdVers.Transaction = tx;
                cmdVers.CommandText = "UPDATE Versions SET FileName = $newName WHERE FileName = $oldName";
                cmdVers.Parameters.AddWithValue("$oldName", oldName);
                cmdVers.Parameters.AddWithValue("$newName", newName);
                cmdVers.ExecuteNonQuery();
            }

            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    public void BulkDelete(IEnumerable<string> names)
    {
        foreach (var name in names)
            DeleteEntry(name);
    }

    public void BulkMove(IEnumerable<string> names, string targetFolder)
    {
        foreach (var name in names)
        {
            var shortName = name.Contains('/') ? name[(name.LastIndexOf('/') + 1)..] : name;
            var newName   = string.IsNullOrEmpty(targetFolder) ? shortName : $"{targetFolder}/{shortName}";
            MoveEntry(name, newName);
        }
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private static string Now() => DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");

    /// <summary>
    /// Skapar föräldramappar i databasen.
    /// "a/b/c/fil.txt" → skapar "a", "a/b" och "a/b/c".
    /// </summary>
    private void EnsureParentFolders(string path, string now)
    {
        var parts = path.Split('/');
        // Allt utom sista segmentet är föräldrar
        for (var i = 1; i < parts.Length; i++)
        {
            var folderName = string.Join("/", parts[..i]);
            using var con = db.CreateConnection();
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
}
