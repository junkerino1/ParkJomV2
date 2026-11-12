using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ParkJomV2.Migrations
{
    /// <inheritdoc />
    public partial class AddPublicParkingVerificationAndIot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AdminReviewNotes",
                table: "PublicParkingLots",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DistanceToStation",
                table: "PublicParkingLots",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EntrancePhotoUrl",
                table: "PublicParkingLots",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FacilityPhotoUrl",
                table: "PublicParkingLots",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IotDeviceUuid",
                table: "PublicParkingLots",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsIotConnected",
                table: "PublicParkingLots",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "NearestStationName",
                table: "PublicParkingLots",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VerificationDocumentType",
                table: "PublicParkingLots",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VerificationDocumentUrl",
                table: "PublicParkingLots",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "VerificationStatus",
                table: "PublicParkingLots",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AdminReviewNotes",
                table: "PublicParkingLots");

            migrationBuilder.DropColumn(
                name: "DistanceToStation",
                table: "PublicParkingLots");

            migrationBuilder.DropColumn(
                name: "EntrancePhotoUrl",
                table: "PublicParkingLots");

            migrationBuilder.DropColumn(
                name: "FacilityPhotoUrl",
                table: "PublicParkingLots");

            migrationBuilder.DropColumn(
                name: "IotDeviceUuid",
                table: "PublicParkingLots");

            migrationBuilder.DropColumn(
                name: "IsIotConnected",
                table: "PublicParkingLots");

            migrationBuilder.DropColumn(
                name: "NearestStationName",
                table: "PublicParkingLots");

            migrationBuilder.DropColumn(
                name: "VerificationDocumentType",
                table: "PublicParkingLots");

            migrationBuilder.DropColumn(
                name: "VerificationDocumentUrl",
                table: "PublicParkingLots");

            migrationBuilder.DropColumn(
                name: "VerificationStatus",
                table: "PublicParkingLots");
        }
    }
}
