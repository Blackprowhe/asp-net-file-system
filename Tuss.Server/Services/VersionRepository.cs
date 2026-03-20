using Tuss.Server.Models;

namespace Tuss.Server.Services;

/// <summary>
/// Ansvarar för CRUD mot Versions-tabellen samt versionsåterställning.
/// </summary>
public class VersionRepository
{
    private readonly DatabaseService _db;

    public VersionRepository(DatabaseService db)
    {
        _db = db;
    }

    /// <summary>Hämtar alla versioner av en fil, sorterade fallande.</summary>
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
            list.Add(MapRow(r));
        return list;
    }

    /// <summary>Hämtar en specifik version av en fil.</summary>
    public FileVersion? GetVersion(string fileName, int version)
    {
        using var con = _db.CreateConnection();
        var cmd = con.CreateCommand();
        cmd.CommandText = """
            SELECT Id, FileName, Version, DiskPath, CreatedAt, Bytes
            FROM Versions WHERE FileName = $name AND Version = $version
            """;
        cmd.Parameters.AddWithValue("$name", fileName);
        cmd.Parameters.AddWithValue("$version", version);

        using var r = cmd.ExecuteReader();
        return r.Read() ? MapRow(r) : null;
    }

    /// <summary>Infogar en ny versionsrad i databasen (inom en befintlig transaktion).</summary>
    internal void InsertVersion(
        Microsoft.Data.Sqlite.SqliteConnection con,
        Microsoft.Data.Sqlite.SqliteTransaction tx,
        string fileName, int version, string diskPath, string createdAt, long bytes)
    {
        var cmd = con.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT OR IGNORE INTO Versions (FileName, Version, DiskPath, CreatedAt, Bytes)
            VALUES ($name, $version, $diskPath, $now, $bytes);
            """;
        cmd.Parameters.AddWithValue("$name", fileName);
        cmd.Parameters.AddWithValue("$version", version);
        cmd.Parameters.AddWithValue("$diskPath", diskPath);
        cmd.Parameters.AddWithValue("$now", createdAt);
        cmd.Parameters.AddWithValue("$bytes", bytes);
        cmd.ExecuteNonQuery();
    }

    private static FileVersion MapRow(Microsoft.Data.Sqlite.SqliteDataReader r) => new()
    {
        Id        = r.GetInt64(0),
        FileName  = r.GetString(1),
        Version   = r.GetInt32(2),
        DiskPath  = r.GetString(3),
        CreatedAt = r.GetString(4),
        Bytes     = r.GetInt64(5),
    };
}

