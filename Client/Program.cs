using System.Net.Http;
using System.Text.Json;
public class Program
{
    public static async Task Main(string[] args)
    {
        // 1. Saknas URL
        if (args.Length < 2)
        {
            Console.WriteLine("ingen url hittades");
            Environment.Exit(1);
        }

        var baseUrl = args[1];

        // 2. Lägg till rätt protokoll
        if (!baseUrl.StartsWith("http://") && !baseUrl.StartsWith("https://"))
        {
            if (baseUrl.Contains("localhost"))
            {
                baseUrl = "http://" + baseUrl;
            }
            else
            {
                baseUrl = "https://" + baseUrl;
            }
        }

        var url = baseUrl + "/api/files";

        //  Skicka en GET-förfrågan
        var client = new HttpClient();
        HttpResponseMessage response;
       
       
        // hantera eventuella problem som kan tillkomma
        try
        {
            response = await client.GetAsync(url);

            if (!response.IsSuccessStatusCode)
            {
                Environment.Exit(1);
            }
        }
        catch
        {
            Environment.Exit(1);
            return;
        }
        // hanterar pull om kommandot stämmer överens med det som skickats in i argumenten
        if (args[0] == "pull")
        {
            Console.WriteLine("pull körs");

            var content = await response.Content.ReadAsStringAsync();
var files = JsonSerializer.Deserialize<List<Dictionary<string, object>>>(content);

if (files != null)
{
    foreach (var file in files)
    {
        var path = file["path"]?.ToString();

       if (path == null)
        {
         continue;
        }
        Console.WriteLine(path);

        var fileUrl = baseUrl + "/api/files/" + path;

        var fileResponse = await client.GetAsync(fileUrl);

        if (!fileResponse.IsSuccessStatusCode)
        {
            continue;
        }

       var bytes = await fileResponse.Content.ReadAsByteArrayAsync();


        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllBytes(path, bytes);
    }

    //  1. Skapa en lista med alla paths från servern
var serverPaths = new HashSet<string>();

foreach (var file in files)
{
    var path = file["path"]?.ToString();

    if (path == null)
        continue;

    //  Normalisera path (så Windows inte klagar)
    path = path.Replace("/", Path.DirectorySeparatorChar.ToString());

    serverPaths.Add(path);
}

//  2. Hämta alla lokala filer (rekursivt)
var localFiles = Directory.GetFiles(
    Directory.GetCurrentDirectory(),
    "*",
    SearchOption.AllDirectories
);

//  3. Loopa igenom alla lokala filer
foreach (var fullPath in localFiles)
{
    //  Gör om till relativ path (så det matchar servern)
    var relativePath = Path.GetRelativePath(
        Directory.GetCurrentDirectory(),
        fullPath
    );

    //  Normalisera även här
    relativePath = relativePath.Replace("/", Path.DirectorySeparatorChar.ToString());

    //  4. Om filen INTE finns på servern → ta bort
    if (!serverPaths.Contains(relativePath))
    {
        Console.WriteLine($"Tar bort: {relativePath}");

        File.Delete(fullPath);
    }
}
}
        }
            else if (args[0] == "push")
            {
                Console.WriteLine("push körs");

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
                    );

                    Console.WriteLine($"Skickar: {relativePath}");

                    // Här kan du lägga till logik för att skicka varje fil till servern
                     var fileUrl = baseUrl + "/api/files" + relativePath;
                var bytes = await File.ReadAllBytesAsync(fullPath);

                var content = new ByteArrayContent(bytes);
                await client.PutAsync(fileUrl, content);
                // Här kan du lägga till logik för att skicka data till servern
                }

               
            }
            else
            {
                Console.WriteLine("ogiltigt kommando");
                Environment.Exit(1);
            }
    }
}