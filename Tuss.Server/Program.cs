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

app.Run();