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

// ─── Filer ───────────────────────────────────────────────────────────────────

// GET /api/files – lista alla filer och mappar
app.MapGet("/api/files", (FileRepository files) =>
{
    var result = files.GetAll().ToDictionary(
        f => f.Name,
        f => (object)new
        {
            created        = f.Created,
            changed        = f.Changed,
            file           = f.IsFile,
            bytes          = f.Bytes,
            extension      = f.Extension,
            currentVersion = f.CurrentVersion ?? 1,
        }
    );
    return Results.Ok(result);
});

// GET /api/files/{*filename} – hämta innehållet i aktiv version
app.MapGet("/api/files/{*filename}", (string filename, FileRepository files, HttpContext context) =>
{
    var file = files.GetByName(filename);
    if (file is null) return Results.NotFound();
    if (!file.IsFile) return Results.BadRequest("Det är en mapp, inte en fil.");

    FileRepository.ApplyHeaders(context, file);
    return Results.Stream(files.OpenRead(file), "application/octet-stream");
});

// POST /api/files/{*filename} – skapa ny fil (version 1), 409 om den redan finns
app.MapPost("/api/files/{*filename}", async (string filename, FileRepository files, HttpContext context) =>
{
    var created = await files.TryCreateAsync(filename, context.Request.Body);
    return created ? Results.Ok() : Results.Conflict();
});

// PUT /api/files/{*filename} – ladda upp ny version (skapar filen om den inte finns)
app.MapPut("/api/files/{*filename}", async (string filename, FileRepository files, HttpContext context) =>
{
    await files.UpsertAsync(filename, context.Request.Body);
    return Results.Ok();
});

// HEAD /api/files/{*filename} – metadata-headers utan body
app.MapMethods("/api/files/{*filename}", ["HEAD"], (string filename, FileRepository files, HttpContext context) =>
{
    var file = files.GetByName(filename);
    if (file is null) return Results.NotFound();
    FileRepository.ApplyHeaders(context, file);
    return Results.Ok();
});

// DELETE /api/files/{*filename} – ta bort fil och alla versioner
app.MapDelete("/api/files/{*filename}", (string filename, FileRepository files) =>
{
    files.Delete(filename);
    return Results.Ok();
});

// ─── Versioner ────────────────────────────────────────────────────────────────

// GET /api/files/{filename}/versions – lista alla versioner för en fil
// OBS: {filename} är URL-enkodad, t.ex. "projekt%2FREADME.md" för "projekt/README.md"
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

// GET /api/files/{filename}/versions/{version} – hämta en specifik version
app.MapGet("/api/files/{filename}/versions/{version:int}", (string filename, int version, FileRepository files) =>
{
    filename = Uri.UnescapeDataString(filename);
    var fileVersion = files.GetVersion(filename, version);
    if (fileVersion is null) return Results.NotFound();
    return Results.Stream(File.OpenRead(fileVersion.DiskPath), "application/octet-stream");
});

// POST /api/files/{filename}/versions/{version}/restore – återställ till en version
app.MapPost("/api/files/{filename}/versions/{version:int}/restore", async (string filename, int version, FileRepository files) =>
{
    filename = Uri.UnescapeDataString(filename);
    var fileVersion = files.GetVersion(filename, version);
    if (fileVersion is null) return Results.NotFound();
    await files.RestoreVersionAsync(filename, version);
    return Results.Ok();
});

// ─── Mappar ───────────────────────────────────────────────────────────────────

// POST /api/folders/{*path} – skapa mapp (och föräldersmappar), 409 om den finns
app.MapPost("/api/folders/{*path}", (string path, FileRepository files) =>
{
    var created = files.CreateFolder(path);
    return created ? Results.Ok() : Results.Conflict();
});

// DELETE /api/folders/{*path} – ta bort mapp och allt innehåll
app.MapDelete("/api/folders/{*path}", (string path, FileRepository files) =>
{
    files.DeleteFolder(path);
    return Results.Ok();
});

// GET /api/folders/{*path} – lista innehållet i en specifik mapp
app.MapGet("/api/folders/{*path}", (string path, FileRepository files) =>
{
    var folder = files.GetByName(path.Trim('/') + "/");
    if (folder is null) return Results.NotFound();

    var contents = files.GetByFolder(path).ToDictionary(
        f => f.Name,
        f => (object)new
        {
            created        = f.Created,
            changed        = f.Changed,
            file           = f.IsFile,
            bytes          = f.Bytes,
            extension      = f.Extension,
            currentVersion = f.CurrentVersion ?? 1,
        }
    );
    return Results.Ok(contents);
});

// SPA-fallback: skicka index.html för Angular-klientrutter, men inte för /api.
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

