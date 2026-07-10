using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ParkJomV2.Migrations
{
    /// <inheritdoc />
    public partial class AddedStationModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "NearestStation",
                table: "Properties");

            migrationBuilder.AddColumn<int>(
                name: "NearestStationId",
                table: "Properties",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "Stations",
                columns: table => new
                {
                    StationId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StationName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Latitude = table.Column<decimal>(type: "decimal(9,6)", nullable: false),
                    Longitude = table.Column<decimal>(type: "decimal(9,6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Stations", x => x.StationId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Properties_NearestStationId",
                table: "Properties",
                column: "NearestStationId");

            migrationBuilder.AddForeignKey(
                name: "FK_Properties_Stations_NearestStationId",
                table: "Properties",
                column: "NearestStationId",
                principalTable: "Stations",
                principalColumn: "StationId",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Properties_Stations_NearestStationId",
                table: "Properties");

            migrationBuilder.DropTable(
                name: "Stations");

            migrationBuilder.DropIndex(
                name: "IX_Properties_NearestStationId",
                table: "Properties");

            migrationBuilder.DropColumn(
                name: "NearestStationId",
                table: "Properties");

            migrationBuilder.AddColumn<string>(
                name: "NearestStation",
                table: "Properties",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);
        }
    }
}
