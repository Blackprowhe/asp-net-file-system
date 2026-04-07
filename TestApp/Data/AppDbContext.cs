namespace TestApp.Data;
using Microsoft.EntityFrameworkCore;
using TestApp.Models;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    public DbSet<FileHistory> FileHistories { get; set; }
}