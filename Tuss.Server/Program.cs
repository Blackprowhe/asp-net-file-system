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


