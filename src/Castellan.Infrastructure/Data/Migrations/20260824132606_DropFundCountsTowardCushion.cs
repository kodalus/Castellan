using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Castellan.Infrastructure.Data.Migrations
{
    /// <summary>
    /// Znacznik „licz do poduszki” znika razem z samą możliwością doliczania funduszy.
    /// Od czasu, gdy do poduszki wchodzą salda wszystkich kont, zaznaczony fundusz
    /// liczył tę samą złotówkę drugi raz — a dla poduszki bezpieczeństwa działo się to
    /// domyślnie. Pieniądze poza kontami znanymi aplikacji dodaje się jako aktywo.
    /// </summary>
    public partial class DropFundCountsTowardCushion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CountsTowardCushion",
                table: "Funds");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "CountsTowardCushion",
                table: "Funds",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }
    }
}
