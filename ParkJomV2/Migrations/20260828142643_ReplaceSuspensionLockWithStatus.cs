using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ParkJomV2.Migrations
{
    /// <inheritdoc />
    public partial class ReplaceSuspensionLockWithStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE ParkingSpots
                SET AvailabilityStatus = 'Suspended'
                WHERE IsSuspensionLocked = 1
                  AND AvailabilityStatus = 'Available';
                """);

            migrationBuilder.DropColumn(
                name: "IsSuspensionLocked",
                table: "ParkingSpots");

            migrationBuilder.DropColumn(
                name: "ParkingInstructions",
                table: "ParkingSpots");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsSuspensionLocked",
                table: "ParkingSpots",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "ParkingInstructions",
                table: "ParkingSpots",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE ParkingSpots
                SET IsSuspensionLocked = 1,
                    AvailabilityStatus = 'Available'
                WHERE AvailabilityStatus = 'Suspended';
                """);
        }
    }
}
