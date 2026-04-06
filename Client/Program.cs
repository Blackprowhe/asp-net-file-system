using System.Net.Http;
using System.Text;
using System.Text.Json;

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

        var root = Path.Combine(Path.GetTempPath(), "myclient", Guid.NewGuid().ToString());
        Directory.CreateDirectory(root);
        Directory.SetCurrentDirectory(root);
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

        string Normalize(string path)
        {
            return path
                .Replace("\\", "/")
                .Replace("./", "")   // 🔥 fixar Windows-problemet
                .Trim('/')
                .Trim();
        }

        // =====================
        // PULL
        // =====================
        if (command == "pull")
        {
            Console.WriteLine("pull körs");

            var content = await response.Content.ReadAsStringAsync();

            Console.WriteLine("SERVER RESPONSE:");
            Console.WriteLine(content);

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

            ExtractPaths(files, "", serverPaths);
            serverPaths = serverPaths
                .Select(p => Normalize(p).Trim('/').Trim().ToLower())
                .ToHashSet();

            foreach (var serverPath in serverPaths)
            {
                var fileUrl = baseUrl + "/api/files/" + serverPath;
                var fileResponse = await client.GetAsync(fileUrl);

                if (!fileResponse.IsSuccessStatusCode)
                    continue;

                var bytes = await fileResponse.Content.ReadAsByteArrayAsync();

                var fullPath = Path.Combine(root, serverPath.Replace("/", Path.DirectorySeparatorChar.ToString()));

                var directory = Path.GetDirectoryName(fullPath);
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);

                File.WriteAllBytes(fullPath, bytes);
            }

            var localFiles = Directory.GetFiles(root, "*", SearchOption.AllDirectories);

            foreach (var fullPath in localFiles)
            {
                var relativePath = Normalize(Path.GetRelativePath(root, fullPath))
                    .Trim('/')
                    .Trim()
                    .ToLower();

                if (string.IsNullOrWhiteSpace(relativePath))
                    continue;

                if (!serverPaths.Contains(relativePath))
                {
                    Console.WriteLine($"Tar bort: {relativePath}");
                    File.Delete(fullPath);
                }
            }

            // 🔥 CLEANUP TOMMA MAPPAR (korrekt version)
            bool deleted;

            do
            {
                deleted = false;

                var directories = Directory.GetDirectories(root, "*", SearchOption.AllDirectories)
                    .OrderByDescending(d => d.Length);

                foreach (var dir in directories)
                {
                    if (!Directory.EnumerateFileSystemEntries(dir).Any())
                    {
                        Directory.Delete(dir);
                        deleted = true;
                    }
                }

            } while (deleted);
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

            var localFiles = Directory.GetFiles(root, "*", SearchOption.AllDirectories);

            foreach (var fullPath in localFiles)
            {
                var relativePath = Path.GetFileName(fullPath).ToLower();

                // 🔥 IGNORERA SYSTEMMAPPar
                if (relativePath.StartsWith("bin/") ||
                    relativePath.StartsWith("obj/") ||
                    relativePath.StartsWith("storage/") ||
                    relativePath.StartsWith("wwwroot/") ||
                    relativePath.StartsWith("tests/") ||
                    relativePath.EndsWith(".cs") ||
                    relativePath.EndsWith(".csproj"))
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
                }
            }
        }

        // =====================
        // ExtractPaths (FIXED)
        // =====================
        static void ExtractPaths(Dictionary<string, JsonElement> files, string currentPath, HashSet<string> paths)
        {
            foreach (var kvp in files)
            {
                var name = kvp.Key;
                var file = kvp.Value;

                var fullPath = string.IsNullOrEmpty(currentPath)
                    ? name
                    : currentPath + "/" + name;

                if (file.TryGetProperty("file", out var isFileProp) && isFileProp.GetBoolean())
                {
                    paths.Add(fullPath);
                }

                if (file.TryGetProperty("file", out var isDirProp) && !isDirProp.GetBoolean())
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