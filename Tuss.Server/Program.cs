using Tuss.Server.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddCors();

// Registrera databasen som en singleton – samma instans används överallt
var dbPath = Path.Combine(builder.Environment.ContentRootPath, "tuss.db");
builder.Services.AddSingleton(new DatabaseService(dbPath));

var app = builder.Build();

// Skapa tabellerna om de inte finns (körs en gång vid uppstart)
app.Services.GetRequiredService<DatabaseService>().Initialize();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseCors(x => x.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());


app.MapGet("/api/hello", () => Results.Ok(new { message = "Hej från API!" }));

// GET /api/files – hämta alla filer (utan innehåll)
app.MapGet("/api/files", (DatabaseService db) =>
{
    using var connection = db.CreateConnection();
    var command = connection.CreateCommand();
    command.CommandText = "SELECT Name, Created, Changed, IsFile, Bytes, Extension FROM Files";

    var result = new Dictionary<string, object>();

    using var reader = command.ExecuteReader();
    while (reader.Read())
    {
        var name = reader.GetString(0);
        result[name] = new
        {
            created = reader.GetString(1),
            changed = reader.GetString(2),
            file = reader.GetBoolean(3),
            bytes = reader.GetInt64(4),
            extension = reader.GetString(5)
        };
    }

    return Results.Ok(result);
});

// GET /api/files/{filename} – hämta innehållet i en fil, 404 om den inte finns
app.MapGet("/api/files/{filename}"), (string filename, DatabaseService db) =>
{
    using var connection = db.CreateConnection();
    var command = connection.CreateCommand();
    command.CommandText = "SELECT Content FROM Files WHERE Name = $name";
    command.Parameters.AddWithValue("$name", filename);

    var content = command.ExecuteScalar() as string;

    if (content == null)
        return Results.NotFound(); // Filen finns inte

    return Results.Ok(new { content });
};

// POST /api/files/{*filename} – skapa en ny fil, 409 om den redan finns
app.MapPost("/api/files/{*filename}", async (string filename, DatabaseService db, HttpContext context) =>
{
    using var reader = new StreamReader(context.Request.Body);
    var content = await reader.ReadToEndAsync();

    var now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
    var extension = Path.GetExtension(filename); // t.ex. ".md", ".txt"
    var bytes = content.Length;

    using var connection = db.CreateConnection();
    var command = connection.CreateCommand();

    // INSERT OR IGNORE lägger inte in raden om Name redan finns
    // changes() returnerar hur många rader som faktiskt ändrades
    command.CommandText = """
        INSERT OR IGNORE INTO Files (Name, Content, Created, Changed, IsFile, Bytes, Extension)
        VALUES ($name, $content, $now, $now, 1, $bytes, $extension);
        SELECT changes();
        """;
    command.Parameters.AddWithValue("$name", filename);
    command.Parameters.AddWithValue("$content", content);
    command.Parameters.AddWithValue("$now", now);
    command.Parameters.AddWithValue("$bytes", bytes);
    command.Parameters.AddWithValue("$extension", extension);

    var rowsChanged = (long)(command.ExecuteScalar() ?? 0L);

    if (rowsChanged == 0)
        return Results.Conflict(); // Filen fanns redan

    return Results.Ok();
});

// HEAD /api/files/{filename} – kolla om en fil finns (200 eller 404)
app.MapHead("/api/files/{filename}", (string filename, DatabaseService db) =>
{
    
};

// PUT /api/files/{filename} – uppdatera innehållet i en fil, 404 om den inte finns
app.MapPut("/api/files/{filename}", async (string filename, DatabaseService db, HttpContext context) =>
{
    
};

// DELETE /api/files/{filename} – ta bort en fil, 404 om den inte finns
app.MapDelete("/api/files/{filename}", (string filename, DatabaseService db) =>
{
    
});

app.Run();