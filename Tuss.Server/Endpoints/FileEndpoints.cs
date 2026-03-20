using Tuss.Server.Helpers;
using Tuss.Server.Services;

namespace Tuss.Server.Endpoints;

/// <summary>
/// Ruttdefinitioner för CRUD-operationer på filer och mappar.
/// </summary>
public static class FileEndpoints
{
    public static void MapFileEndpoints(this IEndpointRouteBuilder app)
    {
        // GET /api/files – alla filer och mappar (top-level med nästlat innehåll)
        app.MapGet("/api/files", (FileRepository files) =>
        {
            var all    = files.GetAll();
            var result = new Dictionary<string, object>();

            foreach (var f in all)
            {
                if (f.Name.Contains('/')) continue;
                result[f.Name] = f.IsFile
                    ? FileDtoMapper.ToDto(f)
                    : FileDtoMapper.ToFolderDto(f, files);
            }

            return Results.Ok(result);
        });

        // GET /api/files/{*path} – hämta fil (innehåll) eller mapp (JSON)
        app.MapGet("/api/files/{*path}", (string path, FileRepository files, HttpContext context) =>
        {
            var entry = files.GetByName(path);
            if (entry is null) return Results.NotFound();

            if (entry.IsFile)
            {
                FileDtoMapper.ApplyHeaders(context, entry);
                return Results.Stream(files.OpenRead(entry), "application/octet-stream");
            }

            var children = files.GetDirectChildren(path);
            var result   = new Dictionary<string, object>();
            var prefix   = path + "/";

            foreach (var child in children)
            {
                var shortName = child.Name[prefix.Length..];
                result[shortName] = child.IsFile
                    ? FileDtoMapper.ToDto(child)
                    : FileDtoMapper.ToFolderDto(child, files);
            }

            return Results.Ok(result);
        });

        // POST /api/files/{*path} – skapa fil (med body) eller mapp (utan body)
        app.MapPost("/api/files/{*path}", async (string path, FileRepository files, HttpContext context) =>
        {
            if (!FileDtoMapper.HasBody(context))
            {
                var created = files.CreateFolder(path);
                return created ? Results.Ok() : Results.Conflict();
            }

            var fileCreated = await files.TryCreateAsync(path, context.Request.Body);
            return fileCreated ? Results.Ok() : Results.Conflict();
        });

        // PUT /api/files/{*path} – skapa/uppdatera fil eller mapp
        app.MapPut("/api/files/{*path}", async (string path, FileRepository files, HttpContext context) =>
        {
            if (!FileDtoMapper.HasBody(context))
            {
                files.UpsertFolder(path);
                return Results.Ok();
            }

            await files.UpsertAsync(path, context.Request.Body);
            return Results.Ok();
        });

        // HEAD /api/files/{*path}
        app.MapMethods("/api/files/{*path}", ["HEAD"], (string path, FileRepository files, HttpContext context) =>
        {
            var entry = files.GetByName(path);
            if (entry is null) return Results.NotFound();
            FileDtoMapper.ApplyHeaders(context, entry);
            return Results.Ok();
        });

        // DELETE /api/files/{*path}
        app.MapDelete("/api/files/{*path}", (string path, FileRepository files) =>
        {
            files.DeleteEntry(path);
            return Results.Ok();
        });

        // PATCH /api/files/{*path} – flytta fil/mapp
        app.MapMethods("/api/files/{*path}", ["PATCH"], (string path, string newPath, FileRepository files) =>
        {
            path    = Uri.UnescapeDataString(path);
            newPath = Uri.UnescapeDataString(newPath);
            files.MoveEntry(path, newPath);
            return Results.Ok();
        });
    }
}

