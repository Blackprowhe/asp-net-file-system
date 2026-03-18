using Microsoft.AspNetCore.StaticFiles;

namespace TestApp.Helpers;

public static class ContentTypeHelper
{
    public static string GetContentType(string fileName)
    {
        var provider = new FileExtensionContentTypeProvider();

        if (!provider.TryGetContentType(fileName, out var contentType))
        {
            return "application/octet-stream";
        }

        if (contentType.StartsWith("text/") || contentType == "application/json")
        {
            return $"{contentType}; charset=UTF-8";
        }

        return contentType;
    }
}