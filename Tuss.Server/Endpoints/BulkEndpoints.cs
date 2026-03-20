using Tuss.Server.Models;
using Tuss.Server.Services;

namespace Tuss.Server.Endpoints;

/// <summary>
/// Ruttdefinitioner för bulk-operationer (radera, flytta, ladda upp).
/// </summary>
public static class BulkEndpoints
{
    public static void MapBulkEndpoints(this IEndpointRouteBuilder app)
    {
        // Bulk-radera
        app.MapPost("/api/files/bulk-delete", (string[] paths, FileRepository files) =>
        {
            files.BulkDelete(paths);
            return Results.Ok();
        });

        // Bulk-flytta
        app.MapPost("/api/files/bulk-move", (BulkMoveRequest req, FileRepository files) =>
        {
            files.BulkMove(req.paths, req.targetFolder);
            return Results.Ok();
        });

        // Bulk-importera (upload)
        app.MapPost("/api/files/bulk-upload", async (HttpRequest request, FileRepository files) =>
        {
            if (!request.HasFormContentType)
                return Results.BadRequest("Missing form content");

            var form         = await request.ReadFormAsync();
            var targetFolder = form["targetFolder"].ToString();

            foreach (var file in form.Files)
            {
                var path = string.IsNullOrEmpty(targetFolder)
                    ? file.FileName
                    : $"{targetFolder}/{file.FileName}";

                await using var stream = file.OpenReadStream();
                await files.UpsertAsync(path, stream);
            }

            return Results.Ok();
        });
    }
}

