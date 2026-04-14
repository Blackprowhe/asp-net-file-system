using Microsoft.EntityFrameworkCore.Migrations;
namespace TestApp.Migrations // Definierar namespace för migrations i applikationen
{
    /// <inheritdoc /> // Ärver XML-dokumentation från basklassen
    public partial class AddFileHistory : Migration // Definierar en migration som heter AddFileHistory och ärver från Migration
    {
        /// <inheritdoc /> // Ärver XML-dokumentation
        protected override void Up(MigrationBuilder migrationBuilder) // Metod som körs när migrationen appliceras (databasen uppdateras)
        {
            migrationBuilder.CreateTable( // Skapar en ny tabell i databasen
                name: "FileHistories", // Namnet på tabellen
                columns: table => new // Definierar kolumnerna i tabellen
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false) // Skapar en kolumn "Id" av typen int, ej null
                        .Annotation("Sqlite:Autoincrement", true), // Sätter autoinkrement på Id (ökar automatiskt)
                    
                    FilePath = table.Column<string>(type: "TEXT", nullable: false), // Kolumn för filens sökväg (obligatorisk)
                    
                    version = table.Column<int>(type: "INTEGER", nullable: false), // Kolumn för versionsnummer (obligatorisk)
                    
                    Content = table.Column<string>(type: "TEXT", nullable: false), // Kolumn för filens innehåll (obligatorisk)
                    
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false) // Kolumn för tidsstämpel (obligatorisk)
                },
                constraints: table => // Definierar begränsningar (constraints) för tabellen
                {
                    table.PrimaryKey("PK_FileHistories", x => x.Id); // Sätter primärnyckel på Id-kolumnen
                });
        }

        /// <inheritdoc /> // Ärver XML-dokumentation
        protected override void Down(MigrationBuilder migrationBuilder) // Metod som körs vid rollback av migrationen
        {
            migrationBuilder.DropTable( // Tar bort tabellen
                name: "FileHistories"); // Namnet på tabellen som ska tas bort
        }
    }
}