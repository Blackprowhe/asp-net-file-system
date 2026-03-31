using System.Net.Http; // används för att skicka HTTP requests
using System.Text; // används för encoding (t.ex. JSON)
using System.Text.Json; // används för att hantera JSON

public class Program
{
    public static async Task Main(string[] args)
    {
        // kontrollera att minst 2 argument finns (kommando + url)
        if (args.Length < 2)
        {
            Console.WriteLine("ingen url hittades");
            Environment.Exit(1); // avsluta programmet med felkod
        }

        var command = args[0]; // första argumentet: pull eller push
        var baseUrl = args[1]; // andra argumentet: serveradress

        // kontrollera att kommandot är giltigt
        if (command != "pull" && command != "push")
        {
            Console.WriteLine("ogiltigt kommando");
            Environment.Exit(1);
        }

        // lägg till http eller https om det saknas
        if (!baseUrl.StartsWith("http://") && !baseUrl.StartsWith("https://"))
        {
            if (baseUrl.Contains("localhost"))
                baseUrl = "http://" + baseUrl; // localhost använder http
            else
                baseUrl = "https://" + baseUrl; // annars https
        }

        using var client = new HttpClient(); // skapa HTTP-klient

        // logga in om användarnamn och lösenord finns
        if (args.Length >= 4)
        {
            var loginUrl = baseUrl + "/api/login"; // login endpoint

            var loginData = new
            {
                username = args[2], // användarnamn
                password = args[3]  // lösenord
            };

            var json = JsonSerializer.Serialize(loginData); // gör om till JSON
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            await client.PostAsync(loginUrl, content); // skicka login request
        }

        HttpResponseMessage response;

        try
        {
            // hämta lista med filer från servern
            response = await client.GetAsync(baseUrl + "/api/files");

            if (!response.IsSuccessStatusCode)
                Environment.Exit(1); // avsluta om servern svarar med fel
        }
        catch
        {
            Environment.Exit(1); // avsluta om servern inte nås
            return;
        }

        var serverPaths = new HashSet<string>(); // lista med filer på servern

        // =====================
        // PULL
        // =====================
        if (command == "pull")
        {
            Console.WriteLine("pull körs");

            var content = await response.Content.ReadAsStringAsync(); // läs JSON från servern

            List<JsonElement> files = new(); // lista med filer

            try
            {
                // om servern inte skickar {} så försök läsa lista
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

            // loopa igenom alla filer från servern
            foreach (var file in files)
            {
                // kontrollera att "path" finns
                if (!file.TryGetProperty("path", out var pathProp))
                    continue;

                var path = pathProp.GetString(); // hämta filens sökväg
                if (path == null) continue;

                Console.WriteLine($"Hämtar: {path}");

                var fileUrl = baseUrl + "/api/files/" + path; // url till filen
                var fileResponse = await client.GetAsync(fileUrl); // hämta filen

                if (!fileResponse.IsSuccessStatusCode)
                    continue;

                var bytes = await fileResponse.Content.ReadAsByteArrayAsync(); // läs fil som bytes

                var directory = Path.GetDirectoryName(path); // hämta mapp
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory); // skapa mapp om den saknas

                File.WriteAllBytes(path, bytes); // spara fil lokalt

                var normalizedPath = path.Replace("/", Path.DirectorySeparatorChar.ToString());
                serverPaths.Add(normalizedPath); // spara i lista
            }

            // hämta alla lokala filer
            var localFiles = Directory.GetFiles(
                Directory.GetCurrentDirectory(),
                "*",
                SearchOption.AllDirectories
            );

            // ta bort lokala filer som inte finns på servern
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
        // PUSH
        // =====================
        else if (command == "push")
        {
            Console.WriteLine("push körs");

            var content = await response.Content.ReadAsStringAsync(); // läs serverns filer

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

            // fyll serverPaths med filer från servern
            foreach (var file in files)
            {
                if (!file.TryGetProperty("path", out var pathProp))
                    continue;

                var path = pathProp.GetString();
                if (path == null) continue;

                serverPaths.Add(path.Replace("\\", "/"));
            }

            // hämta alla lokala filer
            var localFiles = Directory.GetFiles(
                Directory.GetCurrentDirectory(),
                "*",
                SearchOption.AllDirectories
            );

            // ladda upp alla lokala filer
            foreach (var fullPath in localFiles)
            {
                var relativePath = Path.GetRelativePath(
                    Directory.GetCurrentDirectory(),
                    fullPath
                ).Replace("\\", "/");

                Console.WriteLine($"Skickar: {relativePath}");

                var fileUrl = baseUrl + "/api/files/" + relativePath;

                var bytes = await File.ReadAllBytesAsync(fullPath); // läs fil som bytes
                var fileContent = new ByteArrayContent(bytes);

                var result = await client.PutAsync(fileUrl, fileContent); // skicka till servern

                if (!result.IsSuccessStatusCode)
                {
                    Console.WriteLine($"Fel vid upload: {relativePath}");
                }
            }

            // ta bort filer på servern som inte finns lokalt
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