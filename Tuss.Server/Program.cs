using Tuss.Server.Services;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = 1_000_000_000; // 1 GB
});

builder.Services.AddOpenApi();
builder.Services.AddCors();


// Registrera databasen som en singleton – samma instans används överallt
var dbPath = Path.Combine(builder.Environment.ContentRootPath, "tuss.db");
builder.Services.AddSingleton(new DatabaseService(dbPath));
builder.Services.AddSingleton<FileRepository>();

var app = builder.Build();

// Skapa tabellerna om de inte finns (körs en gång vid uppstart)
app.Services.GetRequiredService<DatabaseService>().Initialize();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseCors(x => x.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());

app.UseDefaultFiles();
app.UseStaticFiles();

// ─── Helpers ─────────────────────────────────────────────────────────────────

// Bygger svarsobjektet för en fil
static object FileToDto(Tuss.Server.Models.StoredFile f) => new
{
    created   = f.Created,
    changed   = f.Changed,
    file      = f.IsFile,
    bytes     = f.Bytes,
    extension = f.Extension,
};

// Bygger nästlat träd av mappar med "content"-property som specen kräver
static object FolderToDto(Tuss.Server.Models.StoredFile folder, FileRepository files)
{
    var children = files.GetDirectChildren(folder.Name);
    var content = new Dictionary<string, object>();
    var prefix  = folder.Name + "/";
    foreach (var child in children)
    {
        var shortName = child.Name[prefix.Length..];
        content[shortName] = child.IsFile
            ? FileToDto(child)
            : FolderToDto(child, files);
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

// Avgör om requesten har body-innehåll
static bool HasBody(HttpContext ctx) =>
    ctx.Request.ContentLength is > 0
    || ctx.Request.Headers.ContentType.Count > 0;

// ─── GET /api/files ──────────────────────────────────────────────────────────

// Hämtar alla filer och mappar. Mappar på root-nivå visas med nästlat "content".
app.MapGet("/api/files", (FileRepository files) =>
{
    var all = files.GetAll();
    // Bara top-level poster (inget "/" i namn)
    var result = new Dictionary<string, object>();
    foreach (var f in all)
    {
        if (f.Name.Contains('/')) continue; // barn av en mapp, visas via content
        result[f.Name] = f.IsFile ? FileToDto(f) : FolderToDto(f, files);
    }
    return Results.Ok(result);
});

// ─── GET /api/files/{*path} ──────────────────────────────────────────────────

// Om det är en fil → returnera innehållet.
// Om det är en mapp → returnera direkta barn som JSON.
app.MapGet("/api/files/{*path}", (string path, FileRepository files, HttpContext context) =>
{
    var entry = files.GetByName(path);
    if (entry is null) return Results.NotFound();

    if (entry.IsFile)
    {
        FileRepository.ApplyHeaders(context, entry);
        return Results.Stream(files.OpenRead(entry), "application/octet-stream");
    }

    // Mapp → returnera direkta barn med korta namn
    var children = files.GetDirectChildren(path);
    var result = new Dictionary<string, object>();
    var prefix = path + "/";
    foreach (var child in children)
    {
        var shortName = child.Name[prefix.Length..];
        result[shortName] = child.IsFile
            ? FileToDto(child)
            : FolderToDto(child, files);
    }
    return Results.Ok(result);
});

// ─── POST /api/files/{*path} ─────────────────────────────────────────────────

// Utan body → skapa mapp (409 om den redan finns).
// Med body  → skapa fil (409 om den redan finns).
app.MapPost("/api/files/{*path}", async (string path, FileRepository files, HttpContext context) =>
{
    if (!HasBody(context))
    {
        var created = files.CreateFolder(path);
        return created ? Results.Ok() : Results.Conflict();
    }

    var fileCreated = await files.TryCreateAsync(path, context.Request.Body);
    return fileCreated ? Results.Ok() : Results.Conflict();
});

// ─── PUT /api/files/{*path} ──────────────────────────────────────────────────

// Utan body → skapa/uppdatera mapp.
// Med body  → skapa/uppdatera fil (ny version).
app.MapPut("/api/files/{*path}", async (string path, FileRepository files, HttpContext context) =>
{
    if (!HasBody(context))
    {
        files.UpsertFolder(path);
        return Results.Ok();
    }

    await files.UpsertAsync(path, context.Request.Body);
    return Results.Ok();
});

// ─── HEAD /api/files/{*path} ─────────────────────────────────────────────────

app.MapMethods("/api/files/{*path}", ["HEAD"], (string path, FileRepository files, HttpContext context) =>
{
    var entry = files.GetByName(path);
    if (entry is null) return Results.NotFound();
    FileRepository.ApplyHeaders(context, entry);
    return Results.Ok();
});

// ─── Delete / api/files/{*path} ───────────────────────────────────────────────

// Tar bort fil eller mapp (och allt under den). Alltid 200.
app.MapDelete("/api/files/{*path}", (string path, FileRepository files) =>
{
    files.DeleteEntry(path);
    return Results.Ok();
});

// ─── Move & Bulk ─────────────────────────────────────────────────────────────

// Flyttar en fil eller mapp.
app.MapMethods("/api/files/{*path}", ["PATCH"], (string path, string newPath, FileRepository files) =>
{
    path = Uri.UnescapeDataString(path);
    newPath = Uri.UnescapeDataString(newPath);
    files.MoveEntry(path, newPath);
    return Results.Ok();
});

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
    if (!request.HasFormContentType) return Results.BadRequest("Missing form content");
    var form = await request.ReadFormAsync();
    var targetFolder = form["targetFolder"].ToString();

    foreach (var file in form.Files)
    {
        var path = string.IsNullOrEmpty(targetFolder) ? file.FileName : $"{targetFolder}/{file.FileName}";
        await using var stream = file.OpenReadStream();
        await files.UpsertAsync(path, stream);
    }
    return Results.Ok();
});

// ─── Versioner ───────────────────────────────────────────────────────────────


app.MapGet("/api/files/{filename}/versions", (string filename, FileRepository files) =>
{
    filename = Uri.UnescapeDataString(filename);
    var file = files.GetByName(filename);
    if (file is null) return Results.NotFound();

    var versions = files.GetVersions(filename).Select(v => new
    {
        version   = v.Version,
        createdAt = v.CreatedAt,
        bytes     = v.Bytes,
        isCurrent = v.Version == (file.CurrentVersion ?? 1),
    });
    return Results.Ok(versions);
});

app.MapGet("/api/files/{filename}/versions/{version:int}", (string filename, int version, FileRepository files) =>
{
    filename = Uri.UnescapeDataString(filename);
    var fileVersion = files.GetVersion(filename, version);
    if (fileVersion is null) return Results.NotFound();
    return Results.Stream(File.OpenRead(fileVersion.DiskPath), "application/octet-stream");
});

app.MapPost("/api/files/{filename}/versions/{version:int}/restore", async (string filename, int version, FileRepository files) =>
{
    filename = Uri.UnescapeDataString(filename);
    var fileVersion = files.GetVersion(filename, version);
    if (fileVersion is null) return Results.NotFound();
    await files.RestoreVersionAsync(filename, version);
    return Results.Ok();
});

// ─── SPA fallback ────────────────────────────────────────────────────────────

app.MapFallback(async context =>
{
    if (context.Request.Path.StartsWithSegments("/api"))
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        return;
    }

    context.Response.ContentType = "text/html; charset=utf-8";
    await context.Response.SendFileAsync(Path.Combine(app.Environment.WebRootPath, "index.html"));
});

app.Run();

public record BulkMoveRequest(string[] paths, string targetFolder);

