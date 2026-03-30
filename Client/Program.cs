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

        try
        {
            var client = new HttpClient();
            var response = await client.GetAsync(url);

            // 3. Om servern svarar men inte är okej
            if (!response.IsSuccessStatusCode)
            {
                Environment.Exit(1);
            }
        }
        catch
        {
            // 4. Om request kraschar (ogiltig URL / ingen server)
            Environment.Exit(1);
        }
    }
}