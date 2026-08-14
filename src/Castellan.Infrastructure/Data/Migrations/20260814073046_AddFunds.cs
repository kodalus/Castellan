using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Castellan.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddFunds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Drop any leftover table from an earlier (removed) migration of the same name
            migrationBuilder.Sql("DROP TABLE IF EXISTS \"Funds\";");
            // Remove stale history entry so EF doesn't see the removed migration as applied
            migrationBuilder.Sql(
                "DELETE FROM \"__EFMigrationsHistory\" WHERE \"MigrationId\" LIKE '%_AddFunds';");

            migrationBuilder.CreateTable(
                name: "Funds",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Kind = table.Column<string>(type: "TEXT", nullable: false),
                    TargetAmount = table.Column<long>(type: "INTEGER", nullable: false),
                    StartMonth = table.Column<string>(type: "TEXT", nullable: false),
                    Deadline = table.Column<string>(type: "TEXT", nullable: false),
                    Balance = table.Column<long>(type: "INTEGER", nullable: false),
                    IsArchived = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Funds", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Funds");
        }
    }
}
