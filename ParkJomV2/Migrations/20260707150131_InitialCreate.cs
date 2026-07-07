using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ParkJomV2.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Properties",
                columns: table => new
                {
                    PropertyId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PropertyName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    PropertyType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Address = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    Latitude = table.Column<decimal>(type: "decimal(9,6)", nullable: false),
                    Longitude = table.Column<decimal>(type: "decimal(9,6)", nullable: false),
                    NearestStation = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    DistanceToStation = table.Column<decimal>(type: "decimal(5,2)", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Properties", x => x.PropertyId);
                });

            migrationBuilder.CreateTable(
                name: "AccessLogs",
                columns: table => new
                {
                    AccessLogId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BookingId = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    IoTDeviceId = table.Column<int>(type: "int", nullable: false),
                    Actions = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    AccessedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccessLogs", x => x.AccessLogId);
                });

            migrationBuilder.CreateTable(
                name: "Bookings",
                columns: table => new
                {
                    BookingId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BookingReference = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    RenterId = table.Column<int>(type: "int", nullable: false),
                    ParkingSpotId = table.Column<int>(type: "int", nullable: false),
                    VehicleId = table.Column<int>(type: "int", nullable: false),
                    StartDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EndDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    BookingStatus = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TotalAmount = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    CancellationReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CancelledAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Bookings", x => x.BookingId);
                });

            migrationBuilder.CreateTable(
                name: "Favorites",
                columns: table => new
                {
                    FavoriteId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    ParkingSpotId = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Favorites", x => x.FavoriteId);
                });

            migrationBuilder.CreateTable(
                name: "IoTDevices",
                columns: table => new
                {
                    IoTDeviceId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ParkingSpotId = table.Column<int>(type: "int", nullable: false),
                    Esp32Serial = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    FirmwareVersion = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    DeviceStatus = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsAssigned = table.Column<bool>(type: "bit", nullable: false),
                    LastHeartbeatAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IoTDevices", x => x.IoTDeviceId);
                });

            migrationBuilder.CreateTable(
                name: "IoTStatusLogs",
                columns: table => new
                {
                    IoTStatusLogId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IoTDeviceId = table.Column<int>(type: "int", nullable: false),
                    DeviceStatus = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Message = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    LoggedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IoTStatusLogs", x => x.IoTStatusLogId);
                    table.ForeignKey(
                        name: "FK_IoTStatusLogs_IoTDevices_IoTDeviceId",
                        column: x => x.IoTDeviceId,
                        principalTable: "IoTDevices",
                        principalColumn: "IoTDeviceId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MediaFiles",
                columns: table => new
                {
                    MediaFileId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PublicId = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    SecureUrl = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    OriginalFileName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    MimeType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    FileExtension = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    FileSize = table.Column<long>(type: "bigint", nullable: false),
                    ResourceType = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Folder = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    UploadedBy = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MediaFiles", x => x.MediaFileId);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    UserId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FullName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Email = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    PasswordHash = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PhoneNumber = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    UserType = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    ProfileImageId = table.Column<int>(type: "int", nullable: true),
                    VerificationStatus = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AccountStatus = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    RefreshToken = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RefreshTokenExpiryTime = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastLoginAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    EmailVerifiedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.UserId);
                    table.ForeignKey(
                        name: "FK_Users_MediaFiles_ProfileImageId",
                        column: x => x.ProfileImageId,
                        principalTable: "MediaFiles",
                        principalColumn: "MediaFileId",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "ParkingSpots",
                columns: table => new
                {
                    ParkingSpotId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PropertyId = table.Column<int>(type: "int", nullable: false),
                    OwnerId = table.Column<int>(type: "int", nullable: false),
                    ParkingLabel = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    AvailabilityStatus = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    MonthlyPrice = table.Column<decimal>(type: "decimal(6,2)", nullable: false),
                    IsPublished = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ParkingSpots", x => x.ParkingSpotId);
                    table.ForeignKey(
                        name: "FK_ParkingSpots_Properties_PropertyId",
                        column: x => x.PropertyId,
                        principalTable: "Properties",
                        principalColumn: "PropertyId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ParkingSpots_Users_OwnerId",
                        column: x => x.OwnerId,
                        principalTable: "Users",
                        principalColumn: "UserId");
                });

            migrationBuilder.CreateTable(
                name: "Vehicles",
                columns: table => new
                {
                    VehicleId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    NumberPlate = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    VehicleBrand = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    VehicleModel = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    VehicleColor = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Vehicles", x => x.VehicleId);
                    table.ForeignKey(
                        name: "FK_Vehicles_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "UserId");
                });

            migrationBuilder.CreateTable(
                name: "Wallets",
                columns: table => new
                {
                    WalletId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    Balance = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    OnHold = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Wallets", x => x.WalletId);
                    table.ForeignKey(
                        name: "FK_Wallets_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ParkingSpotImages",
                columns: table => new
                {
                    ParkingSpotImageId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ParkingSpotId = table.Column<int>(type: "int", nullable: false),
                    MediaFileId = table.Column<int>(type: "int", nullable: false),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false),
                    IsPrimary = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ParkingSpotImages", x => x.ParkingSpotImageId);
                    table.ForeignKey(
                        name: "FK_ParkingSpotImages_MediaFiles_MediaFileId",
                        column: x => x.MediaFileId,
                        principalTable: "MediaFiles",
                        principalColumn: "MediaFileId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ParkingSpotImages_ParkingSpots_ParkingSpotId",
                        column: x => x.ParkingSpotId,
                        principalTable: "ParkingSpots",
                        principalColumn: "ParkingSpotId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ParkingVerificationRequests",
                columns: table => new
                {
                    VerificationRequestId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ParkingSpotId = table.Column<int>(type: "int", nullable: false),
                    SubmittedByUserId = table.Column<int>(type: "int", nullable: false),
                    VerificationStatus = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsCurrent = table.Column<bool>(type: "bit", nullable: false),
                    SubmittedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ReviewedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ParkingVerificationRequests", x => x.VerificationRequestId);
                    table.ForeignKey(
                        name: "FK_ParkingVerificationRequests_ParkingSpots_ParkingSpotId",
                        column: x => x.ParkingSpotId,
                        principalTable: "ParkingSpots",
                        principalColumn: "ParkingSpotId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ParkingVerificationRequests_Users_SubmittedByUserId",
                        column: x => x.SubmittedByUserId,
                        principalTable: "Users",
                        principalColumn: "UserId");
                });

            migrationBuilder.CreateTable(
                name: "Reviews",
                columns: table => new
                {
                    ReviewId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BookingId = table.Column<int>(type: "int", nullable: false),
                    ParkingSpotId = table.Column<int>(type: "int", nullable: false),
                    ReviewerId = table.Column<int>(type: "int", nullable: false),
                    Rating = table.Column<int>(type: "int", nullable: false),
                    Comment = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    OwnerReply = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    OwnerReplyAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Reviews", x => x.ReviewId);
                    table.ForeignKey(
                        name: "FK_Reviews_Bookings_BookingId",
                        column: x => x.BookingId,
                        principalTable: "Bookings",
                        principalColumn: "BookingId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Reviews_ParkingSpots_ParkingSpotId",
                        column: x => x.ParkingSpotId,
                        principalTable: "ParkingSpots",
                        principalColumn: "ParkingSpotId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Reviews_Users_ReviewerId",
                        column: x => x.ReviewerId,
                        principalTable: "Users",
                        principalColumn: "UserId");
                });

            migrationBuilder.CreateTable(
                name: "Transactions",
                columns: table => new
                {
                    TransactionId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    WalletId = table.Column<int>(type: "int", nullable: false),
                    BookingId = table.Column<int>(type: "int", nullable: true),
                    TransactionType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    PaymentMethod = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TransactionStatus = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ReferenceNumber = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Transactions", x => x.TransactionId);
                    table.ForeignKey(
                        name: "FK_Transactions_Bookings_BookingId",
                        column: x => x.BookingId,
                        principalTable: "Bookings",
                        principalColumn: "BookingId");
                    table.ForeignKey(
                        name: "FK_Transactions_Wallets_WalletId",
                        column: x => x.WalletId,
                        principalTable: "Wallets",
                        principalColumn: "WalletId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "VerificationDocuments",
                columns: table => new
                {
                    VerificationDocumentId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    VerificationRequestId = table.Column<int>(type: "int", nullable: false),
                    MediaFileId = table.Column<int>(type: "int", nullable: false),
                    DocumentType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    UploadedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VerificationDocuments", x => x.VerificationDocumentId);
                    table.ForeignKey(
                        name: "FK_VerificationDocuments_MediaFiles_MediaFileId",
                        column: x => x.MediaFileId,
                        principalTable: "MediaFiles",
                        principalColumn: "MediaFileId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_VerificationDocuments_ParkingVerificationRequests_VerificationRequestId",
                        column: x => x.VerificationRequestId,
                        principalTable: "ParkingVerificationRequests",
                        principalColumn: "VerificationRequestId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AccessLogs_BookingId",
                table: "AccessLogs",
                column: "BookingId");

            migrationBuilder.CreateIndex(
                name: "IX_AccessLogs_IoTDeviceId",
                table: "AccessLogs",
                column: "IoTDeviceId");

            migrationBuilder.CreateIndex(
                name: "IX_AccessLogs_UserId",
                table: "AccessLogs",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Bookings_BookingReference",
                table: "Bookings",
                column: "BookingReference",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Bookings_ParkingSpotId",
                table: "Bookings",
                column: "ParkingSpotId");

            migrationBuilder.CreateIndex(
                name: "IX_Bookings_RenterId",
                table: "Bookings",
                column: "RenterId");

            migrationBuilder.CreateIndex(
                name: "IX_Bookings_VehicleId",
                table: "Bookings",
                column: "VehicleId");

            migrationBuilder.CreateIndex(
                name: "IX_Favorites_ParkingSpotId",
                table: "Favorites",
                column: "ParkingSpotId");

            migrationBuilder.CreateIndex(
                name: "IX_Favorites_UserId",
                table: "Favorites",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_IoTDevices_ParkingSpotId",
                table: "IoTDevices",
                column: "ParkingSpotId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IoTStatusLogs_IoTDeviceId",
                table: "IoTStatusLogs",
                column: "IoTDeviceId");

            migrationBuilder.CreateIndex(
                name: "IX_MediaFiles_UploadedBy",
                table: "MediaFiles",
                column: "UploadedBy");

            migrationBuilder.CreateIndex(
                name: "IX_ParkingSpotImages_MediaFileId",
                table: "ParkingSpotImages",
                column: "MediaFileId");

            migrationBuilder.CreateIndex(
                name: "IX_ParkingSpotImages_ParkingSpotId",
                table: "ParkingSpotImages",
                column: "ParkingSpotId");

            migrationBuilder.CreateIndex(
                name: "IX_ParkingSpots_OwnerId",
                table: "ParkingSpots",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_ParkingSpots_PropertyId",
                table: "ParkingSpots",
                column: "PropertyId");

            migrationBuilder.CreateIndex(
                name: "IX_ParkingVerificationRequests_ParkingSpotId",
                table: "ParkingVerificationRequests",
                column: "ParkingSpotId");

            migrationBuilder.CreateIndex(
                name: "IX_ParkingVerificationRequests_SubmittedByUserId",
                table: "ParkingVerificationRequests",
                column: "SubmittedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Reviews_BookingId",
                table: "Reviews",
                column: "BookingId");

            migrationBuilder.CreateIndex(
                name: "IX_Reviews_ParkingSpotId",
                table: "Reviews",
                column: "ParkingSpotId");

            migrationBuilder.CreateIndex(
                name: "IX_Reviews_ReviewerId",
                table: "Reviews",
                column: "ReviewerId");

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_BookingId",
                table: "Transactions",
                column: "BookingId");

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_WalletId",
                table: "Transactions",
                column: "WalletId");

            migrationBuilder.CreateIndex(
                name: "IX_Users_Email",
                table: "Users",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_ProfileImageId",
                table: "Users",
                column: "ProfileImageId");

            migrationBuilder.CreateIndex(
                name: "IX_Vehicles_UserId",
                table: "Vehicles",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_VerificationDocuments_MediaFileId",
                table: "VerificationDocuments",
                column: "MediaFileId");

            migrationBuilder.CreateIndex(
                name: "IX_VerificationDocuments_VerificationRequestId",
                table: "VerificationDocuments",
                column: "VerificationRequestId");

            migrationBuilder.CreateIndex(
                name: "IX_Wallets_UserId",
                table: "Wallets",
                column: "UserId",
                unique: true);

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

            migrationBuilder.AddForeignKey(
                name: "FK_AccessLogs_Users_UserId",
                table: "AccessLogs",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "UserId");

            migrationBuilder.AddForeignKey(
                name: "FK_Bookings_ParkingSpots_ParkingSpotId",
                table: "Bookings",
                column: "ParkingSpotId",
                principalTable: "ParkingSpots",
                principalColumn: "ParkingSpotId",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Bookings_Users_RenterId",
                table: "Bookings",
                column: "RenterId",
                principalTable: "Users",
                principalColumn: "UserId");

            migrationBuilder.AddForeignKey(
                name: "FK_Bookings_Vehicles_VehicleId",
                table: "Bookings",
                column: "VehicleId",
                principalTable: "Vehicles",
                principalColumn: "VehicleId",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Favorites_ParkingSpots_ParkingSpotId",
                table: "Favorites",
                column: "ParkingSpotId",
                principalTable: "ParkingSpots",
                principalColumn: "ParkingSpotId",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Favorites_Users_UserId",
                table: "Favorites",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "UserId",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_IoTDevices_ParkingSpots_ParkingSpotId",
                table: "IoTDevices",
                column: "ParkingSpotId",
                principalTable: "ParkingSpots",
                principalColumn: "ParkingSpotId",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_MediaFiles_Users_UploadedBy",
                table: "MediaFiles",
                column: "UploadedBy",
                principalTable: "Users",
                principalColumn: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_MediaFiles_Users_UploadedBy",
                table: "MediaFiles");

            migrationBuilder.DropTable(
                name: "AccessLogs");

            migrationBuilder.DropTable(
                name: "Favorites");

            migrationBuilder.DropTable(
                name: "IoTStatusLogs");

            migrationBuilder.DropTable(
                name: "ParkingSpotImages");

            migrationBuilder.DropTable(
                name: "Reviews");

            migrationBuilder.DropTable(
                name: "Transactions");

            migrationBuilder.DropTable(
                name: "VerificationDocuments");

            migrationBuilder.DropTable(
                name: "IoTDevices");

            migrationBuilder.DropTable(
                name: "Bookings");

            migrationBuilder.DropTable(
                name: "Wallets");

            migrationBuilder.DropTable(
                name: "ParkingVerificationRequests");

            migrationBuilder.DropTable(
                name: "Vehicles");

            migrationBuilder.DropTable(
                name: "ParkingSpots");

            migrationBuilder.DropTable(
                name: "Properties");

            migrationBuilder.DropTable(
                name: "Users");

            migrationBuilder.DropTable(
                name: "MediaFiles");
        }
    }
}
