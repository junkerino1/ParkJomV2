using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ParkJomV2.Migrations
{
    /// <inheritdoc />
    public partial class AddUniqueFavoriteConstraint : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Favorites_UserId",
                table: "Favorites");

            migrationBuilder.CreateIndex(
                name: "IX_Favorites_UserId_ParkingSpotId",
                table: "Favorites",
                columns: new[] { "UserId", "ParkingSpotId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Favorites_UserId_ParkingSpotId",
                table: "Favorites");

            migrationBuilder.CreateIndex(
                name: "IX_Favorites_UserId",
                table: "Favorites",
                column: "UserId");
        }
    }
}
