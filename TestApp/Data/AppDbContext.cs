namespace TestApp.Data;
using Microsoft.EntityFrameworkCore;
using TestApp.Models;


// // Databaskontext för programmet.
// Hanterar anslutning till databasen som innehåller tabeller (DbSets),
// t.ex. FileHistories som representerar lagrad filhistorik.
public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    public DbSet<FileHistory> FileHistories { get; set; }
}