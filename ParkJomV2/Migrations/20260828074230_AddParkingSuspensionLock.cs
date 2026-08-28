using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ParkJomV2.Migrations
{
    /// <inheritdoc />
    public partial class AddParkingSuspensionLock : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                IF COL_LENGTH('ParkingSpots', 'ParkingInstructions') IS NULL
                BEGIN
                    ALTER TABLE ParkingSpots
                    ADD ParkingInstructions nvarchar(2000) NULL;
                END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ParkingInstructions",
                table: "ParkingSpots");
        }
    }
}
