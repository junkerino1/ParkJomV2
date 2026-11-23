using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ParkJomV2.Migrations
{
    public partial class AddPublicParkingSensorOccupancy : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(name: "SensorOccupiedCount", table: "PublicParkingLots", type: "int", nullable: true);
            migrationBuilder.AddColumn<DateTimeOffset>(name: "OccupancyObservedAt", table: "PublicParkingLots", type: "datetimeoffset", nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "SensorOccupiedCount", table: "PublicParkingLots");
            migrationBuilder.DropColumn(name: "OccupancyObservedAt", table: "PublicParkingLots");
        }
    }
}