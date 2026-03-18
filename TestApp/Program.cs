using System.Text.Json.Serialization;
using TestApp.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
});

builder.Services.AddSingleton<FileService>();

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/health", () => "Server is running");


app.MapGet("/api/files", (FileService fileService) =>
{
    var rootListing = fileService.GetDirectoryListing("");
    return rootListing is null ? Results.NotFound() : Results.Json(rootListing);
});

app.MapGet("/api/files/{**path}", (string path, FileService fileService, HttpContext context) =>
{
    if (fileService.FileExists(path))
    {
        var metadata = fileService.GetFileMetadata(path);
        var file = fileService.GetFile(path);

        if (metadata is null || file is null)
        {
            return Results.NotFound();
        }

        context.Response.Headers["X-Created-At"] = metadata.Created;
        context.Response.Headers["X-Changed-At"] = metadata.Changed;
        context.Response.Headers["X-Type"] = metadata.File ? "file" : "directory";
        context.Response.Headers["X-Bytes"] = metadata.Bytes.ToString();
        context.Response.Headers["X-Extension"] = metadata.Extension ?? "";

        return Results.File(file.Bytes, file.ContentType);
    }

    if (fileService.DirectoryExists(path))
    {
        var directory = fileService.GetDirectoryListing(path);
        return directory is null ? Results.NotFound() : Results.Json(directory);
    }

    return Results.NotFound();
});

app.MapMethods("/api/files/{**path}", new[] { "HEAD" }, (string path, FileService fileService, HttpContext context) =>
{
    var metadata = fileService.GetFileMetadata(path);

    if (metadata is null)
    {
        return Results.NotFound();
    }

    context.Response.Headers["X-Created-At"] = metadata.Created;
    context.Response.Headers["X-Changed-At"] = metadata.Changed;
    context.Response.Headers["X-Type"] = metadata.File ? "file" : "directory";
    context.Response.Headers["X-Bytes"] = metadata.Bytes.ToString();
    context.Response.Headers["X-Extension"] = metadata.Extension ?? "";

    return Results.Ok();
});

app.MapPost("/api/files/{**path}", async (string path, HttpRequest request, FileService fileService) =>
{
    if (fileService.FileExists(path))
    {
        return Results.Conflict();
    }

    await fileService.SaveFileAsync(path, request);
    return Results.Ok();
});

app.MapPut("/api/files/{**path}", async (string path, HttpRequest request, FileService fileService) =>
{
    await fileService.SaveFileAsync(path, request);
    return Results.Ok();
});

app.MapDelete("/api/files/{**path}", (string path, FileService fileService) =>
{
    fileService.DeleteFile(path);
    return Results.Ok();
});



app.Run();