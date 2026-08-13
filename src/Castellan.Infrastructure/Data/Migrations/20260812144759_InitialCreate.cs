using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Castellan.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Accounts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    BankKey = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false),
                    LiquidityTier = table.Column<int>(type: "INTEGER", nullable: false),
                    LastReconciledBalance = table.Column<long>(type: "INTEGER", nullable: false),
                    LastReconciledAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    IsArchived = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Accounts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Categories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false),
                    IsSystem = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsArchived = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Categories", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MonthBudgets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Month = table.Column<string>(type: "TEXT", nullable: false),
                    AvailableFunds = table.Column<long>(type: "INTEGER", nullable: false),
                    PlannedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MonthBudgets", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Transactions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    AccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Amount = table.Column<long>(type: "INTEGER", nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CategoryId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RawMerchant = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    MerchantKey = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    Note = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    Source = table.Column<int>(type: "INTEGER", nullable: false),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false),
                    TransferGroupId = table.Column<Guid>(type: "TEXT", nullable: true),
                    SupersededById = table.Column<Guid>(type: "TEXT", nullable: true),
                    RawNotificationId = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Transactions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Transactions_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Envelopes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    MonthBudgetId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CategoryId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PlannedAmount = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Envelopes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Envelopes_MonthBudgets_MonthBudgetId",
                        column: x => x.MonthBudgetId,
                        principalTable: "MonthBudgets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Envelopes_MonthBudgetId",
                table: "Envelopes",
                column: "MonthBudgetId");

            migrationBuilder.CreateIndex(
                name: "IX_MonthBudgets_Month",
                table: "MonthBudgets",
                column: "Month",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_AccountId",
                table: "Transactions",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_CategoryId",
                table: "Transactions",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_OccurredAt",
                table: "Transactions",
                column: "OccurredAt");

            // Seed system categories
            migrationBuilder.InsertData(
                table: "Categories",
                columns: new[] { "Id", "Name", "Kind", "IsSystem", "IsArchived" },
                values: new object[,]
                {
                    { new Guid("00000000-0000-7000-8000-000000000001"), "Nieprzypisane", 0, true, false },
                    { new Guid("00000000-0000-7000-8000-000000000002"), "Nierozpoznane", 0, true, false },
                    { new Guid("00000000-0000-7000-8000-000000000003"), "Przelew", 0, true, false }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "Envelopes");
            migrationBuilder.DropTable(name: "Transactions");
            migrationBuilder.DropTable(name: "MonthBudgets");
            migrationBuilder.DropTable(name: "Categories");
            migrationBuilder.DropTable(name: "Accounts");
        }
    }
}
