using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ParkJomV2.Migrations
{
    /// <inheritdoc />
    public partial class AddPublicParkingRates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "FlatRatePerEntry",
                table: "PublicParkingLots",
                type: "decimal(10,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "OvernightSurcharge",
                table: "PublicParkingLots",
                type: "decimal(10,2)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FlatRatePerEntry",
                table: "PublicParkingLots");

            migrationBuilder.DropColumn(
                name: "OvernightSurcharge",
                table: "PublicParkingLots");
        }
    }
}
