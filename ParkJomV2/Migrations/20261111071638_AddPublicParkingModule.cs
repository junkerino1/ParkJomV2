using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ParkJomV2.Migrations
{
    /// <inheritdoc />
    public partial class AddPublicParkingModule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PublicParkingLots",
                columns: table => new
                {
                    PublicParkingLotId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OwnerId = table.Column<int>(type: "int", nullable: false),
                    PropertyId = table.Column<int>(type: "int", nullable: true),
                    LotName = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Address = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    Latitude = table.Column<decimal>(type: "decimal(9,6)", nullable: false),
                    Longitude = table.Column<decimal>(type: "decimal(9,6)", nullable: false),
                    TotalCapacity = table.Column<int>(type: "int", nullable: false),
                    OperatingType = table.Column<int>(type: "int", nullable: false),
                    OpeningTime = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    ClosingTime = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    HourlyRate = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    DailyCapRate = table.Column<decimal>(type: "decimal(10,2)", nullable: true),
                    InitialGraceMinutes = table.Column<int>(type: "int", nullable: false),
                    ExitGraceMinutes = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PublicParkingLots", x => x.PublicParkingLotId);
                    table.ForeignKey(
                        name: "FK_PublicParkingLots_Properties_PropertyId",
                        column: x => x.PropertyId,
                        principalTable: "Properties",
                        principalColumn: "PropertyId",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_PublicParkingLots_Users_OwnerId",
                        column: x => x.OwnerId,
                        principalTable: "Users",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PublicParkingSessions",
                columns: table => new
                {
                    PublicParkingSessionId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SessionCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    PublicParkingLotId = table.Column<int>(type: "int", nullable: false),
                    CarPlateNumber = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: true),
                    EntryTimeUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExitTimeUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PaidAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DurationMinutes = table.Column<int>(type: "int", nullable: false),
                    HourlyRateSnapshot = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    TotalAmount = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    PaymentMethod = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    StripeSessionId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    StripePaymentIntentId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    WalletTransactionId = table.Column<int>(type: "int", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PublicParkingSessions", x => x.PublicParkingSessionId);
                    table.ForeignKey(
                        name: "FK_PublicParkingSessions_PublicParkingLots_PublicParkingLotId",
                        column: x => x.PublicParkingLotId,
                        principalTable: "PublicParkingLots",
                        principalColumn: "PublicParkingLotId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PublicParkingSessions_Transactions_WalletTransactionId",
                        column: x => x.WalletTransactionId,
                        principalTable: "Transactions",
                        principalColumn: "TransactionId",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_PublicParkingSessions_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PublicParkingLots_IsActive",
                table: "PublicParkingLots",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_PublicParkingLots_OwnerId",
                table: "PublicParkingLots",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_PublicParkingLots_PropertyId",
                table: "PublicParkingLots",
                column: "PropertyId");

            migrationBuilder.CreateIndex(
                name: "IX_PublicParkingSessions_CarPlateNumber_Status",
                table: "PublicParkingSessions",
                columns: new[] { "CarPlateNumber", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_PublicParkingSessions_PublicParkingLotId_Status",
                table: "PublicParkingSessions",
                columns: new[] { "PublicParkingLotId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_PublicParkingSessions_SessionCode",
                table: "PublicParkingSessions",
                column: "SessionCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PublicParkingSessions_StripeSessionId",
                table: "PublicParkingSessions",
                column: "StripeSessionId");

            migrationBuilder.CreateIndex(
                name: "IX_PublicParkingSessions_UserId",
                table: "PublicParkingSessions",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_PublicParkingSessions_WalletTransactionId",
                table: "PublicParkingSessions",
                column: "WalletTransactionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PublicParkingSessions");

            migrationBuilder.DropTable(
                name: "PublicParkingLots");
        }
    }
}
