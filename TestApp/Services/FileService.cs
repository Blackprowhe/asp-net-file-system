using TestApp.Helpers;
using TestApp.Models;

namespace TestApp.Services;

public class FileService
{
    private readonly string _storagePath;

    public FileService()
    {
        _storagePath = Path.Combine(Directory.GetCurrentDirectory(), "Storage");

        if (!Directory.Exists(_storagePath))
        {
            Directory.CreateDirectory(_storagePath);
        }
    }

    public string GetFullPath(string? relativePath)
    {
        relativePath ??= "";
        relativePath = relativePath.Replace('\\', '/').TrimStart('/');

        var fullPath = Path.GetFullPath(Path.Combine(_storagePath, relativePath));
        var storageRoot = Path.GetFullPath(_storagePath);

        if (fullPath != storageRoot &&
            !fullPath.StartsWith(storageRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new UnauthorizedAccessException("Invalid path.");
        }

        return fullPath;
    }

    public bool FileExists(string? path)
    {
        return File.Exists(GetFullPath(path));
    }

    public bool DirectoryExists(string? path)
    {
        return Directory.Exists(GetFullPath(path));
    }

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
            ContentType = ContentTypeHelper.GetContentType(fullPath),
            Name = Path.GetFileName(fullPath)
        };
    }

    public FileItemDto? GetFileMetadata(string path)
    {
        var fullPath = GetFullPath(path);

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

        if (Directory.Exists(fullPath))
        {
            var info = new DirectoryInfo(fullPath);

            return new FileItemDto
            {
                Created = info.CreationTimeUtc.ToString("yyyy-MM-dd HH:mm:ss"),
                Changed = info.LastWriteTimeUtc.ToString("yyyy-MM-dd HH:mm:ss"),
                File = false,
                Bytes = 0,
                Content = GetDirectoryListing(path)
            };
        }

        return null;
    }

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
            else if (Directory.Exists(entry))
            {
                var info = new DirectoryInfo(entry);
                var relativePath = Path.GetRelativePath(_storagePath, entry).Replace('\\', '/');

                result[info.Name] = new FileItemDto
                {
                    Created = info.CreationTimeUtc.ToString("yyyy-MM-dd HH:mm:ss"),
                    Changed = info.LastWriteTimeUtc.ToString("yyyy-MM-dd HH:mm:ss"),
                    File = false,
                    Bytes = 0,
                    Content = GetDirectoryListing(relativePath)
                };
            }
        }

        return result;
    }

    public async Task SaveFileAsync(string path, HttpRequest request)
    {
        var fullPath = GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath);

        if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using var fileStream = new FileStream(fullPath, FileMode.Create, FileAccess.Write);
        await request.Body.CopyToAsync(fileStream);
    }

    public void DeleteFile(string path)
    {
        var fullPath = GetFullPath(path);
        var storageRoot = Path.GetFullPath(_storagePath);

        if (File.Exists(fullPath))
        {
            File.Delete(fullPath);
            RemoveEmptyParentDirectories(Path.GetDirectoryName(fullPath), storageRoot);
            return;
        }

        if (Directory.Exists(fullPath))
        {
            Directory.Delete(fullPath, recursive: true);
            RemoveEmptyParentDirectories(Path.GetDirectoryName(fullPath), storageRoot);
        }
    }

    static void RemoveEmptyParentDirectories(string? dir, string storageRoot)
    {
        var current = dir;
        while (!string.IsNullOrEmpty(current))
        {
            var normalized = Path.GetFullPath(current);
            if (string.Equals(normalized, storageRoot, StringComparison.OrdinalIgnoreCase))
                break;
            if (!Directory.Exists(normalized))
                break;
            if (Directory.EnumerateFileSystemEntries(normalized).Any())
                break;

            var parent = Path.GetDirectoryName(normalized);
            Directory.Delete(normalized);
            current = parent;
        }
    }
}