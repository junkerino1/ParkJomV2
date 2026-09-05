using System.Text.Json;
using System.Text.Json.Serialization;
using ParkJomV2.Models.Enums;

namespace ParkJomV2.DTOs;

#region General API Envelope
public class SupportApiResponse<T>
{
    public int Code { get; set; } = 200;
    public bool Success { get; set; } = true;
    public string Message { get; set; } = "Success";
    public T? Data { get; set; }
}

public class SupportApiPagedResponse<T>
{
    public int Code { get; set; } = 200;
    public bool Success { get; set; } = true;
    public string Message { get; set; } = "Success";
    public PagedResult<T> Data { get; set; } = new();
}

public class PagedResult<T>
{
    public List<T> Items { get; set; } = new();
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public int TotalCount { get; set; } = 0;
    public int TotalPages => PageSize > 0 ? (int)Math.Ceiling(TotalCount / (double)PageSize) : 0;
    public bool HasNextPage => Page < TotalPages;
}
#endregion

#region Support Context DTOs
public class SupportContextDto
{
    public int UserId { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string UserEmail { get; set; } = string.Empty;
    public string UserType { get; set; } = string.Empty;
    public decimal WalletBalance { get; set; }

    public SupportBookingSummaryDto? ActiveBooking { get; set; }
    public List<SupportBookingSummaryDto> RecentBookings { get; set; } = new();
    public List<SupportVehicleSummaryDto> Vehicles { get; set; } = new();
    public List<SupportTransactionSummaryDto> RecentTransactions { get; set; } = new();
    public List<SupportParkingSpotSummaryDto> OwnedSpots { get; set; } = new();
    public List<SupportAccessLogSummaryDto> RecentAccessLogs { get; set; } = new();
}

public class SupportBookingSummaryDto
{
    public int BookingId { get; set; }
    public string BookingReference { get; set; } = string.Empty;
    public string ParkingSpotName { get; set; } = string.Empty;
    public string PropertyAddress { get; set; } = string.Empty;
    public string VehiclePlate { get; set; } = string.Empty;
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal TotalAmount { get; set; }
    public bool HasIoTDevice { get; set; }
    public string? IoTDeviceStatus { get; set; }
    public DateTime? LastHeartbeatAt { get; set; }
}

public class SupportVehicleSummaryDto
{
    public int VehicleId { get; set; }
    public string LicensePlate { get; set; } = string.Empty;
    public string MakeModel { get; set; } = string.Empty;
    public string Color { get; set; } = string.Empty;
}

public class SupportTransactionSummaryDto
{
    public int TransactionId { get; set; }
    public string ReferenceNumber { get; set; } = string.Empty;
    public string TransactionType { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string PaymentMethod { get; set; } = string.Empty;
    public string TransactionStatus { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}

public class SupportParkingSpotSummaryDto
{
    public int ParkingSpotId { get; set; }
    public string SpotNumber { get; set; } = string.Empty;
    public string PropertyName { get; set; } = string.Empty;
    public string AvailabilityStatus { get; set; } = string.Empty;
    public bool HasIoTDevice { get; set; }
    public string? IoTStatus { get; set; }
}

public class SupportAccessLogSummaryDto
{
    public int AccessLogId { get; set; }
    public string Actions { get; set; } = string.Empty;
    public DateTime AccessedAt { get; set; }
    public int? BookingId { get; set; }
    public int? IoTDeviceId { get; set; }
}
#endregion

#region Workflows DTOs
public class SupportWorkflowDefinitionDto
{
    public string WorkflowKey { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Version { get; set; } = "1.0";
    public string Category { get; set; } = string.Empty;
    public string EstimatedResponseTime { get; set; } = "Immediate";
    public List<WorkflowQuestionOptionDto> Options { get; set; } = new();
    public List<WorkflowStepDto> Steps { get; set; } = new();
}

public class WorkflowQuestionOptionDto
{
    public string Key { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}

public class WorkflowStepDto
{
    public string StepId { get; set; } = string.Empty;
    public string Question { get; set; } = string.Empty;
    public string Type { get; set; } = "choice"; // choice, boolean, text, select_booking
    public List<string> AllowedAnswers { get; set; } = new();
    public bool Required { get; set; } = true;
}

public class ExecuteWorkflowRunRequestDto
{
    public Dictionary<string, string> Answers { get; set; } = new();
    public int? BookingId { get; set; }
    public int? VehicleId { get; set; }
    public string? ClientRequestId { get; set; }
}

public class WorkflowRunResultDto
{
    public int WorkflowRunId { get; set; }
    public string RunReference { get; set; } = string.Empty;
    public string Outcome { get; set; } = string.Empty;
    public string Priority { get; set; } = "P2";
    public string AssignedTeam { get; set; } = "CustomerSupport";
    public List<WorkflowCheckItemDto> Checks { get; set; } = new();
    public SupportTicketSummaryDto? Ticket { get; set; }
    public OperationalIncidentSummaryDto? Incident { get; set; }
    public DisputeCustomerSummaryDto? Dispute { get; set; }
    public string CustomerMessage { get; set; } = string.Empty;
    public DateTime CompletedAt { get; set; } = DateTime.UtcNow;
}

public class WorkflowCheckItemDto
{
    public string Name { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty; // valid, failed, offline, normal
    public string Detail { get; set; } = string.Empty;
}
#endregion

#region Conversations DTOs
public class CreateConversationRequestDto
{
    public string? Channel { get; set; } = "LiveChat";
    public string? InitialMessage { get; set; }
    public int? BookingId { get; set; }
    public int? ParkingSpotId { get; set; }
}

public class ConversationDto
{
    public int ConversationId { get; set; }
    public string ConversationReference { get; set; } = string.Empty;
    public int CustomerUserId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string CustomerEmail { get; set; } = string.Empty;
    public string Channel { get; set; } = "LiveChat";
    public string Status { get; set; } = string.Empty;
    public int? AssignedAdminUserId { get; set; }
    public string? AssignedAdminName { get; set; }
    public int? CurrentBookingId { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? LastMessageAt { get; set; }
    public DateTime? ClosedAt { get; set; }
    public string? LastMessageSnippet { get; set; }
    public int MessageCount { get; set; }
}

public class ConversationDetailDto : ConversationDto
{
    public string? ContextSnapshotJson { get; set; }
    public string? ClosingReason { get; set; }
    public List<ConversationMessageDto> Messages { get; set; } = new();
    public List<SupportTicketSummaryDto> ConvertedTickets { get; set; } = new();
}

public class ConversationMessageDto
{
    public int MessageId { get; set; }
    public int ConversationId { get; set; }
    public int? SenderUserId { get; set; }
    public string SenderName { get; set; } = string.Empty;
    public string SenderRole { get; set; } = "Customer";
    public string MessageType { get; set; } = "Customer";
    public string Body { get; set; } = string.Empty;
    public bool IsInternal { get; set; }
    public DateTime CreatedAt { get; set; }
    public List<SupportAttachmentDto> Attachments { get; set; } = new();
}

public class SendConversationMessageRequestDto
{
    public string Message { get; set; } = string.Empty;
    public bool IsInternal { get; set; } = false;
    public List<IFormFile>? Attachments { get; set; }
}

public class CloseConversationRequestDto
{
    public string? Reason { get; set; }
}

public class ConvertConversationToTicketRequestDto
{
    public string? Subject { get; set; }
    public string? Category { get; set; }
    public string? Priority { get; set; }
    public string? AssignedTeam { get; set; }
    public string? InternalSummary { get; set; }
}
#endregion

#region Support Tickets DTOs
public class CreateSupportTicketRequestDto
{
    public string Subject { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? Category { get; set; }
    public string? Priority { get; set; }
    public int? BookingId { get; set; }
    public int? ParkingSpotId { get; set; }
    public int? VehicleId { get; set; }
    public int? ConversationId { get; set; }
    public List<IFormFile>? Attachments { get; set; }
}

public class AdminCreateSupportTicketRequestDto
{
    public int? CustomerUserId { get; set; }
    public string? CustomerEmail { get; set; }
    public string Subject { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string Category { get; set; } = "General";
    public string Priority { get; set; } = "P2";
    public string AssignedTeam { get; set; } = "CustomerSupport";
    public int? AssignedAdminUserId { get; set; }
    public int? BookingId { get; set; }
    public int? ParkingSpotId { get; set; }
    public int? VehicleId { get; set; }
    public int? ConversationId { get; set; }
    public string? InternalSummary { get; set; }
    public List<IFormFile>? Attachments { get; set; }
}

public class SupportTicketDto
{
    public int TicketId { get; set; }
    public string TicketReference { get; set; } = string.Empty;
    public string TicketType { get; set; } = "Preset";
    public string Source { get; set; } = "QuickHelp";
    public string Category { get; set; } = "General";
    public string Priority { get; set; } = "P2";
    public string Status { get; set; } = "New";
    public string Subject { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    public int CustomerUserId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string CustomerEmail { get; set; } = string.Empty;
    public string CustomerRole { get; set; } = "Commuter";

    public int? AssignedAdminUserId { get; set; }
    public string? AssignedAdminName { get; set; }
    public string? AssignedTeam { get; set; }

    public int? ConversationId { get; set; }
    public string? ConversationReference { get; set; }

    public int? WorkflowRunId { get; set; }
    public string? WorkflowRunReference { get; set; }

    public int? BookingId { get; set; }
    public string? BookingReference { get; set; }

    public int? ParkingSpotId { get; set; }
    public int? VehicleId { get; set; }

    public int? OperationalIncidentId { get; set; }
    public string? IncidentReference { get; set; }

    public int? DisputeInvestigationId { get; set; }
    public string? DisputeReference { get; set; }

    public DateTime? AcceptedAt { get; set; }
    public DateTime? FirstResponseAt { get; set; }
    public DateTime? FirstResponseDueAt { get; set; }
    public DateTime? ResolvedAt { get; set; }
    public DateTime? ResolutionDueAt { get; set; }
    public DateTime? ClosedAt { get; set; }
    public string? ResolutionCode { get; set; }
    public string? InternalSummary { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public List<SupportTicketMessageDto> Messages { get; set; } = new();
    public List<SupportAttachmentDto> Attachments { get; set; } = new();
    public List<SupportAuditEventDto> AuditTimeline { get; set; } = new();
}

public class SupportTicketSummaryDto
{
    public int TicketId { get; set; }
    public string TicketReference { get; set; } = string.Empty;
    public string TicketType { get; set; } = "Preset";
    public string Source { get; set; } = "QuickHelp";
    public string Category { get; set; } = "General";
    public string Priority { get; set; } = "P2";
    public string Status { get; set; } = "New";
    public string Subject { get; set; } = string.Empty;
    public int CustomerUserId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string CustomerEmail { get; set; } = string.Empty;
    public string? AssignedAdminName { get; set; }
    public string? AssignedTeam { get; set; }
    public int? BookingId { get; set; }
    public int? OperationalIncidentId { get; set; }
    public int? DisputeInvestigationId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? FirstResponseDueAt { get; set; }
    public DateTime? ResolutionDueAt { get; set; }
    public int MessageCount { get; set; }
}

public class SupportTicketMessageDto
{
    public int MessageId { get; set; }
    public int TicketId { get; set; }
    public int? SenderUserId { get; set; }
    public string SenderName { get; set; } = string.Empty;
    public string SenderRole { get; set; } = "Customer";
    public string Body { get; set; } = string.Empty;
    public bool IsInternal { get; set; }
    public DateTime CreatedAt { get; set; }
    public List<SupportAttachmentDto> Attachments { get; set; } = new();
}

public class SendTicketMessageRequestDto
{
    public string Message { get; set; } = string.Empty;
    public bool IsInternal { get; set; } = false;
    public List<IFormFile>? Attachments { get; set; }
}

public class TicketTransitionRequestDto
{
    public string ToStatus { get; set; } = string.Empty;
    public string? Reason { get; set; }
    public string? ResolutionCode { get; set; }
}

public class AssignTicketRequestDto
{
    public int? AssignedAdminUserId { get; set; }
    public string? AssignedTeam { get; set; }
}

public class ReopenTicketRequestDto
{
    public string Reason { get; set; } = string.Empty;
}

[JsonConverter(typeof(LinkIncidentRequestDtoConverter))]
public class LinkIncidentRequestDto
{
    public int? IncidentId { get; set; }
    public string? IncidentReference { get; set; } // e.g. "INC-2026-52563"
}

public class LinkIncidentRequestDtoConverter : JsonConverter<LinkIncidentRequestDto>
{
    public override LinkIncidentRequestDto? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null) return null;
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            if (reader.TokenType == JsonTokenType.String)
            {
                var val = reader.GetString();
                return int.TryParse(val, out var parsedInt)
                    ? new LinkIncidentRequestDto { IncidentId = parsedInt, IncidentReference = val }
                    : new LinkIncidentRequestDto { IncidentReference = val };
            }
            if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out var parsedNum))
            {
                return new LinkIncidentRequestDto { IncidentId = parsedNum };
            }
            return null;
        }

        var dto = new LinkIncidentRequestDto();
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject) break;
            if (reader.TokenType == JsonTokenType.PropertyName)
            {
                var propName = reader.GetString();
                reader.Read();
                if (string.Equals(propName, "incidentId", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(propName, "id", StringComparison.OrdinalIgnoreCase))
                {
                    if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out var num))
                    {
                        dto.IncidentId = num;
                    }
                    else if (reader.TokenType == JsonTokenType.String)
                    {
                        var str = reader.GetString();
                        if (int.TryParse(str, out var parsedNum))
                        {
                            dto.IncidentId = parsedNum;
                        }
                        else
                        {
                            dto.IncidentReference = str;
                        }
                    }
                }
                else if (string.Equals(propName, "incidentReference", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(propName, "incidentIdentifier", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(propName, "reference", StringComparison.OrdinalIgnoreCase))
                {
                    dto.IncidentReference = reader.GetString();
                }
            }
        }
        return dto;
    }

    public override void Write(Utf8JsonWriter writer, LinkIncidentRequestDto value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        if (value.IncidentId.HasValue) writer.WriteNumber("incidentId", value.IncidentId.Value);
        if (!string.IsNullOrWhiteSpace(value.IncidentReference)) writer.WriteString("incidentReference", value.IncidentReference);
        writer.WriteEndObject();
    }
}

[JsonConverter(typeof(LinkTicketRequestDtoConverter))]
public class LinkTicketRequestDto
{
    public int? TicketId { get; set; }
    public string? TicketReference { get; set; } // e.g. "TKT-2026-35442"
}

public class LinkTicketRequestDtoConverter : JsonConverter<LinkTicketRequestDto>
{
    public override LinkTicketRequestDto? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null) return null;
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            if (reader.TokenType == JsonTokenType.String)
            {
                var val = reader.GetString();
                return int.TryParse(val, out var parsedInt)
                    ? new LinkTicketRequestDto { TicketId = parsedInt, TicketReference = val }
                    : new LinkTicketRequestDto { TicketReference = val };
            }
            if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out var parsedNum))
            {
                return new LinkTicketRequestDto { TicketId = parsedNum };
            }
            return null;
        }

