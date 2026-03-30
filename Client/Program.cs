using System.Net.Http;

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

        if (args[0] == "pull")
        {
            Console.WriteLine("pull körs");

            var content = await response.Content.ReadAsStringAsync();
            Console.WriteLine(content);
        }
    }
}