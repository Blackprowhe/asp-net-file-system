using Microsoft.AspNetCore.StaticFiles;

namespace TestApp.Helpers;

public static class ContentTypeHelper
{
    // Hjälpklass för att bestämma korrekt Content-Type (MIME-typ) baserat på filnamn.
// Använder FileExtensionContentTypeProvider för att mappa filändelser till MIME-typer.
//
// Om filtypen inte känns igen returneras "text/plain; charset=UTF-8" som standard.
// För textbaserade typer (text/* och application/json) läggs charset=UTF-8 till
// för att säkerställa korrekt teckenkodning.

// För typer som tex bilder
 //returneras endast MIME-typen <--(vilken typ av data en fil innehåller.)
 //  tex när en server skickar något tll en webbläsare tex
 // typ Content-Type:text/html så fattar webbläsaren
 //att detta är en webbläsare.
    public static string GetContentType(string fileName)
    {
        // skapar en provider ,

        var provider = new FileExtensionContentTypeProvider();

        // försöker hitta content type
        if (!provider.TryGetContentType(fileName, out var contentType))
        {
            return "text/plain; charset=UTF-8";
        }

        if (contentType.StartsWith("text/") || contentType == "application/json")
        {
            return $"{contentType}; charset=UTF-8";
        }

        return contentType.Split(';')[0];
    }
}