        var dto = new LinkTicketRequestDto();
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject) break;
            if (reader.TokenType == JsonTokenType.PropertyName)
            {
                var propName = reader.GetString();
                reader.Read();
                if (string.Equals(propName, "ticketId", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(propName, "id", StringComparison.OrdinalIgnoreCase))
                {
                    if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out var num))
                    {
                        dto.TicketId = num;
                    }
                    else if (reader.TokenType == JsonTokenType.String)
                    {
                        var str = reader.GetString();
                        if (int.TryParse(str, out var parsedNum))
                        {
                            dto.TicketId = parsedNum;
                        }
                        else
                        {
                            dto.TicketReference = str;
                        }
                    }
                }
                else if (string.Equals(propName, "ticketReference", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(propName, "ticketIdentifier", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(propName, "reference", StringComparison.OrdinalIgnoreCase))
                {
                    dto.TicketReference = reader.GetString();
                }
            }
        }
        return dto;
    }

    public override void Write(Utf8JsonWriter writer, LinkTicketRequestDto value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        if (value.TicketId.HasValue) writer.WriteNumber("ticketId", value.TicketId.Value);
        if (!string.IsNullOrWhiteSpace(value.TicketReference)) writer.WriteString("ticketReference", value.TicketReference);
        writer.WriteEndObject();
    }
}

