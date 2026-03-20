using Tuss.Server.Services;

namespace Tuss.Server.Endpoints;

/// <summary>
/// Ruttdefinitioner för versionshantering av filer.
/// </summary>
public static class VersionEndpoints
{
    public static void MapVersionEndpoints(this IEndpointRouteBuilder app)
    {
        // Lista alla versioner av en fil
        app.MapGet("/api/files/{filename}/versions",
            (string filename, FileRepository files, VersionRepository versions) =>
            {
                filename = Uri.UnescapeDataString(filename);
                var file = files.GetByName(filename);
                if (file is null) return Results.NotFound();

                var result = versions.GetVersions(filename).Select(v => new
                {
                    version   = v.Version,
                    createdAt = v.CreatedAt,
                    bytes     = v.Bytes,
                    isCurrent = v.Version == (file.CurrentVersion ?? 1),
                });

                return Results.Ok(result);
            });

        // Hämta en specifik version av en fil
        app.MapGet("/api/files/{filename}/versions/{version:int}",
            (string filename, int version, VersionRepository versions, FileStorageService storage) =>
            {
                filename = Uri.UnescapeDataString(filename);
                var fileVersion = versions.GetVersion(filename, version);
                if (fileVersion is null) return Results.NotFound();

                return Results.Stream(storage.OpenRead(fileVersion.DiskPath), "application/octet-stream");
            });

        // Återställ en specifik version
        app.MapPost("/api/files/{filename}/versions/{version:int}/restore",
            async (string filename, int version, FileRepository files, VersionRepository versions, FileStorageService storage) =>
            {
                filename = Uri.UnescapeDataString(filename);
                var fileVersion = versions.GetVersion(filename, version);
                if (fileVersion is null) return Results.NotFound();

                await using var stream = storage.OpenRead(fileVersion.DiskPath);
                await files.UpsertAsync(filename, stream);
                return Results.Ok();
            });
    }
}

