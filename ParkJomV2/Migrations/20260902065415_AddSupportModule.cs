using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ParkJomV2.Migrations
{
    /// <inheritdoc />
    public partial class AddSupportModule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OperationalIncidents",
                columns: table => new
                {
                    IncidentId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IncidentReference = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    IncidentType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Priority = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PropertyId = table.Column<int>(type: "int", nullable: true),
                    ParkingSpotId = table.Column<int>(type: "int", nullable: true),
                    IoTDeviceId = table.Column<int>(type: "int", nullable: true),
                    Source = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    AssignedTeam = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    AssignedUserId = table.Column<int>(type: "int", nullable: true),
                    AffectedCustomerCount = table.Column<int>(type: "int", nullable: false),
                    AcknowledgedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ResolvedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ClosedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    EscalationLevel = table.Column<int>(type: "int", nullable: false),
                    NextEscalationAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CorrelationKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OperationalIncidents", x => x.IncidentId);
                    table.ForeignKey(
                        name: "FK_OperationalIncidents_IoTDevices_IoTDeviceId",
                        column: x => x.IoTDeviceId,
                        principalTable: "IoTDevices",
                        principalColumn: "IoTDeviceId");
                    table.ForeignKey(
                        name: "FK_OperationalIncidents_ParkingSpots_ParkingSpotId",
                        column: x => x.ParkingSpotId,
                        principalTable: "ParkingSpots",
                        principalColumn: "ParkingSpotId");
                    table.ForeignKey(
                        name: "FK_OperationalIncidents_Properties_PropertyId",
                        column: x => x.PropertyId,
                        principalTable: "Properties",
                        principalColumn: "PropertyId");
                    table.ForeignKey(
                        name: "FK_OperationalIncidents_Users_AssignedUserId",
                        column: x => x.AssignedUserId,
                        principalTable: "Users",
                        principalColumn: "UserId");
                });

            migrationBuilder.CreateTable(
                name: "SupportAuditEvents",
                columns: table => new
                {
                    AuditEventId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ObjectType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ObjectId = table.Column<int>(type: "int", nullable: false),
                    ObjectReference = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Action = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ActorUserId = table.Column<int>(type: "int", nullable: true),
                    ActorRole = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    PreviousState = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    NewState = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Detail = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Timestamp = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupportAuditEvents", x => x.AuditEventId);
                    table.ForeignKey(
                        name: "FK_SupportAuditEvents_Users_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "Users",
                        principalColumn: "UserId");
                });

            migrationBuilder.CreateTable(
                name: "SupportConversations",
                columns: table => new
                {
                    ConversationId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ConversationReference = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    CustomerUserId = table.Column<int>(type: "int", nullable: false),
                    Channel = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AssignedAdminUserId = table.Column<int>(type: "int", nullable: true),
                    CurrentBookingId = table.Column<int>(type: "int", nullable: true),
                    CurrentParkingSpotId = table.Column<int>(type: "int", nullable: true),
                    ContextSnapshotJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    StartedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastMessageAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ClosedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ClosingReason = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupportConversations", x => x.ConversationId);
                    table.ForeignKey(
                        name: "FK_SupportConversations_Bookings_CurrentBookingId",
                        column: x => x.CurrentBookingId,
                        principalTable: "Bookings",
                        principalColumn: "BookingId");
                    table.ForeignKey(
                        name: "FK_SupportConversations_ParkingSpots_CurrentParkingSpotId",
                        column: x => x.CurrentParkingSpotId,
                        principalTable: "ParkingSpots",
                        principalColumn: "ParkingSpotId");
                    table.ForeignKey(
                        name: "FK_SupportConversations_Users_AssignedAdminUserId",
                        column: x => x.AssignedAdminUserId,
                        principalTable: "Users",
                        principalColumn: "UserId");
                    table.ForeignKey(
                        name: "FK_SupportConversations_Users_CustomerUserId",
                        column: x => x.CustomerUserId,
                        principalTable: "Users",
                        principalColumn: "UserId");
                });

            migrationBuilder.CreateTable(
                name: "SupportOnCallPolicies",
                columns: table => new
                {
                    PolicyId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    P0BackupDelayMinutes = table.Column<int>(type: "int", nullable: false),
                    P0SupervisorDelayMinutes = table.Column<int>(type: "int", nullable: false),
                    P0ManagerDelayMinutes = table.Column<int>(type: "int", nullable: false),
                    P1BackupDelayMinutes = table.Column<int>(type: "int", nullable: false),
                    P1SupervisorDelayMinutes = table.Column<int>(type: "int", nullable: false),
                    P1ManagerDelayMinutes = table.Column<int>(type: "int", nullable: false),
                    NotificationChannels = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    AutoEscalateEnabled = table.Column<bool>(type: "bit", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupportOnCallPolicies", x => x.PolicyId);
                });

            migrationBuilder.CreateTable(
                name: "SupportOnCallSchedules",
                columns: table => new
                {
                    ScheduleId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ShiftName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ShiftStart = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ShiftEnd = table.Column<DateTime>(type: "datetime2", nullable: false),
                    PrimaryResponderId = table.Column<int>(type: "int", nullable: true),
                    BackupResponderId = table.Column<int>(type: "int", nullable: true),
                    SupervisorId = table.Column<int>(type: "int", nullable: true),
                    OperationsManagerId = table.Column<int>(type: "int", nullable: true),
                    ActiveChannels = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupportOnCallSchedules", x => x.ScheduleId);
                    table.ForeignKey(
                        name: "FK_SupportOnCallSchedules_Users_BackupResponderId",
                        column: x => x.BackupResponderId,
                        principalTable: "Users",
                        principalColumn: "UserId");
                    table.ForeignKey(
                        name: "FK_SupportOnCallSchedules_Users_OperationsManagerId",
                        column: x => x.OperationsManagerId,
                        principalTable: "Users",
                        principalColumn: "UserId");
                    table.ForeignKey(
                        name: "FK_SupportOnCallSchedules_Users_PrimaryResponderId",
                        column: x => x.PrimaryResponderId,
                        principalTable: "Users",
                        principalColumn: "UserId");
                    table.ForeignKey(
                        name: "FK_SupportOnCallSchedules_Users_SupervisorId",
                        column: x => x.SupervisorId,
                        principalTable: "Users",
                        principalColumn: "UserId");
                });

            migrationBuilder.CreateTable(
                name: "SupportConversationMessages",
                columns: table => new
                {
                    MessageId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ConversationId = table.Column<int>(type: "int", nullable: false),
                    SenderUserId = table.Column<int>(type: "int", nullable: true),
                    SenderRole = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    MessageType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Body = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsInternal = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupportConversationMessages", x => x.MessageId);
                    table.ForeignKey(
                        name: "FK_SupportConversationMessages_SupportConversations_ConversationId",
                        column: x => x.ConversationId,
                        principalTable: "SupportConversations",
                        principalColumn: "ConversationId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SupportConversationMessages_Users_SenderUserId",
                        column: x => x.SenderUserId,
                        principalTable: "Users",
                        principalColumn: "UserId");
                });

            migrationBuilder.CreateTable(
                name: "DisputeEvidences",
                columns: table => new
                {
                    DisputeEvidenceId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DisputeId = table.Column<int>(type: "int", nullable: false),
                    MediaFileId = table.Column<int>(type: "int", nullable: true),
                    EvidenceType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    FileUrl = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    UploadedByUserId = table.Column<int>(type: "int", nullable: false),
                    UploadedRole = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    IsVerified = table.Column<bool>(type: "bit", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DisputeEvidences", x => x.DisputeEvidenceId);
                    table.ForeignKey(
                        name: "FK_DisputeEvidences_MediaFiles_MediaFileId",
                        column: x => x.MediaFileId,
                        principalTable: "MediaFiles",
                        principalColumn: "MediaFileId");
                    table.ForeignKey(
                        name: "FK_DisputeEvidences_Users_UploadedByUserId",
                        column: x => x.UploadedByUserId,
                        principalTable: "Users",
                        principalColumn: "UserId");
                });

            migrationBuilder.CreateTable(
                name: "DisputeInvestigations",
                columns: table => new
                {
                    DisputeId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DisputeReference = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    DisputeType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CustomerUserId = table.Column<int>(type: "int", nullable: false),
                    TicketId = table.Column<int>(type: "int", nullable: true),
                    BookingId = table.Column<int>(type: "int", nullable: true),
                    PaymentId = table.Column<int>(type: "int", nullable: true),
                    TransactionId = table.Column<int>(type: "int", nullable: true),
                    Amount = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    Currency = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AssignedTeam = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    AssignedUserId = table.Column<int>(type: "int", nullable: true),
                    Decision = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    DecisionReason = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    DecidedByUserId = table.Column<int>(type: "int", nullable: true),
                    DecidedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DisputeInvestigations", x => x.DisputeId);
                    table.ForeignKey(
                        name: "FK_DisputeInvestigations_Bookings_BookingId",
                        column: x => x.BookingId,
                        principalTable: "Bookings",
                        principalColumn: "BookingId");
                    table.ForeignKey(
                        name: "FK_DisputeInvestigations_Payments_PaymentId",
                        column: x => x.PaymentId,
                        principalTable: "Payments",
                        principalColumn: "PaymentId");
                    table.ForeignKey(
                        name: "FK_DisputeInvestigations_Transactions_TransactionId",
                        column: x => x.TransactionId,
                        principalTable: "Transactions",
                        principalColumn: "TransactionId");
                    table.ForeignKey(
                        name: "FK_DisputeInvestigations_Users_AssignedUserId",
                        column: x => x.AssignedUserId,
                        principalTable: "Users",
                        principalColumn: "UserId");
                    table.ForeignKey(
                        name: "FK_DisputeInvestigations_Users_CustomerUserId",
                        column: x => x.CustomerUserId,
                        principalTable: "Users",
                        principalColumn: "UserId");
                    table.ForeignKey(
                        name: "FK_DisputeInvestigations_Users_DecidedByUserId",
                        column: x => x.DecidedByUserId,
                        principalTable: "Users",
                        principalColumn: "UserId");
                });

            migrationBuilder.CreateTable(
                name: "IncidentTickets",
                columns: table => new
                {
                    IncidentId = table.Column<int>(type: "int", nullable: false),
                    TicketId = table.Column<int>(type: "int", nullable: false),
                    LinkedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IncidentTickets", x => new { x.IncidentId, x.TicketId });
                    table.ForeignKey(
                        name: "FK_IncidentTickets_OperationalIncidents_IncidentId",
                        column: x => x.IncidentId,
                        principalTable: "OperationalIncidents",
                        principalColumn: "IncidentId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SupportAttachments",
                columns: table => new
                {
                    AttachmentId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MediaFileId = table.Column<int>(type: "int", nullable: true),
                    TicketId = table.Column<int>(type: "int", nullable: true),
                    TicketMessageId = table.Column<int>(type: "int", nullable: true),
                    ConversationId = table.Column<int>(type: "int", nullable: true),
                    ConversationMessageId = table.Column<int>(type: "int", nullable: true),
                    FileName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    FileUrl = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    FileSize = table.Column<long>(type: "bigint", nullable: false),
                    UploadedByUserId = table.Column<int>(type: "int", nullable: false),
                    IsPrivate = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupportAttachments", x => x.AttachmentId);
                    table.ForeignKey(
                        name: "FK_SupportAttachments_MediaFiles_MediaFileId",
                        column: x => x.MediaFileId,
                        principalTable: "MediaFiles",
                        principalColumn: "MediaFileId");
                    table.ForeignKey(
                        name: "FK_SupportAttachments_SupportConversationMessages_ConversationMessageId",
                        column: x => x.ConversationMessageId,
                        principalTable: "SupportConversationMessages",
                        principalColumn: "MessageId");
                    table.ForeignKey(
                        name: "FK_SupportAttachments_SupportConversations_ConversationId",
                        column: x => x.ConversationId,
                        principalTable: "SupportConversations",
                        principalColumn: "ConversationId");
                    table.ForeignKey(
                        name: "FK_SupportAttachments_Users_UploadedByUserId",
                        column: x => x.UploadedByUserId,
                        principalTable: "Users",
                        principalColumn: "UserId");
                });

            migrationBuilder.CreateTable(
                name: "SupportNotificationAttempts",
                columns: table => new
                {
                    NotificationAttemptId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IncidentId = table.Column<int>(type: "int", nullable: true),
                    TicketId = table.Column<int>(type: "int", nullable: true),
                    Channel = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Recipient = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    RecipientUserId = table.Column<int>(type: "int", nullable: true),
                    Subject = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    Message = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    AttemptCount = table.Column<int>(type: "int", nullable: false),
                    ProviderResponse = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    SentAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupportNotificationAttempts", x => x.NotificationAttemptId);
                    table.ForeignKey(
                        name: "FK_SupportNotificationAttempts_OperationalIncidents_IncidentId",
                        column: x => x.IncidentId,
                        principalTable: "OperationalIncidents",
                        principalColumn: "IncidentId");
                    table.ForeignKey(
                        name: "FK_SupportNotificationAttempts_Users_RecipientUserId",
                        column: x => x.RecipientUserId,
                        principalTable: "Users",
                        principalColumn: "UserId");
                });

            migrationBuilder.CreateTable(
                name: "SupportTicketMessages",
                columns: table => new
                {
                    MessageId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TicketId = table.Column<int>(type: "int", nullable: false),
                    SenderUserId = table.Column<int>(type: "int", nullable: true),
                    SenderRole = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Body = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsInternal = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupportTicketMessages", x => x.MessageId);
                    table.ForeignKey(
                        name: "FK_SupportTicketMessages_Users_SenderUserId",
                        column: x => x.SenderUserId,
                        principalTable: "Users",
                        principalColumn: "UserId");
                });

            migrationBuilder.CreateTable(
                name: "SupportTickets",
                columns: table => new
                {
                    TicketId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TicketReference = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    TicketType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Source = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Category = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Priority = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CustomerUserId = table.Column<int>(type: "int", nullable: false),
                    CreatedByUserId = table.Column<int>(type: "int", nullable: false),
                    AssignedAdminUserId = table.Column<int>(type: "int", nullable: true),
                    AssignedTeam = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ConversationId = table.Column<int>(type: "int", nullable: true),
                    WorkflowRunId = table.Column<int>(type: "int", nullable: true),
                    BookingId = table.Column<int>(type: "int", nullable: true),
                    ParkingSpotId = table.Column<int>(type: "int", nullable: true),
                    VehicleId = table.Column<int>(type: "int", nullable: true),
                    OperationalIncidentId = table.Column<int>(type: "int", nullable: true),
                    DisputeInvestigationId = table.Column<int>(type: "int", nullable: true),
                    Subject = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AcceptedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    FirstResponseAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    FirstResponseDueAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ResolvedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ResolutionDueAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ClosedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ResolutionCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    InternalSummary = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupportTickets", x => x.TicketId);
                    table.ForeignKey(
                        name: "FK_SupportTickets_Bookings_BookingId",
                        column: x => x.BookingId,
                        principalTable: "Bookings",
                        principalColumn: "BookingId");
                    table.ForeignKey(
                        name: "FK_SupportTickets_DisputeInvestigations_DisputeInvestigationId",
                        column: x => x.DisputeInvestigationId,
                        principalTable: "DisputeInvestigations",
                        principalColumn: "DisputeId");
                    table.ForeignKey(
                        name: "FK_SupportTickets_OperationalIncidents_OperationalIncidentId",
                        column: x => x.OperationalIncidentId,
                        principalTable: "OperationalIncidents",
                        principalColumn: "IncidentId");
                    table.ForeignKey(
                        name: "FK_SupportTickets_ParkingSpots_ParkingSpotId",
                        column: x => x.ParkingSpotId,
                        principalTable: "ParkingSpots",
                        principalColumn: "ParkingSpotId");
                    table.ForeignKey(
                        name: "FK_SupportTickets_SupportConversations_ConversationId",
                        column: x => x.ConversationId,
                        principalTable: "SupportConversations",
                        principalColumn: "ConversationId");
                    table.ForeignKey(
                        name: "FK_SupportTickets_Users_AssignedAdminUserId",
                        column: x => x.AssignedAdminUserId,
                        principalTable: "Users",
                        principalColumn: "UserId");
                    table.ForeignKey(
                        name: "FK_SupportTickets_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "UserId");
                    table.ForeignKey(
                        name: "FK_SupportTickets_Users_CustomerUserId",
                        column: x => x.CustomerUserId,
                        principalTable: "Users",
                        principalColumn: "UserId");
                    table.ForeignKey(
                        name: "FK_SupportTickets_Vehicles_VehicleId",
                        column: x => x.VehicleId,
                        principalTable: "Vehicles",
                        principalColumn: "VehicleId");
                });

            migrationBuilder.CreateTable(
                name: "SupportWorkflowRuns",
                columns: table => new
                {
                    WorkflowRunId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RunReference = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    CustomerUserId = table.Column<int>(type: "int", nullable: false),
                    WorkflowKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    WorkflowVersion = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    AnswersJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ContextSnapshotJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Outcome = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Priority = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    AssignedTeam = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ChecksResultJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    TicketId = table.Column<int>(type: "int", nullable: true),
                    IncidentId = table.Column<int>(type: "int", nullable: true),
                    DisputeId = table.Column<int>(type: "int", nullable: true),
                    ClientRequestId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CustomerMessage = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    StartedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupportWorkflowRuns", x => x.WorkflowRunId);
                    table.ForeignKey(
                        name: "FK_SupportWorkflowRuns_DisputeInvestigations_DisputeId",
                        column: x => x.DisputeId,
                        principalTable: "DisputeInvestigations",
                        principalColumn: "DisputeId");
                    table.ForeignKey(
                        name: "FK_SupportWorkflowRuns_OperationalIncidents_IncidentId",
                        column: x => x.IncidentId,
                        principalTable: "OperationalIncidents",
                        principalColumn: "IncidentId");
                    table.ForeignKey(
                        name: "FK_SupportWorkflowRuns_SupportTickets_TicketId",
                        column: x => x.TicketId,
                        principalTable: "SupportTickets",
                        principalColumn: "TicketId");
                    table.ForeignKey(
                        name: "FK_SupportWorkflowRuns_Users_CustomerUserId",
                        column: x => x.CustomerUserId,
                        principalTable: "Users",
                        principalColumn: "UserId");
                });

            migrationBuilder.CreateIndex(
                name: "IX_DisputeEvidences_DisputeId",
                table: "DisputeEvidences",
                column: "DisputeId");

            migrationBuilder.CreateIndex(
                name: "IX_DisputeEvidences_MediaFileId",
                table: "DisputeEvidences",
                column: "MediaFileId");

            migrationBuilder.CreateIndex(
                name: "IX_DisputeEvidences_UploadedByUserId",
                table: "DisputeEvidences",
                column: "UploadedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_DisputeInvestigations_AssignedUserId",
                table: "DisputeInvestigations",
                column: "AssignedUserId");

            migrationBuilder.CreateIndex(
                name: "IX_DisputeInvestigations_BookingId",
                table: "DisputeInvestigations",
                column: "BookingId");

            migrationBuilder.CreateIndex(
                name: "IX_DisputeInvestigations_CustomerUserId",
                table: "DisputeInvestigations",
                column: "CustomerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_DisputeInvestigations_DecidedByUserId",
                table: "DisputeInvestigations",
                column: "DecidedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_DisputeInvestigations_DisputeReference",
                table: "DisputeInvestigations",
                column: "DisputeReference",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DisputeInvestigations_PaymentId",
                table: "DisputeInvestigations",
                column: "PaymentId");

            migrationBuilder.CreateIndex(
                name: "IX_DisputeInvestigations_TicketId",
                table: "DisputeInvestigations",
                column: "TicketId");

            migrationBuilder.CreateIndex(
                name: "IX_DisputeInvestigations_TransactionId",
                table: "DisputeInvestigations",
                column: "TransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_IncidentTickets_TicketId",
                table: "IncidentTickets",
                column: "TicketId");

            migrationBuilder.CreateIndex(
                name: "IX_OperationalIncidents_AssignedUserId",
                table: "OperationalIncidents",
                column: "AssignedUserId");

            migrationBuilder.CreateIndex(
                name: "IX_OperationalIncidents_IncidentReference",
                table: "OperationalIncidents",
                column: "IncidentReference",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OperationalIncidents_IoTDeviceId",
                table: "OperationalIncidents",
                column: "IoTDeviceId");

            migrationBuilder.CreateIndex(
                name: "IX_OperationalIncidents_ParkingSpotId",
                table: "OperationalIncidents",
                column: "ParkingSpotId");

            migrationBuilder.CreateIndex(
                name: "IX_OperationalIncidents_PropertyId",
                table: "OperationalIncidents",
                column: "PropertyId");

            migrationBuilder.CreateIndex(
                name: "IX_SupportAttachments_ConversationId",
                table: "SupportAttachments",
                column: "ConversationId");

            migrationBuilder.CreateIndex(
                name: "IX_SupportAttachments_ConversationMessageId",
                table: "SupportAttachments",
                column: "ConversationMessageId");

            migrationBuilder.CreateIndex(
                name: "IX_SupportAttachments_MediaFileId",
                table: "SupportAttachments",
                column: "MediaFileId");

            migrationBuilder.CreateIndex(
                name: "IX_SupportAttachments_TicketId",
                table: "SupportAttachments",
                column: "TicketId");

            migrationBuilder.CreateIndex(
                name: "IX_SupportAttachments_TicketMessageId",
                table: "SupportAttachments",
                column: "TicketMessageId");

            migrationBuilder.CreateIndex(
                name: "IX_SupportAttachments_UploadedByUserId",
                table: "SupportAttachments",
                column: "UploadedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_SupportAuditEvents_ActorUserId",
                table: "SupportAuditEvents",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_SupportAuditEvents_ObjectType_ObjectId",
                table: "SupportAuditEvents",
                columns: new[] { "ObjectType", "ObjectId" });

            migrationBuilder.CreateIndex(
                name: "IX_SupportConversationMessages_ConversationId",
                table: "SupportConversationMessages",
                column: "ConversationId");

            migrationBuilder.CreateIndex(
                name: "IX_SupportConversationMessages_SenderUserId",
                table: "SupportConversationMessages",
                column: "SenderUserId");

            migrationBuilder.CreateIndex(
                name: "IX_SupportConversations_AssignedAdminUserId",
                table: "SupportConversations",
                column: "AssignedAdminUserId");

            migrationBuilder.CreateIndex(
                name: "IX_SupportConversations_ConversationReference",
                table: "SupportConversations",
                column: "ConversationReference",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SupportConversations_CurrentBookingId",
                table: "SupportConversations",
                column: "CurrentBookingId");

            migrationBuilder.CreateIndex(
                name: "IX_SupportConversations_CurrentParkingSpotId",
                table: "SupportConversations",
                column: "CurrentParkingSpotId");

            migrationBuilder.CreateIndex(
                name: "IX_SupportConversations_CustomerUserId",
                table: "SupportConversations",
                column: "CustomerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_SupportNotificationAttempts_IncidentId",
                table: "SupportNotificationAttempts",
                column: "IncidentId");

            migrationBuilder.CreateIndex(
                name: "IX_SupportNotificationAttempts_RecipientUserId",
                table: "SupportNotificationAttempts",
                column: "RecipientUserId");

            migrationBuilder.CreateIndex(
                name: "IX_SupportNotificationAttempts_TicketId",
                table: "SupportNotificationAttempts",
                column: "TicketId");

            migrationBuilder.CreateIndex(
                name: "IX_SupportOnCallSchedules_BackupResponderId",
                table: "SupportOnCallSchedules",
                column: "BackupResponderId");

            migrationBuilder.CreateIndex(
                name: "IX_SupportOnCallSchedules_OperationsManagerId",
                table: "SupportOnCallSchedules",
                column: "OperationsManagerId");

            migrationBuilder.CreateIndex(
                name: "IX_SupportOnCallSchedules_PrimaryResponderId",
                table: "SupportOnCallSchedules",
                column: "PrimaryResponderId");

            migrationBuilder.CreateIndex(
                name: "IX_SupportOnCallSchedules_SupervisorId",
                table: "SupportOnCallSchedules",
                column: "SupervisorId");

            migrationBuilder.CreateIndex(
                name: "IX_SupportTicketMessages_SenderUserId",
                table: "SupportTicketMessages",
                column: "SenderUserId");

            migrationBuilder.CreateIndex(
                name: "IX_SupportTicketMessages_TicketId",
                table: "SupportTicketMessages",
                column: "TicketId");

            migrationBuilder.CreateIndex(
                name: "IX_SupportTickets_AssignedAdminUserId",
                table: "SupportTickets",
                column: "AssignedAdminUserId");

            migrationBuilder.CreateIndex(
                name: "IX_SupportTickets_BookingId",
                table: "SupportTickets",
                column: "BookingId");

            migrationBuilder.CreateIndex(
                name: "IX_SupportTickets_ConversationId",
                table: "SupportTickets",
                column: "ConversationId");

            migrationBuilder.CreateIndex(
                name: "IX_SupportTickets_CreatedByUserId",
                table: "SupportTickets",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_SupportTickets_CustomerUserId",
                table: "SupportTickets",
                column: "CustomerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_SupportTickets_DisputeInvestigationId",
                table: "SupportTickets",
                column: "DisputeInvestigationId");

            migrationBuilder.CreateIndex(
                name: "IX_SupportTickets_OperationalIncidentId",
                table: "SupportTickets",
                column: "OperationalIncidentId");

            migrationBuilder.CreateIndex(
                name: "IX_SupportTickets_ParkingSpotId",
                table: "SupportTickets",
                column: "ParkingSpotId");

            migrationBuilder.CreateIndex(
                name: "IX_SupportTickets_TicketReference",
                table: "SupportTickets",
                column: "TicketReference",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SupportTickets_VehicleId",
                table: "SupportTickets",
                column: "VehicleId");

            migrationBuilder.CreateIndex(
                name: "IX_SupportTickets_WorkflowRunId",
                table: "SupportTickets",
                column: "WorkflowRunId");

            migrationBuilder.CreateIndex(
                name: "IX_SupportWorkflowRuns_CustomerUserId",
                table: "SupportWorkflowRuns",
                column: "CustomerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_SupportWorkflowRuns_DisputeId",
                table: "SupportWorkflowRuns",
                column: "DisputeId");

            migrationBuilder.CreateIndex(
                name: "IX_SupportWorkflowRuns_IncidentId",
                table: "SupportWorkflowRuns",
                column: "IncidentId");

            migrationBuilder.CreateIndex(
                name: "IX_SupportWorkflowRuns_RunReference",
                table: "SupportWorkflowRuns",
                column: "RunReference",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SupportWorkflowRuns_TicketId",
                table: "SupportWorkflowRuns",
                column: "TicketId");

            migrationBuilder.AddForeignKey(
                name: "FK_DisputeEvidences_DisputeInvestigations_DisputeId",
                table: "DisputeEvidences",
                column: "DisputeId",
                principalTable: "DisputeInvestigations",
                principalColumn: "DisputeId",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_DisputeInvestigations_SupportTickets_TicketId",
                table: "DisputeInvestigations",
                column: "TicketId",
                principalTable: "SupportTickets",
                principalColumn: "TicketId");

            migrationBuilder.AddForeignKey(
                name: "FK_IncidentTickets_SupportTickets_TicketId",
                table: "IncidentTickets",
                column: "TicketId",
                principalTable: "SupportTickets",
                principalColumn: "TicketId",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_SupportAttachments_SupportTicketMessages_TicketMessageId",
                table: "SupportAttachments",
                column: "TicketMessageId",
                principalTable: "SupportTicketMessages",
                principalColumn: "MessageId");

            migrationBuilder.AddForeignKey(
                name: "FK_SupportAttachments_SupportTickets_TicketId",
                table: "SupportAttachments",
                column: "TicketId",
                principalTable: "SupportTickets",
                principalColumn: "TicketId");

            migrationBuilder.AddForeignKey(
                name: "FK_SupportNotificationAttempts_SupportTickets_TicketId",
                table: "SupportNotificationAttempts",
                column: "TicketId",
                principalTable: "SupportTickets",
                principalColumn: "TicketId");

            migrationBuilder.AddForeignKey(
                name: "FK_SupportTicketMessages_SupportTickets_TicketId",
                table: "SupportTicketMessages",
                column: "TicketId",
                principalTable: "SupportTickets",
                principalColumn: "TicketId",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_SupportTickets_SupportWorkflowRuns_WorkflowRunId",
                table: "SupportTickets",
                column: "WorkflowRunId",
                principalTable: "SupportWorkflowRuns",
                principalColumn: "WorkflowRunId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SupportTickets_DisputeInvestigations_DisputeInvestigationId",
                table: "SupportTickets");

            migrationBuilder.DropForeignKey(
                name: "FK_SupportWorkflowRuns_DisputeInvestigations_DisputeId",
                table: "SupportWorkflowRuns");

            migrationBuilder.DropForeignKey(
                name: "FK_SupportWorkflowRuns_SupportTickets_TicketId",
                table: "SupportWorkflowRuns");

            migrationBuilder.DropTable(
                name: "DisputeEvidences");

            migrationBuilder.DropTable(
                name: "IncidentTickets");

            migrationBuilder.DropTable(
                name: "SupportAttachments");

            migrationBuilder.DropTable(
                name: "SupportAuditEvents");

            migrationBuilder.DropTable(
                name: "SupportNotificationAttempts");

            migrationBuilder.DropTable(
                name: "SupportOnCallPolicies");

            migrationBuilder.DropTable(
                name: "SupportOnCallSchedules");

            migrationBuilder.DropTable(
                name: "SupportConversationMessages");

            migrationBuilder.DropTable(
                name: "SupportTicketMessages");

            migrationBuilder.DropTable(
                name: "DisputeInvestigations");

            migrationBuilder.DropTable(
                name: "SupportTickets");

            migrationBuilder.DropTable(
                name: "SupportConversations");

            migrationBuilder.DropTable(
                name: "SupportWorkflowRuns");

            migrationBuilder.DropTable(
                name: "OperationalIncidents");
        }
    }
}
