using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ParkJomV2.Migrations
{
    /// <inheritdoc />
    public partial class RemovedVehicleFromBookingQuote : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_BookingQuotes_Vehicles_VehicleId",
                table: "BookingQuotes");

            migrationBuilder.DropIndex(
                name: "IX_BookingQuotes_VehicleId",
                table: "BookingQuotes");

            migrationBuilder.DropColumn(
                name: "VehicleId",
                table: "BookingQuotes");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "VehicleId",
                table: "BookingQuotes",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_BookingQuotes_VehicleId",
                table: "BookingQuotes",
                column: "VehicleId");

            migrationBuilder.AddForeignKey(
                name: "FK_BookingQuotes_Vehicles_VehicleId",
                table: "BookingQuotes",
                column: "VehicleId",
                principalTable: "Vehicles",
                principalColumn: "VehicleId");
        }
    }
}
