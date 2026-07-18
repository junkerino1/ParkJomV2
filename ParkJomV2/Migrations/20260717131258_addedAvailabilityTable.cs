using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ParkJomV2.Migrations
{
    /// <inheritdoc />
    public partial class addedAvailabilityTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Availabilities",
                columns: table => new
                {
                    AvailabilityId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ParkingSpotId = table.Column<int>(type: "int", nullable: false),
                    DayType = table.Column<int>(type: "int", nullable: false),
                    StartTime = table.Column<TimeOnly>(type: "time", nullable: false),
                    EndTime = table.Column<TimeOnly>(type: "time", nullable: false),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: true),
                    EffectiveUntil = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Availabilities", x => x.AvailabilityId);
                    table.ForeignKey(
                        name: "FK_Availabilities_ParkingSpots_ParkingSpotId",
                        column: x => x.ParkingSpotId,
                        principalTable: "ParkingSpots",
                        principalColumn: "ParkingSpotId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Availabilities_ParkingSpotId",
                table: "Availabilities",
                column: "ParkingSpotId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Availabilities");
        }
    }
}
