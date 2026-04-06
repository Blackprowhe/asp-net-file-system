using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

public class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Length < 2)
        {
            Console.WriteLine("ingen url hittades");
            return 1;
        }

        var command = args[0];
        var baseUrl = args[1];

        if (command is not ("pull" or "push"))
        {
            Console.WriteLine("ogiltigt kommando");
            return 1;
        }

        if (!baseUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            && !baseUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            baseUrl = baseUrl.Contains("localhost", StringComparison.OrdinalIgnoreCase)
                ? "http://" + baseUrl
                : "https://" + baseUrl;
        }

        baseUrl = baseUrl.TrimEnd('/');

        using var client = new HttpClient();

        if (args.Length >= 4)
        {
            var loginJson = JsonSerializer.Serialize(new { username = args[2], password = args[3] });
            var content = new StringContent(loginJson, Encoding.UTF8, "application/json");
            try
            {
                await client.PostAsync(baseUrl + "/api/login", content);
            }
            catch
            {
                return 1;
            }
        }

        var root = Directory.GetCurrentDirectory();

        HttpResponseMessage listResponse;
        try
        {
            listResponse = await client.GetAsync(baseUrl + "/api/files");
            if (!listResponse.IsSuccessStatusCode)
                return 1;
        }
        catch
        {
            return 1;
        }

        var body = await listResponse.Content.ReadAsStringAsync();

        Dictionary<string, JsonElement> rootListing;
        try
        {
            rootListing = string.IsNullOrWhiteSpace(body) || body.Trim() == "{}"
                ? new Dictionary<string, JsonElement>()
                : JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(body) ?? new();
        }
        catch
        {
            return 1;
        }

        if (command == "pull")
        {
            var serverFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            CollectFilePaths(rootListing, "", serverFiles);

            foreach (var relPath in serverFiles)
            {
                var url = baseUrl + "/api/files/" + Uri.EscapeDataString(relPath.Replace('\\', '/'));
                HttpResponseMessage fileResponse;
                try
                {
                    fileResponse = await client.GetAsync(url);
                }
                catch
                {
                    return 1;
                }

                if (!fileResponse.IsSuccessStatusCode)
                    continue;

                var bytes = await fileResponse.Content.ReadAsByteArrayAsync();
                var dest = SafePathUnderRoot(root, relPath);
                if (dest is null)
                    continue;

                var parent = Path.GetDirectoryName(dest);
                if (!string.IsNullOrEmpty(parent))
                    Directory.CreateDirectory(parent);

                await File.WriteAllBytesAsync(dest, bytes);
            }

            foreach (var fullPath in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
            {
                var rel = NormalizeRelativePath(Path.GetRelativePath(root, fullPath));
                if (string.IsNullOrEmpty(rel) || rel.Contains("..", StringComparison.Ordinal))
                    continue;

                if (!serverFiles.Contains(rel))
                    File.Delete(fullPath);
            }

            PruneEmptyDirectories(root);
        }
        else
        {
            var serverFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            CollectFilePaths(rootListing, "", serverFiles);

            var localFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var fullPath in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
            {
                var rel = NormalizeRelativePath(Path.GetRelativePath(root, fullPath));
                if (string.IsNullOrEmpty(rel) || rel.Contains("..", StringComparison.Ordinal))
                    continue;
                localFiles.Add(rel);
            }

            foreach (var relPath in serverFiles)
            {
                if (localFiles.Contains(relPath))
                    continue;

                var url = baseUrl + "/api/files/" + Uri.EscapeDataString(relPath.Replace('\\', '/'));
                try
                {
                    var del = await client.DeleteAsync(url);
                    if (!del.IsSuccessStatusCode)
                        return 1;
                }
                catch
                {
                    return 1;
                }
            }

            if (localFiles.Count == 0)
            {
                for (var i = 0; i < 32; i++)
                {
                    HttpResponseMessage again;
                    try
                    {
                        again = await client.GetAsync(baseUrl + "/api/files");
                        if (!again.IsSuccessStatusCode)
                            return 1;
                    }
                    catch
                    {
                        return 1;
                    }

                    var text = await again.Content.ReadAsStringAsync();
                    Dictionary<string, JsonElement> listing;
                    try
                    {
                        listing = string.IsNullOrWhiteSpace(text) || text.Trim() == "{}"
                            ? new Dictionary<string, JsonElement>()
                            : JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(text) ?? new();
                    }
                    catch
                    {
                        return 1;
                    }

                    if (listing.Count == 0)
                        break;

                    foreach (var key in listing.Keys.ToList())
                    {
                        var url = baseUrl + "/api/files/" + Uri.EscapeDataString(key.Replace('\\', '/'));
                        try
                        {
                            var del = await client.DeleteAsync(url);
                            if (!del.IsSuccessStatusCode)
                                return 1;
                        }
                        catch
                        {
                            return 1;
                        }
                    }
                }
            }

            foreach (var relPath in localFiles)
            {
                var localPath = SafePathUnderRoot(root, relPath);
                if (localPath is null || !File.Exists(localPath))
                    continue;

                var url = baseUrl + "/api/files/" + Uri.EscapeDataString(relPath.Replace('\\', '/'));
                var bytes = await File.ReadAllBytesAsync(localPath);
                using var putContent = new ByteArrayContent(bytes);
                putContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

                HttpResponseMessage putResponse;
                try
                {
                    putResponse = await client.PutAsync(url, putContent);
                }
                catch
                {
                    return 1;
                }

                if (!putResponse.IsSuccessStatusCode)
                    return 1;
            }
        }

        return 0;
    }

    static string NormalizeRelativePath(string relative) =>
        relative.Replace('\\', '/').TrimStart('/');

    static string? SafePathUnderRoot(string root, string relative)
    {
        relative = relative.Replace('\\', '/').TrimStart('/');
        foreach (var segment in relative.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment is "." or "..")
                return null;
        }

        var combined = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        var fullRoot = Path.GetFullPath(root);
        if (!combined.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
            return null;
        if (combined.Length > fullRoot.Length
            && combined[fullRoot.Length] != Path.DirectorySeparatorChar)
            return null;

        return combined;
    }

    static void CollectFilePaths(
        Dictionary<string, JsonElement> nodes,
        string prefix,
        HashSet<string> filePaths)
    {
        foreach (var (name, el) in nodes)
        {
            var fullPath = string.IsNullOrEmpty(prefix) ? name : prefix + "/" + name;

            if (!el.TryGetProperty("file", out var fileProp))
                continue;

            if (fileProp.GetBoolean())
            {
                filePaths.Add(fullPath);
                continue;
            }

            if (!el.TryGetProperty("content", out var contentProp))
                continue;

            Dictionary<string, JsonElement>? sub;
            try
            {
                sub = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(contentProp.GetRawText());
            }
            catch
            {
                continue;
            }

            if (sub is { Count: > 0 })
                CollectFilePaths(sub, fullPath, filePaths);
        }
    }

    static void PruneEmptyDirectories(string root)
    {
        bool changed;
        do
        {
            changed = false;
            foreach (var dir in Directory.GetDirectories(root, "*", SearchOption.AllDirectories)
                         .OrderByDescending(d => d.Length))
            {
                if (Directory.EnumerateFileSystemEntries(dir).Any())
                    continue;
                Directory.Delete(dir);
                changed = true;
            }
        } while (changed);
    }
}
