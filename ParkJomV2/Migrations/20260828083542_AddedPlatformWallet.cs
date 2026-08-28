using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ParkJomV2.Migrations
{
    /// <inheritdoc />
    public partial class AddedPlatformWallet : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Transactions_Wallets_WalletId",
                table: "Transactions");

            migrationBuilder.AlterColumn<int>(
                name: "WalletId",
                table: "Transactions",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AddColumn<int>(
                name: "PlatformWalletId",
                table: "Transactions",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PlatformWallets",
                columns: table => new
                {
                    PlatformWalletId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Balance = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    TotalRevenue = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    TotalRefunded = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlatformWallets", x => x.PlatformWalletId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_PlatformWalletId",
                table: "Transactions",
                column: "PlatformWalletId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Transactions_ExactlyOneWallet",
                table: "Transactions",
                sql: "(WalletId IS NOT NULL AND PlatformWalletId IS NULL) OR (WalletId IS NULL AND PlatformWalletId IS NOT NULL)");

            migrationBuilder.AddForeignKey(
                name: "FK_Transactions_PlatformWallets_PlatformWalletId",
                table: "Transactions",
                column: "PlatformWalletId",
                principalTable: "PlatformWallets",
                principalColumn: "PlatformWalletId");

            migrationBuilder.AddForeignKey(
                name: "FK_Transactions_Wallets_WalletId",
                table: "Transactions",
                column: "WalletId",
                principalTable: "Wallets",
                principalColumn: "WalletId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Transactions_PlatformWallets_PlatformWalletId",
                table: "Transactions");

            migrationBuilder.DropForeignKey(
                name: "FK_Transactions_Wallets_WalletId",
                table: "Transactions");

            migrationBuilder.DropTable(
                name: "PlatformWallets");

            migrationBuilder.DropIndex(
                name: "IX_Transactions_PlatformWalletId",
                table: "Transactions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Transactions_ExactlyOneWallet",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "PlatformWalletId",
                table: "Transactions");

            migrationBuilder.AlterColumn<int>(
                name: "WalletId",
                table: "Transactions",
                type: "int",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Transactions_Wallets_WalletId",
                table: "Transactions",
                column: "WalletId",
                principalTable: "Wallets",
                principalColumn: "WalletId",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
