using Microsoft.AspNetCore.StaticFiles;

namespace TestApp.Helpers;

public static class ContentTypeHelper
{
    public static string GetContentType(string fileName)
    {
        var provider = new FileExtensionContentTypeProvider();

        if (!provider.TryGetContentType(fileName, out var contentType))
        {
            return "text/plain; charset=UTF-8";
        }

        if (contentType.StartsWith("text/") || contentType == "application/json")
        {
            return $"{contentType}; charset=UTF-8";
        }

        return contentType;
    }
}