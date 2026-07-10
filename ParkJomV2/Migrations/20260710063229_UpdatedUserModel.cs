using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ParkJomV2.Migrations
{
    /// <inheritdoc />
    public partial class UpdatedUserModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Users_MediaFiles_ProfileImageId",
                table: "Users");

            migrationBuilder.DropIndex(
                name: "IX_Users_ProfileImageId",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "EmailVerifiedAt",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "PasswordHash",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "ProfileImageId",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "RefreshTokenExpiryTime",
                table: "Users");

            migrationBuilder.RenameColumn(
                name: "VerificationStatus",
                table: "Users",
                newName: "GoogleId");

            migrationBuilder.RenameColumn(
                name: "RefreshToken",
                table: "Users",
                newName: "ProfilePictureURL");

            migrationBuilder.RenameColumn(
                name: "FullName",
                table: "Users",
                newName: "LastName");

            migrationBuilder.AddColumn<string>(
                name: "FirstName",
                table: "Users",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "IsProfileComplete",
                table: "Users",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FirstName",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "IsProfileComplete",
                table: "Users");

            migrationBuilder.RenameColumn(
                name: "ProfilePictureURL",
                table: "Users",
                newName: "RefreshToken");

            migrationBuilder.RenameColumn(
                name: "LastName",
                table: "Users",
                newName: "FullName");

            migrationBuilder.RenameColumn(
                name: "GoogleId",
                table: "Users",
                newName: "VerificationStatus");

            migrationBuilder.AddColumn<DateTime>(
                name: "EmailVerifiedAt",
                table: "Users",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PasswordHash",
                table: "Users",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "ProfileImageId",
                table: "Users",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "RefreshTokenExpiryTime",
                table: "Users",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_ProfileImageId",
                table: "Users",
                column: "ProfileImageId");

            migrationBuilder.AddForeignKey(
                name: "FK_Users_MediaFiles_ProfileImageId",
                table: "Users",
                column: "ProfileImageId",
                principalTable: "MediaFiles",
                principalColumn: "MediaFileId",
                onDelete: ReferentialAction.SetNull);
        }
    }
}
