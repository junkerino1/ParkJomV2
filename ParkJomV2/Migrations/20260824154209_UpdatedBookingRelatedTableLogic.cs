using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ParkJomV2.Migrations
{
    /// <inheritdoc />
    public partial class UpdatedBookingRelatedTableLogic : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Bookings_RenterId",
                table: "Bookings");

            migrationBuilder.AddColumn<DateTime>(
                name: "ConfiguredAt",
                table: "ParkingSpots",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Description",
                table: "ParkingSpots",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsConfigurationComplete",
                table: "ParkingSpots",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "ActualExitAt",
                table: "Bookings",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "BookedDays",
                table: "Bookings",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "BookingQuoteId",
                table: "Bookings",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CheckedInAt",
                table: "Bookings",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IdempotencyKey",
                table: "Bookings",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OverstayHours",
                table: "Bookings",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "OverstayPenaltyAmount",
                table: "Bookings",
                type: "decimal(10,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "OwnerPayoutAmount",
                table: "Bookings",
                type: "decimal(10,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "PlatformCommissionAmount",
                table: "Bookings",
                type: "decimal(10,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "PlatformCommissionRate",
                table: "Bookings",
                type: "decimal(5,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "RatePerDaySnapshot",
                table: "Bookings",
                type: "decimal(10,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "RateType",
                table: "Bookings",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<decimal>(
                name: "RefundAmount",
                table: "Bookings",
                type: "decimal(10,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "RentalSubtotal",
                table: "Bookings",
                type: "decimal(10,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "BookingQuotes",
                columns: table => new
                {
                    BookingQuoteId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RenterId = table.Column<int>(type: "int", nullable: false),
                    ParkingSpotId = table.Column<int>(type: "int", nullable: false),
                    VehicleId = table.Column<int>(type: "int", nullable: false),
                    StartDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EndDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    BookedDays = table.Column<int>(type: "int", nullable: false),
                    RateType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RatePerDay = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    RentalSubtotal = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    PlatformCommissionRate = table.Column<decimal>(type: "decimal(5,2)", nullable: false),
                    PlatformCommissionAmount = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    OwnerPayoutAmount = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    RenterTotal = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RedeemedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BookingQuotes", x => x.BookingQuoteId);
                    table.ForeignKey(
                        name: "FK_BookingQuotes_ParkingSpots_ParkingSpotId",
                        column: x => x.ParkingSpotId,
                        principalTable: "ParkingSpots",
                        principalColumn: "ParkingSpotId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BookingQuotes_Users_RenterId",
                        column: x => x.RenterId,
                        principalTable: "Users",
                        principalColumn: "UserId");
                    table.ForeignKey(
                        name: "FK_BookingQuotes_Vehicles_VehicleId",
                        column: x => x.VehicleId,
                        principalTable: "Vehicles",
                        principalColumn: "VehicleId");
                });

            migrationBuilder.CreateIndex(
                name: "IX_Bookings_BookingQuoteId",
                table: "Bookings",
                column: "BookingQuoteId",
                unique: true,
                filter: "[BookingQuoteId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Bookings_RenterId_IdempotencyKey",
                table: "Bookings",
                columns: new[] { "RenterId", "IdempotencyKey" },
                unique: true,
                filter: "[IdempotencyKey] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_BookingQuotes_ParkingSpotId",
                table: "BookingQuotes",
                column: "ParkingSpotId");

            migrationBuilder.CreateIndex(
                name: "IX_BookingQuotes_RenterId",
                table: "BookingQuotes",
                column: "RenterId");

            migrationBuilder.CreateIndex(
                name: "IX_BookingQuotes_VehicleId",
                table: "BookingQuotes",
                column: "VehicleId");

            migrationBuilder.AddForeignKey(
                name: "FK_Bookings_BookingQuotes_BookingQuoteId",
                table: "Bookings",
                column: "BookingQuoteId",
                principalTable: "BookingQuotes",
                principalColumn: "BookingQuoteId",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Bookings_BookingQuotes_BookingQuoteId",
                table: "Bookings");

            migrationBuilder.DropTable(
                name: "BookingQuotes");

            migrationBuilder.DropIndex(
                name: "IX_Bookings_BookingQuoteId",
                table: "Bookings");

            migrationBuilder.DropIndex(
                name: "IX_Bookings_RenterId_IdempotencyKey",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "ConfiguredAt",
                table: "ParkingSpots");

            migrationBuilder.DropColumn(
                name: "Description",
                table: "ParkingSpots");

            migrationBuilder.DropColumn(
                name: "IsConfigurationComplete",
                table: "ParkingSpots");

            migrationBuilder.DropColumn(
                name: "ActualExitAt",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "BookedDays",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "BookingQuoteId",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "CheckedInAt",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "IdempotencyKey",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "OverstayHours",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "OverstayPenaltyAmount",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "OwnerPayoutAmount",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "PlatformCommissionAmount",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "PlatformCommissionRate",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "RatePerDaySnapshot",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "RateType",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "RefundAmount",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "RentalSubtotal",
                table: "Bookings");

            migrationBuilder.CreateIndex(
                name: "IX_Bookings_RenterId",
                table: "Bookings",
                column: "RenterId");
        }
    }
}
