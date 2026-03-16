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

// GET /api/files – hämta alla filer (utan innehåll)
app.MapGet("/api/files", (FileRepository files) =>
{
    var result = files.GetAll().ToDictionary(
        f => f.Name,
        f => (object)new
        {
            created   = f.Created,
            changed   = f.Changed,
            file      = f.IsFile,
            bytes     = f.Bytes,
            extension = f.Extension,
        }
    );
    return Results.Ok(result);
});

// GET /api/files/{*filename} – hämta innehållet i en fil, 404 om den inte finns
app.MapGet("/api/files/{*filename}", (string filename, FileRepository files, HttpContext context) =>
{
    var file = files.GetByName(filename);
    if (file is null)
        return Results.NotFound();

    FileRepository.ApplyHeaders(context, file);
    return Results.Stream(files.OpenRead(file), "text/plain");
});

// POST /api/files/{*filename} – skapa en ny fil, 409 om den redan finns
app.MapPost("/api/files/{*filename}", async (string filename, FileRepository files, HttpContext context) =>
{
    var created = await files.TryCreateAsync(filename, context.Request.Body);
    return created ? Results.Ok() : Results.Conflict();
});

// HEAD /api/files/{*filename} – hämta metadata-headers utan body
app.MapMethods("/api/files/{*filename}", ["HEAD"], (string filename, FileRepository files, HttpContext context) =>
{
    var file = files.GetByName(filename);
    if (file is null)
        return Results.NotFound();

    FileRepository.ApplyHeaders(context, file);
    return Results.Ok();
});

// DELETE /api/files/{*filename} – ta bort en fil, alltid 200
app.MapDelete("/api/files/{*filename}", (string filename, FileRepository files) =>
{
    files.Delete(filename);
    return Results.Ok();
});

// PUT /api/files/{*filename} – skapa eller ersätt en fil, alltid 200
app.MapPut("/api/files/{*filename}", async (string filename, FileRepository files, HttpContext context) =>
{
    await files.UpsertAsync(filename, context.Request.Body);
    return Results.Ok();
});

app.Run();

