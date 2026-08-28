using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ParkJomV2.Migrations
{
    /// <inheritdoc />
    public partial class AddSuspensionLockToParkingSpots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsSuspensionLocked",
                table: "ParkingSpots",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.Sql(
                """
                UPDATE parkingSpot
                SET parkingSpot.IsSuspensionLocked = 1
                FROM ParkingSpots AS parkingSpot
                INNER JOIN Users AS ownerAccount
                    ON ownerAccount.UserId = parkingSpot.OwnerId
                WHERE ownerAccount.AccountStatus = 'Suspended';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsSuspensionLocked",
                table: "ParkingSpots");
        }
    }
}
