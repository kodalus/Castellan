using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Castellan.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddProposedTransferGroupId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ProposedTransferGroupId",
                table: "Transactions",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ProposedTransferGroupId",
                table: "Transactions");
        }
    }
}