[JsonConverter(typeof(LinkDisputeRequestDtoConverter))]
public class LinkDisputeRequestDto
{
    public int? DisputeId { get; set; }
    public string? DisputeReference { get; set; } // e.g. "DSP-2026-12345"
}

public class LinkDisputeRequestDtoConverter : JsonConverter<LinkDisputeRequestDto>
{
    public override LinkDisputeRequestDto? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null) return null;
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            if (reader.TokenType == JsonTokenType.String)
            {
                var val = reader.GetString();
                return int.TryParse(val, out var parsedInt)
                    ? new LinkDisputeRequestDto { DisputeId = parsedInt, DisputeReference = val }
                    : new LinkDisputeRequestDto { DisputeReference = val };
            }
            if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out var parsedNum))
            {
                return new LinkDisputeRequestDto { DisputeId = parsedNum };
            }
            return null;
        }

        var dto = new LinkDisputeRequestDto();
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject) break;
            if (reader.TokenType == JsonTokenType.PropertyName)
            {
                var propName = reader.GetString();
                reader.Read();
                if (string.Equals(propName, "disputeId", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(propName, "id", StringComparison.OrdinalIgnoreCase))
                {
                    if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out var num))
                    {
                        dto.DisputeId = num;
                    }
                    else if (reader.TokenType == JsonTokenType.String)
                    {
                        var str = reader.GetString();
                        if (int.TryParse(str, out var parsedNum))
                        {
                            dto.DisputeId = parsedNum;
                        }
                        else
                        {
                            dto.DisputeReference = str;
                        }
                    }
                }
                else if (string.Equals(propName, "disputeReference", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(propName, "disputeIdentifier", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(propName, "reference", StringComparison.OrdinalIgnoreCase))
                {
                    dto.DisputeReference = reader.GetString();
                }
            }
        }
        return dto;
    }

    public override void Write(Utf8JsonWriter writer, LinkDisputeRequestDto value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        if (value.DisputeId.HasValue) writer.WriteNumber("disputeId", value.DisputeId.Value);
        if (!string.IsNullOrWhiteSpace(value.DisputeReference)) writer.WriteString("disputeReference", value.DisputeReference);
        writer.WriteEndObject();
    }
}
#endregion

