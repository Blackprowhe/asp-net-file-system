namespace Tuss.Server.Services;

/// <summary>
/// Ansvarar för all disk-I/O: läsa, skriva och radera filer på disk.
/// Äger <c>storage/</c>-mappen och vet hur versionerade filnamn byggs.
/// </summary>
public class FileStorageService
{
    private readonly string _storageRoot;

    public FileStorageService(IWebHostEnvironment env)
    {
        _storageRoot = Path.Combine(env.ContentRootPath, "storage");
        Directory.CreateDirectory(_storageRoot);
    }

    /// <summary>Returnerar disksökvägen för en specifik version av en fil.</summary>
    public string GetVersionDiskPath(string name, int version)
    {
        var safeName = name.Replace("/", "_").Replace("\\", "_");
        return Path.Combine(_storageRoot, $"{safeName}_v{version}");
    }

    /// <summary>Sparar en ström till disk och returnerar antalet sparade bytes.</summary>
    public async Task<long> SaveToDiskAsync(string diskPath, Stream source)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(diskPath)!);
        await using (var fs = File.Create(diskPath))
            await source.CopyToAsync(fs);

        return new FileInfo(diskPath).Length;
    }

    /// <summary>Öppnar en fil för läsning.</summary>
    public Stream OpenRead(string diskPath) => File.OpenRead(diskPath);

    /// <summary>Raderar en fil från disk om den existerar.</summary>
    public void DeleteIfExists(string diskPath)
    {
        if (File.Exists(diskPath))
            File.Delete(diskPath);
    }
}

