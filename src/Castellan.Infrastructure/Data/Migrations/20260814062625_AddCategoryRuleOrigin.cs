using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Castellan.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCategoryRuleOrigin : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CategoryRules_Priority",
                table: "CategoryRules");

            migrationBuilder.DropColumn(
                name: "Priority",
                table: "CategoryRules");

            migrationBuilder.AddColumn<int>(
                name: "HitCount",
                table: "CategoryRules",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "LastUsedAt",
                table: "CategoryRules",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Origin",
                table: "CategoryRules",
                type: "TEXT",
                nullable: false,
                defaultValue: "Manual");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "HitCount",
                table: "CategoryRules");

            migrationBuilder.DropColumn(
                name: "LastUsedAt",
                table: "CategoryRules");

            migrationBuilder.DropColumn(
                name: "Origin",
                table: "CategoryRules");

            migrationBuilder.AddColumn<int>(
                name: "Priority",
                table: "CategoryRules",
                type: "INTEGER",
                nullable: false,
                defaultValue: 100);

            migrationBuilder.CreateIndex(
                name: "IX_CategoryRules_Priority",
                table: "CategoryRules",
                column: "Priority");
        }
    }
}