#region Incidents DTOs
public class CreateIncidentRequestDto
{
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string IncidentType { get; set; } = "GateFailure";
    public string Priority { get; set; } = "P1";
    public string? AssignedTeam { get; set; } = "ParkingOperations";
    public int? AssignedUserId { get; set; }
    public int? PropertyId { get; set; }
    public int? ParkingSpotId { get; set; }
    public int? IoTDeviceId { get; set; }
    public string? Source { get; set; } = "Admin";
    public int? InitialTicketId { get; set; }
    public string? InitialTicketReference { get; set; }
}

public class OperationalIncidentDto
{
    public int IncidentId { get; set; }
    public string IncidentReference { get; set; } = string.Empty;
    public string IncidentType { get; set; } = "GateFailure";
    public string Priority { get; set; } = "P1";
    public string Status { get; set; } = "Open";
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int? PropertyId { get; set; }
    public string? PropertyName { get; set; }
    public int? ParkingSpotId { get; set; }
    public string? SpotNumber { get; set; }
    public int? IoTDeviceId { get; set; }
    public string? Esp32Serial { get; set; }
    public string Source { get; set; } = "QuickHelp";
    public string AssignedTeam { get; set; } = "ParkingOperations";
    public int? AssignedUserId { get; set; }
    public string? AssignedUserName { get; set; }
    public int AffectedCustomerCount { get; set; }
    public DateTime? AcknowledgedAt { get; set; }
    public DateTime? ResolvedAt { get; set; }
    public DateTime? ClosedAt { get; set; }
    public int EscalationLevel { get; set; }
    public DateTime? NextEscalationAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public List<SupportTicketSummaryDto> LinkedTickets { get; set; } = new();
    public List<SupportAuditEventDto> AuditTimeline { get; set; } = new();
    public List<SupportNotificationAttemptDto> NotificationAttempts { get; set; } = new();
}

