using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ParkJomV2.Migrations
{
    /// <inheritdoc />
    public partial class UpdatedPropertyModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "OsmId",
                table: "Properties",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "TimeToStation",
                table: "Properties",
                type: "decimal(5,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateIndex(
                name: "IX_Properties_OsmId",
                table: "Properties",
                column: "OsmId",
                unique: true,
                filter: "[OsmId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Properties_OsmId",
                table: "Properties");

            migrationBuilder.DropColumn(
                name: "OsmId",
                table: "Properties");

            migrationBuilder.DropColumn(
                name: "TimeToStation",
                table: "Properties");
        }
    }
}
