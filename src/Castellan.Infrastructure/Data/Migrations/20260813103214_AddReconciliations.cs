using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Castellan.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddReconciliations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Reconciliations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    AccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ObservedBalance = table.Column<long>(type: "INTEGER", nullable: false),
                    ObservedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    PreviousBalance = table.Column<long>(type: "INTEGER", nullable: false),
                    PreviousAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    RecordedDelta = table.Column<long>(type: "INTEGER", nullable: false),
                    Discrepancy = table.Column<long>(type: "INTEGER", nullable: false),
                    GeneratedTransactionId = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Reconciliations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Reconciliations_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Reconciliations_AccountId",
                table: "Reconciliations",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_Reconciliations_ObservedAt",
                table: "Reconciliations",
                column: "ObservedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Reconciliations");
        }
    }
}