public class OperationalIncidentSummaryDto
{
    public int IncidentId { get; set; }
    public string IncidentReference { get; set; } = string.Empty;
    public string IncidentType { get; set; } = "GateFailure";
    public string Priority { get; set; } = "P1";
    public string Status { get; set; } = "Open";
    public string Title { get; set; } = string.Empty;
    public string AssignedTeam { get; set; } = "ParkingOperations";
    public int AffectedCustomerCount { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class IncidentTransitionRequestDto
{
    public string ToStatus { get; set; } = string.Empty; // Monitoring, Resolved, Closed
    public string? Reason { get; set; }
}

public class AssignIncidentRequestDto
{
    public int? AssignedUserId { get; set; }
    public string? AssignedTeam { get; set; }
}

public class AccessOverrideRequestDto
{
    public int BookingId { get; set; }
    public string Reason { get; set; } = string.Empty;
    public bool Confirmation { get; set; } = true;
    public string? CommandId { get; set; }
}

public class AccessOverrideResultDto
{
    public bool Success { get; set; }
    public string CommandId { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public int BookingId { get; set; }
    public int? IoTDeviceId { get; set; }
    public DateTime ExecutedAt { get; set; }
}
#endregion

#region Disputes DTOs
public class DisputeCustomerDto
{
    public int DisputeId { get; set; }
    public string DisputeReference { get; set; } = string.Empty;
    public string DisputeType { get; set; } = "Refund";
    public string Status { get; set; } = "Opened";
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "MYR";
    public string Reason { get; set; } = string.Empty;
    public int? TicketId { get; set; }
    public int? BookingId { get; set; }
    public string? Decision { get; set; }
    public string? DecisionReason { get; set; }
    public DateTime? DecidedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public List<DisputeEvidenceDto> Evidences { get; set; } = new();
}

public class DisputeCustomerSummaryDto
{
    public int DisputeId { get; set; }
    public string DisputeReference { get; set; } = string.Empty;
    public string DisputeType { get; set; } = "Refund";
    public string Status { get; set; } = "Opened";
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "MYR";
    public DateTime CreatedAt { get; set; }
}

public class DisputeAdminDto : DisputeCustomerDto
{
    public int CustomerUserId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string CustomerEmail { get; set; } = string.Empty;
    public string AssignedTeam { get; set; } = "Payments";
    public int? AssignedUserId { get; set; }
    public string? AssignedUserName { get; set; }
    public int? PaymentId { get; set; }
    public int? TransactionId { get; set; }
    public int? DecidedByUserId { get; set; }
    public string? DecidedByUserName { get; set; }
    public List<SupportAuditEventDto> AuditTimeline { get; set; } = new();
}

public class DisputeEvidenceDto
{
    public int DisputeEvidenceId { get; set; }
    public int DisputeId { get; set; }
    public string EvidenceType { get; set; } = "Receipt";
    public string FileName { get; set; } = string.Empty;
    public string FileUrl { get; set; } = string.Empty;
    public string UploadedRole { get; set; } = "Customer";
    public string UploadedByName { get; set; } = string.Empty;
    public bool IsVerified { get; set; }
    public string? Description { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class UploadDisputeEvidenceRequestDto
{
    public string EvidenceType { get; set; } = "Receipt";
    public string? Description { get; set; }
    public IFormFile File { get; set; } = null!;
}

public class RequestDisputeEvidenceRequestDto
{
    public string RequiredEvidence { get; set; } = string.Empty;
    public string CustomerMessage { get; set; } = string.Empty;
    public DateTime? Deadline { get; set; }
}

public class DisputeDecisionRequestDto
{
    public string Decision { get; set; } = "ApproveReversal"; // ApproveReversal, Decline, NeedMoreInfo
    public string Reason { get; set; } = string.Empty;
    public decimal? Amount { get; set; }
}

public class AssignDisputeRequestDto
{
    public int? AssignedUserId { get; set; }
    public string? AssignedTeam { get; set; }
    public string? Status { get; set; }
}
#endregion

#region Attachments, Audit & Notifications
public class SupportAttachmentDto
{
    public int AttachmentId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string FileUrl { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public bool IsPrivate { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class SupportAuditEventDto
{
    public int AuditEventId { get; set; }
    public string ObjectType { get; set; } = string.Empty;
    public int ObjectId { get; set; }
    public string ObjectReference { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public int? ActorUserId { get; set; }
    public string ActorName { get; set; } = string.Empty;
    public string ActorRole { get; set; } = "System";
    public string? PreviousState { get; set; }
    public string? NewState { get; set; }
    public string? Detail { get; set; }
    public DateTime Timestamp { get; set; }
}

public class SupportNotificationAttemptDto
{
    public int NotificationAttemptId { get; set; }
    public string Channel { get; set; } = "Push";
    public string Recipient { get; set; } = string.Empty;
    public string? Subject { get; set; }
    public string Message { get; set; } = string.Empty;
    public string Status { get; set; } = "Sent";
    public int AttemptCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? SentAt { get; set; }
}
#endregion

#region On-Call & Dashboard DTOs
public class SupportDashboardDto
{
    public int WaitingConversationsCount { get; set; }
    public int OpenTicketsCount { get; set; }
    public int ActiveIncidentsCount { get; set; }
    public int OpenDisputesCount { get; set; }
    public int SlaRiskTicketsCount { get; set; }
    public List<SupportTicketSummaryDto> RecentTickets { get; set; } = new();
    public List<OperationalIncidentSummaryDto> RecentIncidents { get; set; } = new();
    public List<ConversationDto> ActiveConversations { get; set; } = new();
}

public class SupportOnCallStatusDto
{
    public int ScheduleId { get; set; }
    public string ShiftName { get; set; } = string.Empty;
    public DateTime ShiftStart { get; set; }
    public DateTime ShiftEnd { get; set; }
    public OnCallResponderDto? PrimaryResponder { get; set; }
    public OnCallResponderDto? BackupResponder { get; set; }
    public OnCallResponderDto? Supervisor { get; set; }
    public OnCallResponderDto? OperationsManager { get; set; }
    public List<string> ActiveChannels { get; set; } = new();
    public SupportOnCallPolicyDto Policy { get; set; } = new();
}

public class OnCallResponderDto
{
    public int UserId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
}

public class SupportOnCallPolicyDto
{
    public int P0BackupDelayMinutes { get; set; } = 2;
    public int P0SupervisorDelayMinutes { get; set; } = 5;
    public int P0ManagerDelayMinutes { get; set; } = 15;
    public int P1BackupDelayMinutes { get; set; } = 5;
    public int P1SupervisorDelayMinutes { get; set; } = 15;
    public int P1ManagerDelayMinutes { get; set; } = 30;
    public string NotificationChannels { get; set; } = "Push,SMS,Phone,Email";
    public bool AutoEscalateEnabled { get; set; } = true;
}

public class UpdateOnCallPolicyRequestDto : SupportOnCallPolicyDto
{
}

public class TestOnCallNotificationRequestDto
{
    public string Channel { get; set; } = "Push"; // Push, SMS, Phone, Email
    public string? TargetRecipient { get; set; }
    public string? Message { get; set; }
}

public class TestNotificationResultDto
{
    public bool Success { get; set; }
    public string Channel { get; set; } = string.Empty;
    public string Recipient { get; set; } = string.Empty;
    public string Status { get; set; } = "Sent";
    public string Detail { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
#endregion
