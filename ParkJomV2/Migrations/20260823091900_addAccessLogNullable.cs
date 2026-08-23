using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ParkJomV2.Migrations
{
    /// <inheritdoc />
    public partial class addAccessLogNullable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AccessLogs_Bookings_BookingId",
                table: "AccessLogs");

            migrationBuilder.DropForeignKey(
                name: "FK_AccessLogs_IoTDevices_IoTDeviceId",
                table: "AccessLogs");

            migrationBuilder.AlterColumn<int>(
                name: "UserId",
                table: "AccessLogs",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AlterColumn<int>(
                name: "IoTDeviceId",
                table: "AccessLogs",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AlterColumn<int>(
                name: "BookingId",
                table: "AccessLogs",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AddForeignKey(
                name: "FK_AccessLogs_Bookings_BookingId",
                table: "AccessLogs",
                column: "BookingId",
                principalTable: "Bookings",
                principalColumn: "BookingId");

            migrationBuilder.AddForeignKey(
                name: "FK_AccessLogs_IoTDevices_IoTDeviceId",
                table: "AccessLogs",
                column: "IoTDeviceId",
                principalTable: "IoTDevices",
                principalColumn: "IoTDeviceId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AccessLogs_Bookings_BookingId",
                table: "AccessLogs");

            migrationBuilder.DropForeignKey(
                name: "FK_AccessLogs_IoTDevices_IoTDeviceId",
                table: "AccessLogs");

            migrationBuilder.AlterColumn<int>(
                name: "UserId",
                table: "AccessLogs",
                type: "int",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "IoTDeviceId",
                table: "AccessLogs",
                type: "int",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "BookingId",
                table: "AccessLogs",
                type: "int",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_AccessLogs_Bookings_BookingId",
                table: "AccessLogs",
                column: "BookingId",
                principalTable: "Bookings",
                principalColumn: "BookingId",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_AccessLogs_IoTDevices_IoTDeviceId",
                table: "AccessLogs",
                column: "IoTDeviceId",
                principalTable: "IoTDevices",
                principalColumn: "IoTDeviceId",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
