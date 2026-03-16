using Tuss.Server.Services;

var builder = WebApplication.CreateBuilder(args);

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
app.MapGet("/api/files/{*filename}", (string filename, FileRepository files) =>
{
    var file = files.GetByName(filename);
    if (file is null)
        return Results.NotFound();

    return Results.Text(file.Content, "text/plain");
});

// POST /api/files/{*filename} – skapa en ny fil, 409 om den redan finns
app.MapPost("/api/files/{*filename}", async (string filename, FileRepository files, HttpContext context) =>
{
    using var reader = new StreamReader(context.Request.Body);
    var content = await reader.ReadToEndAsync();

    return files.TryCreate(filename, content)
        ? Results.Ok()
        : Results.Conflict();
});

app.Run();

