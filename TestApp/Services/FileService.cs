using TestApp.Helpers;
using TestApp.Models;
using TestApp.Data;
using Microsoft.EntityFrameworkCore;

namespace TestApp.Services;

// Service-klass som hanterar all fil- och mapphantering.
// Ansvarar för att läsa, skriva, ta bort och lista filer,
// samt hantera versionshistorik via databasen.
public class FileService
{
    private readonly string _storagePath;
    private readonly AppDbContext _context;

    public FileService(AppDbContext context)
    {
        _context = context;

        // Sätter root-mappen där alla filer lagras
        _storagePath = Path.Combine(Directory.GetCurrentDirectory(), "Storage");

        // Skapar Storage-mappen om den inte finns
        if (!Directory.Exists(_storagePath))
        {
            Directory.CreateDirectory(_storagePath);
        }
    }

    // Bygger en säker absolut sökväg från en relativ path.
    // Skyddar mot directory traversal (t.ex. ../../).
    public string GetFullPath(string? relativePath)
    {
        if (string.IsNullOrEmpty(relativePath))
            relativePath = "";

        // Normaliserar path (byter \ till / och tar bort leading /)
        relativePath = relativePath.Replace('\\', '/').TrimStart('/');

        var fullPath = Path.GetFullPath(Path.Combine(_storagePath, relativePath));
        var storageRoot = Path.GetFullPath(_storagePath);

        // Säkerhetscheck: path måste ligga inom Storage
        if (fullPath != storageRoot &&
            !fullPath.StartsWith(storageRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new UnauthorizedAccessException("Invalid path.");
        }

        return fullPath;
    }

    // Kollar om en fil finns
    public bool FileExists(string? path)
    {
        return File.Exists(GetFullPath(path));
    }

    // Kollar om en mapp finns
    public bool DirectoryExists(string? path)
    {
        return Directory.Exists(GetFullPath(path));
    }

    // Hämtar en fil (innehåll + MIME-typ)
    public FileContentResult? GetFile(string path)
    {
        var fullPath = GetFullPath(path);

        if (!File.Exists(fullPath))
        {
            return null;
        }

        return new FileContentResult
        {
            Bytes = File.ReadAllBytes(fullPath),
            ContentType = ContentTypeHelper.GetContentType(fullPath), // MIME-typ
            Name = Path.GetFileName(fullPath)
        };
    }

    // Hämtar metadata för fil eller mapp (används i HEAD/GET)
    public FileItemDto? GetFileMetadata(string path)
    {
        var fullPath = GetFullPath(path);

        // Om det är en fil
        if (File.Exists(fullPath))
        {
            var info = new FileInfo(fullPath);

            return new FileItemDto
            {
                Created = info.CreationTimeUtc.ToString("yyyy-MM-dd HH:mm:ss"),
                Changed = info.LastWriteTimeUtc.ToString("yyyy-MM-dd HH:mm:ss"),
                File = true,
                Bytes = info.Length,
                Extension = info.Extension
            };
        }

        // Om det är en mapp
        if (Directory.Exists(fullPath))
        {
            var info = new DirectoryInfo(fullPath);

            return new FileItemDto
            {
                Created = info.CreationTimeUtc.ToString("yyyy-MM-dd HH:mm:ss"),
                Changed = info.LastWriteTimeUtc.ToString("yyyy-MM-dd HH:mm:ss"),
                File = false,
                Bytes = 0,
                Content = GetDirectoryListing(path) // innehåll i mappen
            };
        }

        return null;
    }

    // Hämtar alla filer/mappar i en katalog (rekursivt)
    public Dictionary<string, FileItemDto>? GetDirectoryListing(string? path)
    {
        var fullPath = GetFullPath(path);

        if (!Directory.Exists(fullPath))
        {
            return null;
        }

        var entries = Directory.GetFileSystemEntries(fullPath);
        var result = new Dictionary<string, FileItemDto>();

        foreach (var entry in entries)
        {
            // Om det är en fil
            if (File.Exists(entry))
            {
                var info = new FileInfo(entry);

                result[info.Name] = new FileItemDto
                {
                    Created = info.CreationTimeUtc.ToString("yyyy-MM-dd HH:mm:ss"),
                    Changed = info.LastWriteTimeUtc.ToString("yyyy-MM-dd HH:mm:ss"),
                    File = true,
                    Bytes = info.Length,
                    Extension = info.Extension
                };
            }
            // Om det är en mapp
            else if (Directory.Exists(entry))
            {
                var info = new DirectoryInfo(entry);

                // Gör path relativ igen
                var relativePath = Path.GetRelativePath(_storagePath, entry).Replace('\\', '/');

                result[info.Name] = new FileItemDto
                {
                    Created = info.CreationTimeUtc.ToString("yyyy-MM-dd HH:mm:ss"),
                    Changed = info.LastWriteTimeUtc.ToString("yyyy-MM-dd HH:mm:ss"),
                    File = false,
                    Bytes = 0,
                    Content = GetDirectoryListing(relativePath) // rekursivt
                };
            }
        }

        return result;
    }

    // Sparar fil eller skapar mapp
    public async Task SaveFileAsync(string path, HttpRequest request)
    {
        var fullPath = GetFullPath(path);

        // Om path slutar med "/" → skapa mapp
        if (path.EndsWith("/"))
        {
            if (!Directory.Exists(fullPath))
            {
                Directory.CreateDirectory(fullPath);
            }
            return;
        }

        // Säkerställ att parent-mapp finns
        var directory = Path.GetDirectoryName(fullPath);

        if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        // Läser request body (filens innehåll)
        byte[] bytes;
        string newContent = "";

        if (request.HasFormContentType)
        {
            var form = await request.ReadFormAsync();
            var file = form.Files["file"];

            if (file == null)
                throw new Exception("Ingen fil skickades.");

            using var ms = new MemoryStream();
            await file.CopyToAsync(ms);
            bytes = ms.ToArray();

            newContent = null; // 🔥 viktigt
        }
        else
        {
            using var memoryStream = new MemoryStream();
            await request.Body.CopyToAsync(memoryStream);

            bytes = memoryStream.ToArray();
            newContent = System.Text.Encoding.UTF8.GetString(bytes);
        }

        // Om fil redan finns → spara historik i databasen
        if (File.Exists(fullPath))
        {
            var latestVersion = await _context.FileHistories
                .Where(f => f.FilePath == path)
                .MaxAsync(f => (int?)f.Version) ?? 0;

            var history = new FileHistory
            {
                FilePath = path,
                Version = latestVersion + 1,
               Content = newContent ?? "",
                CreatedAt = DateTime.UtcNow
            };

            _context.FileHistories.Add(history);
            await _context.SaveChangesAsync();
        }

        // Skriver filen till disk
        await File.WriteAllBytesAsync(fullPath, bytes);
    }

    // Tar bort fil eller mapp
    public void DeleteFile(string path)
    {
        var fullPath = GetFullPath(path);
        var storageRoot = Path.GetFullPath(_storagePath);

        // Om fil
        if (File.Exists(fullPath))
        {
            File.Delete(fullPath);

            // Tar bort tomma mappar uppåt
            RemoveEmptyParentDirectories(Path.GetDirectoryName(fullPath), storageRoot);
            return;
        }

        // Om mapp
        if (Directory.Exists(fullPath))
        {
            Directory.Delete(fullPath, recursive: true);

            RemoveEmptyParentDirectories(Path.GetDirectoryName(fullPath), storageRoot);
        }
    }

    // Tar bort tomma parent-mappar (cleanup)
    static void RemoveEmptyParentDirectories(string? dir, string storageRoot)
    {
        var current = dir;

        while (!string.IsNullOrEmpty(current))
        {
            var normalized = Path.GetFullPath(current);

            // Stoppar vid root
            if (string.Equals(normalized, storageRoot, StringComparison.OrdinalIgnoreCase))
                break;

            if (!Directory.Exists(normalized))
                break;

            // Om mappen inte är tom → stoppa
            if (Directory.EnumerateFileSystemEntries(normalized).Any())
                break;

            var parent = Path.GetDirectoryName(normalized);

            Directory.Delete(normalized);
            current = parent;
        }
    }
    public async Task SaveFileStreamAsync(string path, Stream stream)
{
    var fullPath = GetFullPath(path);

    // skapa mapp om behövs
    var directory = Path.GetDirectoryName(fullPath);

    if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
    {
        Directory.CreateDirectory(directory);
    }

    using var fileStream = File.Create(fullPath);
    await stream.CopyToAsync(fileStream);
}
}