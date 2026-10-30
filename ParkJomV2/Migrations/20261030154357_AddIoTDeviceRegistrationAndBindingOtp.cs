using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ParkJomV2.Migrations
{
    /// <inheritdoc />
    public partial class AddIoTDeviceRegistrationAndBindingOtp : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_IoTDevices_ParkingSpots_ParkingSpotId",
                table: "IoTDevices");

            migrationBuilder.DropIndex(
                name: "IX_IoTDevices_ParkingSpotId",
                table: "IoTDevices");

            migrationBuilder.AlterColumn<string>(
                name: "Message",
                table: "IoTStatusLogs",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(1000)",
                oldMaxLength: 1000,
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Topic",
                table: "IoTStatusLogs",
                type: "nvarchar(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "ParkingSpotId",
                table: "IoTDevices",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.CreateTable(
                name: "IoTDeviceBindingOtps",
                columns: table => new
                {
                    IoTDeviceId = table.Column<int>(type: "int", nullable: false),
                    OtpHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    GeneratedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IoTDeviceBindingOtps", x => x.IoTDeviceId);
                    table.ForeignKey(
                        name: "FK_IoTDeviceBindingOtps_IoTDevices_IoTDeviceId",
                        column: x => x.IoTDeviceId,
                        principalTable: "IoTDevices",
                        principalColumn: "IoTDeviceId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_IoTDevices_Esp32Serial",
                table: "IoTDevices",
                column: "Esp32Serial",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IoTDevices_ParkingSpotId",
                table: "IoTDevices",
                column: "ParkingSpotId",
                unique: true,
                filter: "[ParkingSpotId] IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_IoTDevices_ParkingSpots_ParkingSpotId",
                table: "IoTDevices",
                column: "ParkingSpotId",
                principalTable: "ParkingSpots",
                principalColumn: "ParkingSpotId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_IoTDevices_ParkingSpots_ParkingSpotId",
                table: "IoTDevices");

            migrationBuilder.DropTable(
                name: "IoTDeviceBindingOtps");

            migrationBuilder.DropIndex(
                name: "IX_IoTDevices_Esp32Serial",
                table: "IoTDevices");

            migrationBuilder.DropIndex(
                name: "IX_IoTDevices_ParkingSpotId",
                table: "IoTDevices");

            migrationBuilder.DropColumn(
                name: "Topic",
                table: "IoTStatusLogs");

            migrationBuilder.AlterColumn<string>(
                name: "Message",
                table: "IoTStatusLogs",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "ParkingSpotId",
                table: "IoTDevices",
                type: "int",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_IoTDevices_ParkingSpotId",
                table: "IoTDevices",
                column: "ParkingSpotId",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_IoTDevices_ParkingSpots_ParkingSpotId",
                table: "IoTDevices",
                column: "ParkingSpotId",
                principalTable: "ParkingSpots",
                principalColumn: "ParkingSpotId",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
