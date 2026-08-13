using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Castellan.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddRawNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RawNotifications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PackageName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Text = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    PostedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ParseStatus = table.Column<int>(type: "INTEGER", nullable: false),
                    TransactionId = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RawNotifications", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RawNotifications_PackageName",
                table: "RawNotifications",
                column: "PackageName");

            migrationBuilder.CreateIndex(
                name: "IX_RawNotifications_ParseStatus",
                table: "RawNotifications",
                column: "ParseStatus");

            migrationBuilder.CreateIndex(
                name: "IX_RawNotifications_PostedAt",
                table: "RawNotifications",
                column: "PostedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RawNotifications");
        }
    }
}
