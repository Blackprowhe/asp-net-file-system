using Tuss.Server.Endpoints;
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
builder.Services.AddSingleton<FileStorageService>();
builder.Services.AddSingleton<VersionRepository>();
builder.Services.AddSingleton<FileRepository>();

var app = builder.Build();

// Skapa tabellerna om de inte finns (körs en gång vid uppstart)
app.Services.GetRequiredService<DatabaseService>().Initialize();

// ─── Middleware ───────────────────────────────────────────────────────────────

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseCors(x => x.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());

app.UseDefaultFiles();
app.UseStaticFiles();


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

// ─── Endpoints ───────────────────────────────────────────────────────────────

app.MapBulkEndpoints();       // Måste registreras före fil-endpoints (mer specifika rutter först)
app.MapVersionEndpoints();
app.MapFileEndpoints();

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

