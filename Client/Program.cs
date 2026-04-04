using System.Net.Http; // används för att skicka HTTP requests
using System.Text; // används för encoding (t.ex. JSON)
using System.Text.Json; // används för att hantera JSON

public class Program
{
    public static async Task Main(string[] args)
    {

        if (args.Length < 2)
        {
            Console.WriteLine("ingen url hittades");
            Environment.Exit(1);
        }

        var command = args[0];
        var baseUrl = args[1];

        if (command != "pull" && command != "push")
        {
            Console.WriteLine("ogiltigt kommando");
            Environment.Exit(1);
        }

        if (!baseUrl.StartsWith("http://") && !baseUrl.StartsWith("https://"))
        {
            if (baseUrl.Contains("localhost"))
                baseUrl = "http://" + baseUrl;
            else
                baseUrl = "https://" + baseUrl;
        }

        using var client = new HttpClient();

        var root = Directory.GetCurrentDirectory();

        if (args.Length >= 4)
        {
            var loginUrl = baseUrl + "/api/login";

            var loginData = new
            {
                username = args[2],
                password = args[3]
            };

            var json = JsonSerializer.Serialize(loginData);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            await client.PostAsync(loginUrl, content);
        }

        HttpResponseMessage response;

        try
        {
            response = await client.GetAsync(baseUrl + "/api/files");

            if (!response.IsSuccessStatusCode)
                Environment.Exit(1);
        }
        catch
        {
            Environment.Exit(1);
            return;
        }

        var serverPaths = new HashSet<string>();

        // =====================
        // PULL
        // =====================
        if (command == "pull")
        {
            Console.WriteLine("pull körs");

            var content = await response.Content.ReadAsStringAsync();

            Dictionary<string, JsonElement> files = new();

            try
            {
                if (content.Trim() != "{}")
                {
                    files = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(content) ?? new();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("JSON ERROR:");
                Console.WriteLine(ex.Message);
                return;
            }

            foreach (var kvp in files)
            {
                var file = kvp.Value;

                if (!file.TryGetProperty("path", out var pathProp))
                    continue;

                var path = pathProp.GetString();
                if (path == null) continue;

                Console.WriteLine($"Hämtar: {path}");

                var fileUrl = baseUrl + "/api/files/" + path;
                var fileResponse = await client.GetAsync(fileUrl);

                if (!fileResponse.IsSuccessStatusCode)
                    continue;

                var bytes = await fileResponse.Content.ReadAsByteArrayAsync();

                var directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);

                File.WriteAllBytes(path, bytes);

                var normalizedPath = path.Replace("/", Path.DirectorySeparatorChar.ToString());
                serverPaths.Add(normalizedPath);
            }

            var localFiles = Directory.GetFiles(
                root,
                "*",
                SearchOption.AllDirectories
            );

            foreach (var fullPath in localFiles)
            {
                var relativePath = Path.GetRelativePath(
                    root,
                    fullPath
                ).Replace("/", Path.DirectorySeparatorChar.ToString());

                if (!serverPaths.Contains(relativePath))
                {
                    Console.WriteLine($"Tar bort: {relativePath}");
                    File.Delete(fullPath);
                }
            }
        }

        // =====================
        // PUSH
        // =====================
        else if (command == "push")
        {
            Console.WriteLine("push körs");

            var content = await response.Content.ReadAsStringAsync();

            Dictionary<string, JsonElement> files = new();

            try
            {
                if (content.Trim() != "{}")
                {
                    files = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(content) ?? new();
                }
            }
            catch
            {
                Console.WriteLine("Kunde inte läsa JSON");
            }

           ExtractPaths(files, "", serverPaths);

            var localFiles = Directory.GetFiles(
                root,
                "*",
                SearchOption.AllDirectories
            );

            foreach (var fullPath in localFiles)
            {
                var relativePath = Path.GetRelativePath(root, fullPath)
                    .Replace("\\", "/");

                if (relativePath.StartsWith("bin/") ||
                    relativePath.StartsWith("obj/"))
                {
                    continue;
                }

                Console.WriteLine($"Skickar: {relativePath}");

                var fileUrl = baseUrl + "/api/files/" + relativePath;

                var bytes = await File.ReadAllBytesAsync(fullPath);

                var fileContent = new ByteArrayContent(bytes);
                fileContent.Headers.ContentType =
                    new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");

                var result = await client.PutAsync(fileUrl, fileContent);

                if (!result.IsSuccessStatusCode)
                {
                    Console.WriteLine($"Fel vid upload: {relativePath}");
                    Console.WriteLine($"Status: {result.StatusCode}");
                    continue;
                }
            }
        }

        // =====================
        // DELETE på server
        // =====================

        var localFilesForDelete = Directory.GetFiles(
            root,
            "*",
            SearchOption.AllDirectories
        );

       if (command == "pull")
{
    foreach (var serverPath in serverPaths)
    {
        var normalizedServerPath = serverPath.Replace("\\", "/");

        var existsLocally = localFilesForDelete.Any(fullPath =>
        {
            var relativePath = Path.GetRelativePath(root, fullPath)
                .Replace("\\", "/");

            return relativePath.Equals(normalizedServerPath, StringComparison.OrdinalIgnoreCase);
        });

        if (!existsLocally)
        {
            Console.WriteLine($"Tar bort från server: {serverPath}");

            var deleteUrl = baseUrl + "/api/files/" + normalizedServerPath;
            await client.DeleteAsync(deleteUrl);
        }
    }
}
    static void ExtractPaths(Dictionary<string, JsonElement> files, string currentPath, HashSet<string> paths)
{
    foreach (var kvp in files)
    {
        var name = kvp.Key;
        var file = kvp.Value;

        var fullPath = string.IsNullOrEmpty(currentPath)
            ? name
            : currentPath + "/" + name;

        paths.Add(fullPath);

        if (file.TryGetProperty("file", out var isFileProp) && !isFileProp.GetBoolean())
        {
            if (file.TryGetProperty("content", out var contentProp))
            {
                var subFiles = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(contentProp.GetRawText());
                if (subFiles != null)
                {
                    ExtractPaths(subFiles, fullPath, paths);
                }
            }
        }
    }
}
}       
}


