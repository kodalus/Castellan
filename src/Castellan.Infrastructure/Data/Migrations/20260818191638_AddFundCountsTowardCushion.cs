using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Castellan.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddFundCountsTowardCushion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "CountsTowardCushion",
                table: "Funds",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            // Do tej pory regułę niósł rodzaj funduszu: poduszka bezpieczeństwa liczyła
            // się do Majątku, reszta nie. Teraz decyduje jawny znacznik, więc istniejące
            // poduszki muszą go dostać — inaczej po aktualizacji przestałyby się liczyć
            // po cichu, a liczba miesięcy spadłaby bez żadnego powodu widocznego dla
            // użytkownika.
            migrationBuilder.Sql(
                "UPDATE Funds SET CountsTowardCushion = 1 WHERE Kind = 'Emergency';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CountsTowardCushion",
                table: "Funds");
        }
    }
}
