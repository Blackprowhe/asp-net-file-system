using System.Text.Json.Serialization;
using Microsoft.AspNetCore.SignalR;
using TestApp.Services;
using TestApp.Helpers;
using TestApp.Models;
using TestApp.Data;
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

builder.Services.AddScoped<FileService>();

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

// hämtar fil eller mapp baserat på sökväg 
// Hämtar en fil eller mapp baserat på angiven sökväg.
// Om sökvägen motsvarar en fil returneras filens innehåll (bytes)
// tillsammans med metadata via HTTP-headers (t.ex. skapad datum, storlek, typ).

app.MapGet("/api/files/{**path}", (string path, FileService fileService, HttpContext context,IHubContext<EventsHub> hub ) =>
{
    if (fileService.FileExists(path))
    {
        // info om filen

        var metadata = fileService.GetFileMetadata(path);
        var file = fileService.GetFile(path);

        if (metadata is null || file is null)
        {
            return Results.NotFound();
        }

        // lägger till metadata i headers

        context.Response.Headers["X-Created-At"] = metadata.Created;
        context.Response.Headers["X-Changed-At"] = metadata.Changed;
        context.Response.Headers["X-Type"] = metadata.File ? "file" : "directory";
        context.Response.Headers["X-Bytes"] = metadata.Bytes.ToString();
        context.Response.Headers["X-Extension"] = metadata.Extension ?? "";

        return Results.File(file.Bytes, file.ContentType);
    }
    
    // om det inte är en fil, kollar vi om det är en mapp
    //och hämtar innehållet.
    if (fileService.DirectoryExists(path))
    {
        var directory = fileService.GetDirectoryListing(path);
        return directory is null ? Results.NotFound() : Results.Json(directory);
    }

    return Results.NotFound();
});

// hämtar alla tididgare versioner av en fil
//returerar som json.
// Hämtar versionshistorik för en specifik fil baserat på sökväg.
// Söker i databasen efter alla poster i FileHistories som matchar filens path,
// sorterar dem i versionsordning och returnerar resultatet som JSON.

app.MapGet("/api/files/history/{**path}", async (string path, AppDbContext context) =>
{

    // hämtar data från databasen via db context och ef

    var history = await context.FileHistories

    // hämtar endast historik för den fil  du står i

        .Where(f => f.FilePath == path)

        // sorterar på version
        .OrderBy(f => f.Version)

        // kör databas anropet

        .ToListAsync();

    return Results.Ok(history);
});

// Hanterar HEAD-förfrågningar för en fil eller mapp
// baserat på sökväg.
// Returnerar endast metadata i HTTP-headers
// utan att skicka filinnehåll.
app.MapMethods("/api/files/{**path}", new[] { "HEAD" }, (string path, FileService fileService, HttpContext context,IHubContext<EventsHub> hub   ) =>
{
    // hämta meta data om filen eller mappen

    var metadata = fileService.GetFileMetadata(path);

    if (metadata is null)
    {
        return Results.NotFound();
    }
    
    // sätter metadata i headers

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
    // kolla om fil finns

    if (fileService.FileExists(path))
    {
        return Results.Conflict();
    }

    await fileService.SaveFileAsync(path, request);

    // skickar en realtidsuppdatering via SignalR till 
    // alla anslutna klienter
    await hub.Clients.All.SendAsync("Event",0, path);
     
   

    return Results.Ok();
});

// Skapar en ny fil baserat på angiven sökväg.
// Kontrollerar först om filen redan finns och returnerar i så fall 409 Conflict.
// Läser innehållet från HTTP-requesten och sparar filen via FileService.
// Efter att filen skapats skickas ett realtidsevent via SignalR till alla klienter,
// så att UI kan uppdateras automatiskt.
// Används både för att skapa tomma filer och ladda upp innehåll.


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

// tar emot path och services

app.MapDelete("/api/files/{**path}", async (
    string path,
    FileService fileService,
    IHubContext<EventsHub> hub) =>
{
    // sparar om det är en fil eller mapp
    //NOTERA detta görs innan delete

    var fileExists = fileService.FileExists(path);
    var directoryExists = fileService.DirectoryExists(path);

    fileService.DeleteFile(path);

    if (fileExists)
    {
        // 2 = filborttagen
        await hub.Clients.All.SendAsync("Event", 2, path);
    }
    else if (directoryExists)
    {
        // 7 = mappborttagen
        await hub.Clients.All.SendAsync("Event", 7, path);
    }

    return Results.Ok();
});



app.Run();