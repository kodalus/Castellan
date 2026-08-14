using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Castellan.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAssets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TABLE IF EXISTS \"Assets\";");
            migrationBuilder.Sql(
                "DELETE FROM \"__EFMigrationsHistory\" WHERE \"MigrationId\" LIKE '%_AddAssets';");

            migrationBuilder.CreateTable(
                name: "Assets",
                columns: table => new
                {
                    Id        = table.Column<Guid>  (type: "TEXT",    nullable: false),
                    Name      = table.Column<string>(type: "TEXT",    maxLength: 100, nullable: false),
                    Liquidity = table.Column<string>(type: "TEXT",    nullable: false),
                    Value     = table.Column<long>  (type: "INTEGER", nullable: false),
                    UpdatedOn = table.Column<string>(type: "TEXT",    nullable: false),
                    IsArchived = table.Column<bool> (type: "INTEGER", nullable: false, defaultValue: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Assets", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "Assets");
        }
    }
}
