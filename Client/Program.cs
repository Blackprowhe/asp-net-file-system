using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

// Detta är en klient som kan synka filer mellan en dator och servern.
// programmet har två huvud kommandon pull och push,
// pull hämtar alla filer från servern och sparar lokalt, 
// och push gör tvärtom, den tar alla filer som finns lokalt och laddar upp till servern,
// och tar bort filer från servern som inte finns lokalt.
//Tanken är att det ska fungera som en enkel versionshantering 
//eller backup, där man alltid kan hålla filer
// synkade mellan lokalt och servern.


// dotnet run push localhost:5137
//dotnet run pull localhost:5137
//dotnet run pull localhost:5137 username password
//dotnet run push localhost:5137 username password
public class Program
{
    public static async Task<int> Main(string[] args)
    {
        //om argument större än 2 ingen url finns eller ogiltigt kommando
        if (args.Length < 2)
        {
            Console.WriteLine("ingen url hittades");
            return 1;
        }

        // koden hittar args[0] och args[1] och kollar så att args[0] är pull eller push, och att args[1] är en url, annars skrivs felmeddelande ut

        var command = args[0];
        var baseUrl = args[1];

        if (command is not ("pull" or "push"))
        {
            Console.WriteLine("ogiltigt kommando");
            return 1;
        }


        // koden kollar så att inget http eller https finns i url:en, och lägger till http om localhost finns i url:en


        if (!baseUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            && !baseUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            baseUrl = baseUrl.Contains("localhost", StringComparison.OrdinalIgnoreCase)
                ? "http://" + baseUrl
                : "https://" + baseUrl;
        }
        //trimmar ner onödiga snedstreck i slutet av url:en
        baseUrl = baseUrl.TrimEnd('/');

        using var client = new HttpClient();
        // login sker om args är fyra eller mindre än fyra, och args[2] och args[3] används som username och password,
        //  och skickas i en json body till /api/login, om något går fel returneras 1
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
        //koden hämtar directoryt och sparar i root
        var root = Directory.GetCurrentDirectory();

        // koden hämtar listan av filer och mappar från servern genom att göra en GET request till /api/files,
        //  och sparar svaret i listResponse, om något går fel returneras 1
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
        // koden läser svaret från servern som en string och sparar i body, om något går fel returneras 1
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
        //om kommandot är pull så hämtas alla filer från servern och sparas lokalt,
        //  och alla filer som inte finns på servern tas bort lokalt
        if (command == "pull")
        {

            // koden samlar alla filvägar från serverns listing i en HashSet serverFiles,
            //  och använder CollectFilePaths för att traversera listingens struktur
            var serverFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            CollectFilePaths(rootListing, "", serverFiles);

            // koden loopar igenom alla filvägar i serverFiles, och för varje filväg görs en GET request till /api/files/{filväg} för att hämta filens innehåll,
            //  och sparar filen lokalt under samma relativa väg, om något går fel returneras 1,
            //  och om filen inte finns på servern fortsätter loopen
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
                // om filen inte finns på servern fortsätter loopen
                if (!fileResponse.IsSuccessStatusCode)
                    continue;
                // koden läser filens innehåll som en byte array och sparar den lokalt
                //  under samma relativa väg, om något går fel returneras 1
                var bytes = await fileResponse.Content.ReadAsByteArrayAsync();
                var dest = SafePathUnderRoot(root, relPath);
                if (dest is null)
                    continue;
                // hämta mappens sökväg och skapa mappen om den inte finns, innan filen sparas
                //kontrollerar att sökvägen finns eller iallafall inte är null,
                //  och skapar mappen om den inte finns, innan filen sparas
                //spara mappen asyncront och kolla så att det inte blir några fel,
                //  om något går fel returneras 1
                var parent = Path.GetDirectoryName(dest);
                if (!string.IsNullOrEmpty(parent))
                    Directory.CreateDirectory(parent);

                await File.WriteAllBytesAsync(dest, bytes);
            }
            // loopar igenom alla filer som finns lokalt undermappar root, och kollar om de finns i serverFiles,
            //  och om de inte finns så tas de bort lokalt
            foreach (var fullPath in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
            {
                var rel = NormalizeRelativePath(Path.GetRelativePath(root, fullPath));
                if (string.IsNullOrEmpty(rel) || rel.Contains("..", StringComparison.Ordinal))
                    continue;

                if (!serverFiles.Contains(rel))
                    File.Delete(fullPath);
            }

            // tar bort alla tomma mappar lokalt efter att filer tagits bort,
            //  och fortsätter ta bort tomma mappar tills inga fler finns
            PruneEmptyDirectories(root);
        }
        else
        {   // skapar en hashset för serverfiler och ignoerar stora och små bokstäver
            // och går igenom serverns struktur och fyller server files med rtelativa filvägar


            var serverFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            CollectFilePaths(rootListing, "", serverFiles);



            // samma sak här fast med lokala filer och fyller localFiles med relativa filvägar

            var localFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var fullPath in Directory.GetFiles(root, "*", SearchOption.AllDirectories))

            {   // denna koden gör sökvägen säker och relativ och kollar så att den inte är tom eller innehåller "..", 
                // och om den är det så fortsätter loopen, annars läggs den till i localFiles
                var rel = NormalizeRelativePath(Path.GetRelativePath(root, fullPath));
                if (string.IsNullOrEmpty(rel) || rel.Contains("..", StringComparison.Ordinal))
                    continue;
                localFiles.Add(rel);
            }
            // loopar igenom alla filvägar i serverFiles, 
            // och hoppar över de som också finns i localFiles,
            //  och för de som inte finns i localFiles
            //  görs en DELETE request till /api/files/{filväg}
            //  för att ta bort filen från servern, om något går
            //  fel returneras 1
            foreach (var relPath in serverFiles)


            {   // om filen finns både på servern och lokalt så fortsätter loopen, annars görs en DELETE request till /api/files/{filväg}
                //  för att ta bort filen från servern, om något går fel returneras 1
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
            {   // Den här rackaren är en retry loop som försöker ta bort alla filer från servern upp till 32 gånger,
                //  eller tills inga filer finns kvar på servern,
                for (var i = 0; i < 32; i++)
                {
                    // gör en GET request till /api/files för att hämta den uppdaterade
                    //  listan av filer på servern, och sparar svaret, om något går fel
                    //  returneras 1
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

                    // Läser JSON-svar från servern och konverterar till en dictionary.
                    // Om svaret är tomt ("", "{}") används en tom dictionary för att undvika fel.
                    // Annars deserialiseras JSON till Dictionary<string, JsonElement>.
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


                    // loopar igenom alla filer som finns på servern och gör en Delete Request
                    // för att ta bort filer som inte ska finnas 
                    // och bygger url säkert
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

            // loopar igenom alla lokala filer.
            foreach (var relPath in localFiles)
            {
                // gör sökvägen säker och relativ och kollar så att den inte är null.
                // Och skippar ogiltiga paths och filer som inte finns.
                var localPath = SafePathUnderRoot(root, relPath);
                if (localPath is null || !File.Exists(localPath))
                    continue;


                // bygger url säkert till servern och gör ett korekt api anrop
                //läser hela filens innehåll som byte array och säger till servern
                // att denna datan här är Raw...
                // varför inte bara string? tänker ni säkert då XD detta är för att bytes funkar
                // på allt eftersom allt i datavärlden är bytes, broken som fan XDD
                var url = baseUrl + "/api/files/" + Uri.EscapeDataString(relPath.Replace('\\', '/'));
                var bytes = await File.ReadAllBytesAsync(localPath);
                using var putContent = new ByteArrayContent(bytes);
                putContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

                // Skickar PUT-request till servern för att ladda upp filen.
                // Om requesten misslyckas (fel statuskod eller exception)
                // avslutas programmet med returnkod 1.
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

    // // Normaliserar sökvägar så att alla använder '/' istället för '\'
    // och tar bort ledande snedstreck
    static string NormalizeRelativePath(string relative) =>
        relative.Replace('\\', '/').TrimStart('/');


    // // Säkerställer att en relativ sökväg inte kan lämna root-mappen.
    // Blockerar ".." och verifierar att den slutliga sökvägen ligger inom root.
    // Returnerar null om sökvägen är ogiltig eller osäker.
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

    // går igenom alla filer och mappar i serverns JSON och samlar alla filvägar i en lista.
    // används för att veta vilka filer som finns på servern
    //jämföra med lokala filer
    // synca push och pull
    static void CollectFilePaths(
        Dictionary<string, JsonElement> nodes,
        string prefix,
        HashSet<string> filePaths)
    {
        foreach (var (name, el) in nodes)
        {
            var fullPath = string.IsNullOrEmpty(prefix) ? name : prefix + "/" + name;

            // koden kollar om det är en fil.
            if (!el.TryGetProperty("file", out var fileProp))
                continue;
            // om det är en fil lägg till i listan 
            if (fileProp.GetBoolean())
            {
                filePaths.Add(fullPath);
                continue;
            }
            // och om det är en mapp så har den Coontent.
            if (!el.TryGetProperty("content", out var contentProp))
                continue;
            // koden försöker deserialisera content till en dictionary,
            //  och om det inte går så fortsätter loopen,
            Dictionary<string, JsonElement>? sub;
            try
            {
                sub = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(contentProp.GetRawText());
            }
            catch
            {
                continue;
            }
            // funktionen anropar sig själv
            // går ini mappar och mappar i mappar osv XD
            if (sub is { Count: > 0 })
                CollectFilePaths(sub, fullPath, filePaths);
        }
    }


    // // Rensar bort tomma mappar i hela katalogträdet.
    // Kör i flera varv eftersom borttagning av en mapp kan göra dess förälder tom.
    // Mappar sorteras så att de djupaste tas bort först.

    // (LETADE EFTER DEN HÄR I 5 DAGAR...)
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
