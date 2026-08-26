using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Castellan.Infrastructure.Data.Migrations
{
    /// <summary>
    /// Pole zawsze miało wartość Immediate i nikt go nigdy nie czytał. Dublowało przy tym
    /// pojęcie, które już działa: Asset.AssetLiquidity ma cztery poziomy i jest realnie
    /// używany w poduszce, a LiquidityTier miał trzy inne — dwie niekompatybilne skale
    /// na jedną rzecz.
    ///
    /// Konto z okresem wypowiedzenia (lokata) lepiej opisać jako AKTYWO: nie wiszą na nim
    /// transakcje i nie uzgadnia się jego salda. Pole o jednej wartości kusi, żeby „skoro
    /// jest, to je wykorzystać", i tak powstają półfunkcje.
    /// </summary>
    public partial class DropAccountLiquidityTier : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LiquidityTier",
                table: "Accounts");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "LiquidityTier",
                table: "Accounts",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);
        }
    }
}
