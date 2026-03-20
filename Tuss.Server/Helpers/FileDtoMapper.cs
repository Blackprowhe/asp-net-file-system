using Tuss.Server.Models;
using Tuss.Server.Services;

namespace Tuss.Server.Helpers;

/// <summary>
/// Bygger DTO-objekt för API-svar och sätter HTTP-headers.
/// Separerar presentationslogik från data-åtkomst.
/// </summary>
public static class FileDtoMapper
{
    /// <summary>Bygger svarsobjektet för en fil.</summary>
    public static object ToDto(StoredFile f) => new
    {
        created   = f.Created,
        changed   = f.Changed,
        file      = f.IsFile,
        bytes     = f.Bytes,
        extension = f.Extension,
    };

    /// <summary>Bygger nästlat träd av mappar med "content"-property.</summary>
    public static object ToFolderDto(StoredFile folder, FileRepository files)
    {
        var children = files.GetDirectChildren(folder.Name);
        var content  = new Dictionary<string, object>();
        var prefix   = folder.Name + "/";

        foreach (var child in children)
        {
            var shortName = child.Name[prefix.Length..];
            content[shortName] = child.IsFile
                ? ToDto(child)
                : ToFolderDto(child, files);
        }

        return new
        {
            created = folder.Created,
            changed = folder.Changed,
            file    = false,
            bytes   = files.GetFolderSize(folder.Name),
            content,
        };
    }

    /// <summary>Avgör om requesten har body-innehåll.</summary>
    public static bool HasBody(HttpContext ctx) =>
        ctx.Request.ContentLength is > 0
        || ctx.Request.Headers.ContentType.Count > 0;

    /// <summary>Sätter metadata-headers på svaret för en fil/mapp.</summary>
    public static void ApplyHeaders(HttpContext context, StoredFile file)
    {
        context.Response.Headers["X-Created-At"] = file.Created;
        context.Response.Headers["X-Changed-At"] = file.Changed;
        context.Response.Headers["X-Type"]       = file.IsFile ? "file" : "folder";
        context.Response.Headers["X-Bytes"]      = file.Bytes.ToString();
        context.Response.Headers["X-Extension"]  = file.Extension;
    }
}

