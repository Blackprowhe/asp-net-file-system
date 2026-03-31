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

        // Lägg till protokoll
        if (!baseUrl.StartsWith("http://") && !baseUrl.StartsWith("https://"))
        {
            if (baseUrl.Contains("localhost"))
                baseUrl = "http://" + baseUrl;
            else
                baseUrl = "https://" + baseUrl;
        }

        using var client = new HttpClient();

        // 🔐 LOGIN (om finns)
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
        // 📥 PULL
        // =====================
        if (command == "pull")
        {
            Console.WriteLine("pull körs");

            var content = await response.Content.ReadAsStringAsync();

            List<JsonElement> files = new();

            try
            {
                // Hantera {} från servern
                if (content.Trim() != "{}")
                {
                    files = JsonSerializer.Deserialize<List<JsonElement>>(content) ?? new();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("JSON ERROR:");
                Console.WriteLine(ex.Message);
                return;
            }

            foreach (var file in files)
            {
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

            // Ta bort lokala filer som inte finns på servern
            var localFiles = Directory.GetFiles(
                Directory.GetCurrentDirectory(),
                "*",
                SearchOption.AllDirectories
            );

            foreach (var fullPath in localFiles)
            {
                var relativePath = Path.GetRelativePath(
                    Directory.GetCurrentDirectory(),
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
        // 📤 PUSH
        // =====================
        else if (command == "push")
        {
            Console.WriteLine("push körs");

            var content = await response.Content.ReadAsStringAsync();

            List<JsonElement> files = new();

            try
            {
                if (content.Trim() != "{}")
                {
                    files = JsonSerializer.Deserialize<List<JsonElement>>(content) ?? new();
                }
            }
            catch
            {
                Console.WriteLine("Kunde inte läsa JSON");
            }

            // fyll serverPaths
            foreach (var file in files)
            {
                if (!file.TryGetProperty("path", out var pathProp))
                    continue;

                var path = pathProp.GetString();
                if (path == null) continue;

                serverPaths.Add(path.Replace("\\", "/"));
            }

            var localFiles = Directory.GetFiles(
                Directory.GetCurrentDirectory(),
                "*",
                SearchOption.AllDirectories
            );

            // Upload
            foreach (var fullPath in localFiles)
            {
                var relativePath = Path.GetRelativePath(
                    Directory.GetCurrentDirectory(),
                    fullPath
                ).Replace("\\", "/");

                Console.WriteLine($"Skickar: {relativePath}");

                var fileUrl = baseUrl + "/api/files/" + relativePath;

                var bytes = await File.ReadAllBytesAsync(fullPath);
                var fileContent = new ByteArrayContent(bytes);

                var result = await client.PutAsync(fileUrl, fileContent);

                if (!result.IsSuccessStatusCode)
                {
                    Console.WriteLine($"Fel vid upload: {relativePath}");
                }
            }

            // Delete
            foreach (var serverPath in serverPaths)
            {
                var existsLocally = localFiles.Any(fullPath =>
                {
                    var relativePath = Path.GetRelativePath(
                        Directory.GetCurrentDirectory(),
                        fullPath
                    ).Replace("\\", "/");

                    return relativePath == serverPath;
                });

                if (!existsLocally)
                {
                    Console.WriteLine($"Tar bort från server: {serverPath}");

                    var deleteUrl = baseUrl + "/api/files/" + serverPath;
                    await client.DeleteAsync(deleteUrl);
                }
            }
        }
    }
}