using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ParkJomV2.Migrations
{
    /// <inheritdoc />
    public partial class SimplifySupportTicketModule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SupportAttachments_SupportConversationMessages_ConversationMessageId",
                table: "SupportAttachments");

            migrationBuilder.DropForeignKey(
                name: "FK_SupportAttachments_SupportConversations_ConversationId",
                table: "SupportAttachments");

            migrationBuilder.DropForeignKey(
                name: "FK_SupportTickets_SupportConversations_ConversationId",
                table: "SupportTickets");

            migrationBuilder.DropForeignKey(
                name: "FK_SupportTickets_SupportWorkflowRuns_WorkflowRunId",
                table: "SupportTickets");

            migrationBuilder.DropTable(
                name: "SupportConversationMessages");

            migrationBuilder.DropTable(
                name: "SupportWorkflowRuns");

            migrationBuilder.DropTable(
                name: "SupportConversations");

            migrationBuilder.DropIndex(
                name: "IX_SupportTickets_ConversationId",
                table: "SupportTickets");

            migrationBuilder.DropIndex(
                name: "IX_SupportTickets_WorkflowRunId",
                table: "SupportTickets");

            migrationBuilder.DropIndex(
                name: "IX_SupportAttachments_ConversationId",
                table: "SupportAttachments");

            migrationBuilder.DropIndex(
                name: "IX_SupportAttachments_ConversationMessageId",
                table: "SupportAttachments");

            migrationBuilder.DropColumn(
                name: "ConversationId",
                table: "SupportTickets");

            migrationBuilder.DropColumn(
                name: "WorkflowRunId",
                table: "SupportTickets");

            migrationBuilder.DropColumn(
                name: "ConversationId",
                table: "SupportAttachments");

            migrationBuilder.DropColumn(
                name: "ConversationMessageId",
                table: "SupportAttachments");

            migrationBuilder.AddColumn<string>(
                name: "IssueCode",
                table: "SupportTickets",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IssueCode",
                table: "SupportTickets");

            migrationBuilder.AddColumn<int>(
                name: "ConversationId",
                table: "SupportTickets",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "WorkflowRunId",
                table: "SupportTickets",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ConversationId",
                table: "SupportAttachments",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ConversationMessageId",
                table: "SupportAttachments",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SupportConversations",
                columns: table => new
                {
                    ConversationId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AssignedAdminUserId = table.Column<int>(type: "int", nullable: true),
                    CurrentBookingId = table.Column<int>(type: "int", nullable: true),
                    CurrentParkingSpotId = table.Column<int>(type: "int", nullable: true),
                    CustomerUserId = table.Column<int>(type: "int", nullable: false),
                    Channel = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ClosedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ClosingReason = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ContextSnapshotJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ConversationReference = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastMessageAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    StartedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(max)", nullable: false),
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
                name: "SupportWorkflowRuns",
                columns: table => new
                {
                    WorkflowRunId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CustomerUserId = table.Column<int>(type: "int", nullable: false),
                    DisputeId = table.Column<int>(type: "int", nullable: true),
                    IncidentId = table.Column<int>(type: "int", nullable: true),
                    TicketId = table.Column<int>(type: "int", nullable: true),
                    AnswersJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AssignedTeam = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ChecksResultJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ClientRequestId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ContextSnapshotJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CustomerMessage = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Outcome = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Priority = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    RunReference = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    StartedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    WorkflowKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    WorkflowVersion = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false)
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

            migrationBuilder.CreateTable(
                name: "SupportConversationMessages",
                columns: table => new
                {
                    MessageId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ConversationId = table.Column<int>(type: "int", nullable: false),
                    SenderUserId = table.Column<int>(type: "int", nullable: true),
                    Body = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsInternal = table.Column<bool>(type: "bit", nullable: false),
                    MessageType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    SenderRole = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false)
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

            migrationBuilder.CreateIndex(
                name: "IX_SupportTickets_ConversationId",
                table: "SupportTickets",
                column: "ConversationId");

            migrationBuilder.CreateIndex(
                name: "IX_SupportTickets_WorkflowRunId",
                table: "SupportTickets",
                column: "WorkflowRunId");

            migrationBuilder.CreateIndex(
                name: "IX_SupportAttachments_ConversationId",
                table: "SupportAttachments",
                column: "ConversationId");

            migrationBuilder.CreateIndex(
                name: "IX_SupportAttachments_ConversationMessageId",
                table: "SupportAttachments",
                column: "ConversationMessageId");

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
                name: "FK_SupportAttachments_SupportConversationMessages_ConversationMessageId",
                table: "SupportAttachments",
                column: "ConversationMessageId",
                principalTable: "SupportConversationMessages",
                principalColumn: "MessageId");

            migrationBuilder.AddForeignKey(
                name: "FK_SupportAttachments_SupportConversations_ConversationId",
                table: "SupportAttachments",
                column: "ConversationId",
                principalTable: "SupportConversations",
                principalColumn: "ConversationId");

            migrationBuilder.AddForeignKey(
                name: "FK_SupportTickets_SupportConversations_ConversationId",
                table: "SupportTickets",
                column: "ConversationId",
                principalTable: "SupportConversations",
                principalColumn: "ConversationId");

            migrationBuilder.AddForeignKey(
                name: "FK_SupportTickets_SupportWorkflowRuns_WorkflowRunId",
                table: "SupportTickets",
                column: "WorkflowRunId",
                principalTable: "SupportWorkflowRuns",
                principalColumn: "WorkflowRunId");
        }
    }
}
