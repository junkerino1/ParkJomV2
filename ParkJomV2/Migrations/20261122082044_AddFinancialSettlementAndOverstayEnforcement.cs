using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ParkJomV2.Migrations
{
    /// <inheritdoc />
    public partial class AddFinancialSettlementAndOverstayEnforcement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "HardwareExitLockEnabled",
                table: "SystemConfigurations",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "OverstayMaxDailyPenalty",
                table: "SystemConfigurations",
                type: "decimal(10,2)",
                precision: 10,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "OverstayPenaltyMultiplier",
                table: "SystemConfigurations",
                type: "decimal(5,2)",
                precision: 5,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "TaxRatePercent",
                table: "SystemConfigurations",
                type: "decimal(5,2)",
                precision: 5,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<DateTime>(
                name: "PayoutReleasedAt",
                table: "Bookings",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "OverstayCases",
                columns: table => new
                {
                    OverstayCaseId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CaseReference = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    BookingId = table.Column<int>(type: "int", nullable: false),
                    RenterId = table.Column<int>(type: "int", nullable: false),
                    ParkingSpotId = table.Column<int>(type: "int", nullable: false),
                    IoTDeviceId = table.Column<int>(type: "int", nullable: true),
                    WindowDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ScheduledEndAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    GraceEndsAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DetectedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DepartedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    OverstayMinutes = table.Column<int>(type: "int", nullable: false),
                    BillableHours = table.Column<int>(type: "int", nullable: false),
                    BaseHourlyRate = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    PenaltyMultiplier = table.Column<decimal>(type: "decimal(5,2)", nullable: false),
                    HourlyPenaltyTariff = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    CalculatedPenaltyAmount = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    DebitedAmount = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    WaivedAmount = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    PaymentStatus = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    PushWarningSentAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    SmsWarningSentAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    SmsProviderRef = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    SmsDeliveryStatus = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    LedgerDebitedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PenaltyTransactionId = table.Column<int>(type: "int", nullable: true),
                    HardwareLockActive = table.Column<bool>(type: "bit", nullable: false),
                    HardwareLockEngagedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    HardwareLockReleasedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    HardwareLockReleasedByUserId = table.Column<int>(type: "int", nullable: true),
                    DisputeInvestigationId = table.Column<int>(type: "int", nullable: true),
                    ResolutionNotes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ResolvedByUserId = table.Column<int>(type: "int", nullable: true),
                    ResolvedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OverstayCases", x => x.OverstayCaseId);
                    table.ForeignKey(
                        name: "FK_OverstayCases_Bookings_BookingId",
                        column: x => x.BookingId,
                        principalTable: "Bookings",
                        principalColumn: "BookingId");
                    table.ForeignKey(
                        name: "FK_OverstayCases_DisputeInvestigations_DisputeInvestigationId",
                        column: x => x.DisputeInvestigationId,
                        principalTable: "DisputeInvestigations",
                        principalColumn: "DisputeId");
                    table.ForeignKey(
                        name: "FK_OverstayCases_IoTDevices_IoTDeviceId",
                        column: x => x.IoTDeviceId,
                        principalTable: "IoTDevices",
                        principalColumn: "IoTDeviceId");
                    table.ForeignKey(
                        name: "FK_OverstayCases_ParkingSpots_ParkingSpotId",
                        column: x => x.ParkingSpotId,
                        principalTable: "ParkingSpots",
                        principalColumn: "ParkingSpotId");
                    table.ForeignKey(
                        name: "FK_OverstayCases_Transactions_PenaltyTransactionId",
                        column: x => x.PenaltyTransactionId,
                        principalTable: "Transactions",
                        principalColumn: "TransactionId");
                    table.ForeignKey(
                        name: "FK_OverstayCases_Users_HardwareLockReleasedByUserId",
                        column: x => x.HardwareLockReleasedByUserId,
                        principalTable: "Users",
                        principalColumn: "UserId");
                    table.ForeignKey(
                        name: "FK_OverstayCases_Users_RenterId",
                        column: x => x.RenterId,
                        principalTable: "Users",
                        principalColumn: "UserId");
                    table.ForeignKey(
                        name: "FK_OverstayCases_Users_ResolvedByUserId",
                        column: x => x.ResolvedByUserId,
                        principalTable: "Users",
                        principalColumn: "UserId");
                });

            migrationBuilder.CreateTable(
                name: "SettlementBatches",
                columns: table => new
                {
                    SettlementBatchId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BatchReference = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Rail = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ScheduleWindow = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    ItemCount = table.Column<int>(type: "int", nullable: false),
                    TotalAmount = table.Column<decimal>(type: "decimal(12,2)", nullable: false),
                    Currency = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    BnmFormatVersion = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    FileChecksum = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    BankReference = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreatedByUserId = table.Column<int>(type: "int", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    SubmittedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ClearedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SettlementBatches", x => x.SettlementBatchId);
                    table.ForeignKey(
                        name: "FK_SettlementBatches_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "UserId");
                });

            migrationBuilder.CreateTable(
                name: "SettlementBatchItems",
                columns: table => new
                {
                    SettlementBatchItemId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SettlementBatchId = table.Column<int>(type: "int", nullable: false),
                    TransactionId = table.Column<int>(type: "int", nullable: false),
                    OwnerUserId = table.Column<int>(type: "int", nullable: false),
                    BankName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    BankCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    AccountNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    AccountHolderName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    ClearingReference = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    FailureReason = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    ProcessedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SettlementBatchItems", x => x.SettlementBatchItemId);
                    table.ForeignKey(
                        name: "FK_SettlementBatchItems_SettlementBatches_SettlementBatchId",
                        column: x => x.SettlementBatchId,
                        principalTable: "SettlementBatches",
                        principalColumn: "SettlementBatchId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SettlementBatchItems_Transactions_TransactionId",
                        column: x => x.TransactionId,
                        principalTable: "Transactions",
                        principalColumn: "TransactionId");
                    table.ForeignKey(
                        name: "FK_SettlementBatchItems_Users_OwnerUserId",
                        column: x => x.OwnerUserId,
                        principalTable: "Users",
                        principalColumn: "UserId");
                });

            migrationBuilder.UpdateData(
                table: "SystemConfigurations",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "HardwareExitLockEnabled", "OverstayMaxDailyPenalty", "OverstayPenaltyMultiplier", "TaxRatePercent" },
                values: new object[] { true, 30.00m, 1.50m, 8.00m });

            migrationBuilder.CreateIndex(
                name: "IX_OverstayCases_BookingId_WindowDate",
                table: "OverstayCases",
                columns: new[] { "BookingId", "WindowDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OverstayCases_CaseReference",
                table: "OverstayCases",
                column: "CaseReference",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OverstayCases_DisputeInvestigationId",
                table: "OverstayCases",
                column: "DisputeInvestigationId");

            migrationBuilder.CreateIndex(
                name: "IX_OverstayCases_HardwareLockActive",
                table: "OverstayCases",
                column: "HardwareLockActive");

            migrationBuilder.CreateIndex(
                name: "IX_OverstayCases_HardwareLockReleasedByUserId",
                table: "OverstayCases",
                column: "HardwareLockReleasedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_OverstayCases_IoTDeviceId",
                table: "OverstayCases",
                column: "IoTDeviceId");

            migrationBuilder.CreateIndex(
                name: "IX_OverstayCases_ParkingSpotId",
                table: "OverstayCases",
                column: "ParkingSpotId");

            migrationBuilder.CreateIndex(
                name: "IX_OverstayCases_PenaltyTransactionId",
                table: "OverstayCases",
                column: "PenaltyTransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_OverstayCases_RenterId",
                table: "OverstayCases",
                column: "RenterId");

            migrationBuilder.CreateIndex(
                name: "IX_OverstayCases_ResolvedByUserId",
                table: "OverstayCases",
                column: "ResolvedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_OverstayCases_Status",
                table: "OverstayCases",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_SettlementBatches_BatchReference",
                table: "SettlementBatches",
                column: "BatchReference",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SettlementBatches_CreatedByUserId",
                table: "SettlementBatches",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_SettlementBatches_Status",
                table: "SettlementBatches",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_SettlementBatchItems_OwnerUserId",
                table: "SettlementBatchItems",
                column: "OwnerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_SettlementBatchItems_SettlementBatchId",
                table: "SettlementBatchItems",
                column: "SettlementBatchId");

            migrationBuilder.CreateIndex(
                name: "IX_SettlementBatchItems_Status",
                table: "SettlementBatchItems",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_SettlementBatchItems_TransactionId",
                table: "SettlementBatchItems",
                column: "TransactionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OverstayCases");

            migrationBuilder.DropTable(
                name: "SettlementBatchItems");

            migrationBuilder.DropTable(
                name: "SettlementBatches");

            migrationBuilder.DropColumn(
                name: "HardwareExitLockEnabled",
                table: "SystemConfigurations");

            migrationBuilder.DropColumn(
                name: "OverstayMaxDailyPenalty",
                table: "SystemConfigurations");

            migrationBuilder.DropColumn(
                name: "OverstayPenaltyMultiplier",
                table: "SystemConfigurations");

            migrationBuilder.DropColumn(
                name: "TaxRatePercent",
                table: "SystemConfigurations");

            migrationBuilder.DropColumn(
                name: "PayoutReleasedAt",
                table: "Bookings");
        }
    }
}
