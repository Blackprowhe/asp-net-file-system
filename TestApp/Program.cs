using System.Text.Json.Serialization;
using Microsoft.AspNetCore.SignalR;
using TestApp.Services;
using TestApp.Helpers;
using TestApp.Models;
using Microsoft.EntityFrameworkCore;

// skapar builder
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite("Data Source=files.db"));

builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = long.MaxValue;
});


builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
});

builder.Services.AddSingleton<FileService>();

builder.Services.AddSignalR();


// bygger appen
var app = builder.Build();

app.MapHub<EventsHub>("/api/events/signalr");
// gör det möjligt att använda wwwroot som root för statiska filer
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/health", () => "Server is running");

// Hämtar alla filer och mappar i root och returnerar dem som JSON
app.MapGet("/api/files", (FileService fileService,IHubContext<EventsHub> hub) =>
{
    var rootListing = fileService.GetDirectoryListing("");
    return rootListing is null ? Results.NotFound() : Results.Json(rootListing);
});

app.MapGet("/api/files/{**path}", (string path, FileService fileService, HttpContext context,IHubContext<EventsHub> hub ) =>
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

app.MapMethods("/api/files/{**path}", new[] { "HEAD" }, (string path, FileService fileService, HttpContext context,IHubContext<EventsHub> hub   ) =>
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

app.MapPost("/api/files/{**path}", async (
    string path,
    HttpRequest request,
    FileService fileService,
    IHubContext<EventsHub> hub) =>
{
    if (fileService.FileExists(path))
    {
        return Results.Conflict();
    }

    await fileService.SaveFileAsync(path, request);

    await hub.Clients.All.SendAsync("Event",0, path);
     
   

    return Results.Ok();
});



app.MapPut("/api/files/{**path}", async (
    string path,
    HttpRequest request,
    FileService fileService,
    IHubContext<EventsHub> hub) =>
{
    var existed = fileService.FileExists(path);

    await fileService.SaveFileAsync(path, request);

    await hub.Clients.All.SendAsync("Event", existed ? 1 : 0, path);

    return Results.Ok();
});

app.MapDelete("/api/files/{**path}", async (
    string path,
    FileService fileService,
    IHubContext<EventsHub> hub) =>
{
    var fileExists = fileService.FileExists(path);
    var directoryExists = fileService.DirectoryExists(path);

    fileService.DeleteFile(path);

    if (fileExists)
    {
        await hub.Clients.All.SendAsync("Event", 2, path);
    }
    else if (directoryExists)
    {
        await hub.Clients.All.SendAsync("Event", 7, path);
    }

    return Results.Ok();
});



app.Run();