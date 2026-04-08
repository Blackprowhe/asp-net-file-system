namespace TestApp.Data;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

// // Factory-klass som används av Entity Framework Core vid design-time (t.ex. migrationer).
// Den skapar en instans av AppDbContext utan att applikationen behöver startas.
// EF Core anropar CreateDbContext automatiskt för att få rätt databasinställningar,
// här konfigurerad att använda en SQLite-databas (files.db).
public class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    // “Använd den här klassen för att skapa DbContext
    //  vid design-time (t.ex. migrationer)

    // denna metod anropar entity automatiskt.
    public AppDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();

        optionsBuilder.UseSqlite("Data Source=files.db");

        return new AppDbContext(optionsBuilder.Options);
    }
}