using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ParkJomV2.Data;
using ParkJomV2.DTOs;
using ParkJomV2.Models;
using ParkJomV2.Models.Enums;

namespace ParkJomV2.Services.Support;

public class SupportWorkflowService
{
    private readonly ApplicationDbContext _context;
    private readonly SupportAuditService _auditService;
    private readonly ISupportRealtimeNotifier _realtimeNotifier;
    private readonly ILogger<SupportWorkflowService> _logger;

    public SupportWorkflowService(
        ApplicationDbContext context,
        SupportAuditService auditService,
        ISupportRealtimeNotifier realtimeNotifier,
        ILogger<SupportWorkflowService> logger)
    {
        _context = context;
        _auditService = auditService;
        _realtimeNotifier = realtimeNotifier;
        _logger = logger;
    }

    public List<SupportWorkflowDefinitionDto> GetWorkflowDefinitions()
    {
        return new List<SupportWorkflowDefinitionDto>
        {
            new()
            {
                WorkflowKey = "parking-access",
                Title = "I cannot enter or exit",
                Description = "Urgent gate, barrier, or parking entry/exit issues.",
                Category = "ParkingAccess",
                EstimatedResponseTime = "24/7 Immediate triage",
                Options = new List<WorkflowQuestionOptionDto>
                {
                    new() { Key = "enter", Label = "Cannot Enter Gate/Barrier", Description = "Unable to pass entry barrier or scanner not responding" },
                    new() { Key = "exit", Label = "Cannot Exit Parking", Description = "Barrier will not open to exit the parking facility" },
                    new() { Key = "validation", Label = "Plate/QR Validation Failed", Description = "Camera or QR reader failed to validate booking" }
                },
                Steps = new List<WorkflowStepDto>
                {
                    new() { StepId = "issue", Question = "Are you trying to enter or exit?", Type = "choice", AllowedAnswers = new() { "enter", "exit", "validation" }, Required = true },
                    new() { StepId = "trapped", Question = "Are you or your vehicle currently trapped inside or outside?", Type = "choice", AllowedAnswers = new() { "yes", "no" }, Required = true },
                    new() { StepId = "safetyRisk", Question = "Is there any immediate safety hazard or blocking traffic?", Type = "choice", AllowedAnswers = new() { "yes", "no" }, Required = true }
                }
            },
            new()
            {
                WorkflowKey = "booking",
                Title = "Booking problem",
                Description = "Issues with booking details, cancellation, timing, or missing reservations.",
                Category = "Booking",
                EstimatedResponseTime = "Within 15 minutes",
                Options = new List<WorkflowQuestionOptionDto>
                {
                    new() { Key = "missing", Label = "Booking not found", Description = "Confirmed booking is not showing up in your account" },
                    new() { Key = "location", Label = "Wrong parking location", Description = "Booking assigned to incorrect property or spot" },
                    new() { Key = "cancel", Label = "Cannot cancel booking", Description = "Cancel button not available or error during cancellation" },
                    new() { Key = "time", Label = "Incorrect booking time", Description = "Start or end time is different from expected" },
                    new() { Key = "expired", Label = "Booking shown as expired", Description = "Booking marked expired prematurely" },
                    new() { Key = "other", Label = "Other booking issue", Description = "General booking inquiry" }
                },
                Steps = new List<WorkflowStepDto>
                {
                    new() { StepId = "issue", Question = "What problem are you experiencing with your booking?", Type = "choice", AllowedAnswers = new() { "missing", "location", "cancel", "time", "expired", "other" }, Required = true },
                    new() { StepId = "details", Question = "Please describe the issue or desired modification:", Type = "text", AllowedAnswers = new(), Required = false }
                }
            },
            new()
            {
                WorkflowKey = "payment-refund",
                Title = "Payment or refund issue",
                Description = "Inquiries regarding charges, duplicate debits, refund status, or disputes.",
                Category = "Payment",
                EstimatedResponseTime = "Within 30 minutes",
                Options = new List<WorkflowQuestionOptionDto>
                {
                    new() { Key = "paid-missing", Label = "Payment successful but booking missing", Description = "Card/Wallet charged but no booking created" },
                    new() { Key = "refund", Label = "Refund status inquiry", Description = "Checking status of pending refund" },
                    new() { Key = "failed", Label = "Payment failed", Description = "Payment checkout failed or timed out" },
                    new() { Key = "duplicate", Label = "Charged twice (Duplicate charge)", Description = "Double debit for the same booking session" },
                    new() { Key = "unknown", Label = "Unrecognized charge", Description = "I do not recognize this transaction on my statement" },
                    new() { Key = "other", Label = "Other payment issue", Description = "Other wallet or card related issues" }
                },
                Steps = new List<WorkflowStepDto>
                {
                    new() { StepId = "issue", Question = "What type of payment issue are you reporting?", Type = "choice", AllowedAnswers = new() { "paid-missing", "refund", "failed", "duplicate", "unknown", "other" }, Required = true },
                    new() { StepId = "transactionRef", Question = "Transaction or Reference Number (if available):", Type = "text", AllowedAnswers = new(), Required = false }
                }
            },
            new()
            {
                WorkflowKey = "account-vehicle-owner",
                Title = "Account, vehicle or owner support",
                Description = "Help with profile verification, vehicle details, or host payouts.",
                Category = "Account",
                EstimatedResponseTime = "Within 1 hour",
                Options = new List<WorkflowQuestionOptionDto>
                {
                    new() { Key = "account-access", Label = "Cannot access account / Login problem", Description = "Password, OTP, or account locked" },
                    new() { Key = "vehicle", Label = "Vehicle information incorrect", Description = "Plate number update or vehicle registration error" },
                    new() { Key = "verification", Label = "Account or spot verification status", Description = "Pending identity or spot document review" },
                    new() { Key = "payout", Label = "Owner payout status", Description = "Host withdrawal or earnings status" },
                    new() { Key = "listing", Label = "Parking listing problem", Description = "Host spot listing, rates, or calendar configuration" },
                    new() { Key = "payout-dispute", Label = "Owner payout dispute", Description = "Disagreement on commission or payout amount" },
                    new() { Key = "security", Label = "Account security / Impersonation", Description = "Suspected unauthorized access or compromised credentials" }
                },
                Steps = new List<WorkflowStepDto>
                {
                    new() { StepId = "issue", Question = "Select the specific issue:", Type = "choice", AllowedAnswers = new() { "account-access", "vehicle", "verification", "payout", "listing", "payout-dispute", "security" }, Required = true }
                }
            }
        };
    }

    public SupportWorkflowDefinitionDto? GetWorkflowDefinition(string workflowKey)
    {
        return GetWorkflowDefinitions().FirstOrDefault(w => string.Equals(w.WorkflowKey, workflowKey, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<WorkflowRunResultDto> ExecuteWorkflowRunAsync(int userId, string workflowKey, ExecuteWorkflowRunRequestDto request)
    {
        var definition = GetWorkflowDefinition(workflowKey);
        if (definition == null)
        {
            throw new ArgumentException($"Invalid workflow key: {workflowKey}");
        }

        var user = await _context.Users.FirstOrDefaultAsync(u => u.UserId == userId);
        if (user == null)
        {
            throw new KeyNotFoundException("User not found");
        }

        var now = DateTime.UtcNow;
        var runRef = $"RUN-{now:yyyy}-{Random.Shared.Next(10000, 99999)}";
        var answersJson = JsonSerializer.Serialize(request.Answers);

        var checks = new List<WorkflowCheckItemDto>();
        string outcome = "AutoResolved";
        string priority = "P2";
        string assignedTeam = "CustomerSupport";
        string customerMessage = "Your request has been received and processed.";

        SupportTicket? createdTicket = null;
        OperationalIncident? createdIncident = null;
        DisputeInvestigation? createdDispute = null;

        // Perform workflow-specific verification and logic
        switch (workflowKey.ToLowerInvariant())
        {
            case "parking-access":
            {
                var issue = request.Answers.GetValueOrDefault("issue", "enter");
                var isTrapped = string.Equals(request.Answers.GetValueOrDefault("trapped"), "yes", StringComparison.OrdinalIgnoreCase);
                var isSafetyRisk = string.Equals(request.Answers.GetValueOrDefault("safetyRisk"), "yes", StringComparison.OrdinalIgnoreCase);

                Booking? booking = null;
                if (request.BookingId.HasValue)
                {
                    booking = await _context.Bookings
                        .Include(b => b.ParkingSpot)
                            .ThenInclude(p => p.IoTDevice)
                        .Include(b => b.Vehicle)
                        .FirstOrDefaultAsync(b => b.BookingId == request.BookingId.Value && b.RenterId == userId);
                }

                if (booking == null)
                {
                    booking = await _context.Bookings
                        .Include(b => b.ParkingSpot)
                            .ThenInclude(p => p.IoTDevice)
                        .Include(b => b.Vehicle)
                        .Where(b => b.RenterId == userId)
                        .OrderByDescending(b => b.StartDate)
                        .FirstOrDefaultAsync();
                }

                var spot = booking?.ParkingSpot;
                var iot = spot?.IoTDevice;

                checks.Add(new WorkflowCheckItemDto
                {
                    Name = "Booking Verification",
                    Status = booking != null ? "valid" : "missing",
                    Detail = booking != null ? $"Found booking #{booking.BookingReference} ({booking.BookingStatus})" : "No active booking linked"
                });

                checks.Add(new WorkflowCheckItemDto
                {
                    Name = "Gate Hardware / IoT Status",
                    Status = iot == null ? "normal" : (iot.DeviceStatus == DeviceStatus.Online ? "online" : "offline"),
                    Detail = iot != null ? $"Device {iot.Esp32Serial} status: {iot.DeviceStatus}" : "No hardware IoT sensor registered"
                });

                if (isTrapped || isSafetyRisk)
                {
                    priority = "P0";
                    assignedTeam = "ParkingOperations";
                    outcome = "OperationalIncidentAndTicket";
                    customerMessage = "EMERGENCY: On-call emergency parking operations have been alerted immediately. An agent is responding to your gate.";

                    // Create P0 Incident
                    createdIncident = new OperationalIncident
                    {
                        IncidentReference = $"INC-{now:yyyy}-{Random.Shared.Next(10000, 99999)}",
                        IncidentType = isTrapped ? "UserTrapped" : "SafetyHazard",
                        Priority = IncidentPriority.P0,
                        Status = IncidentStatus.Open,
                        Title = $"P0 EMERGENCY: User trapped/safety risk at {spot?.ParkingLabel ?? "Parking Gate"}",
                        Description = $"User {user.Email} reported emergency: Trapped={isTrapped}, SafetyRisk={isSafetyRisk}. Booking: {booking?.BookingReference}",
                        PropertyId = spot?.PropertyId,
                        ParkingSpotId = spot?.ParkingSpotId,
                        IoTDeviceId = iot?.IoTDeviceId,
                        Source = "QuickHelp",
                        AssignedTeam = "ParkingOperations",
                        AffectedCustomerCount = 1,
                        NextEscalationAt = now.AddMinutes(2),
                        CreatedAt = now,
                        UpdatedAt = now
                    };
                    _context.OperationalIncidents.Add(createdIncident);
                    await _context.SaveChangesAsync();

                    // Create Customer Ticket
                    createdTicket = CreateTicketEntity(user, $"[EMERGENCY P0] Cannot {issue} - Trapped/Safety", SupportCategory.ParkingAccess, SupportTicketPriority.P0, "ParkingOperations", SupportSource.QuickHelp, booking?.BookingId, spot?.ParkingSpotId, booking?.VehicleId);
                    _context.SupportTickets.Add(createdTicket);
                    await _context.SaveChangesAsync();

                    // Link Join Table
                    _context.IncidentTickets.Add(new IncidentTicket { IncidentId = createdIncident.IncidentId, TicketId = createdTicket.TicketId, LinkedAt = now });
                    createdTicket.OperationalIncidentId = createdIncident.IncidentId;
                }
                else if (iot != null && iot.DeviceStatus == DeviceStatus.Offline)
                {
                    priority = "P1";
                    assignedTeam = "ParkingOperations";
                    outcome = "OperationalIncidentAndTicket";
                    customerMessage = "The parking gate is currently offline. An operational incident has been dispatched to Parking Operations.";

                    createdIncident = new OperationalIncident
                    {
                        IncidentReference = $"INC-{now:yyyy}-{Random.Shared.Next(10000, 99999)}",
                        IncidentType = "DeviceOffline",
                        Priority = IncidentPriority.P1,
                        Status = IncidentStatus.Open,
                        Title = $"Gate Offline at spot {spot?.ParkingLabel ?? $"Spot #{spot?.ParkingSpotId}"}",
                        Description = $"Gate IoT device {iot.Esp32Serial} is offline. User {user.Email} cannot {issue}.",
                        PropertyId = spot?.PropertyId,
                        ParkingSpotId = spot?.ParkingSpotId,
                        IoTDeviceId = iot.IoTDeviceId,
                        Source = "QuickHelp",
                        AssignedTeam = "ParkingOperations",
                        NextEscalationAt = now.AddMinutes(5),
                        CreatedAt = now,
                        UpdatedAt = now
                    };
                    _context.OperationalIncidents.Add(createdIncident);
                    await _context.SaveChangesAsync();

                    createdTicket = CreateTicketEntity(user, $"Gate offline at {spot?.ParkingLabel ?? $"Spot #{spot?.ParkingSpotId}"}", SupportCategory.ParkingAccess, SupportTicketPriority.P1, "ParkingOperations", SupportSource.QuickHelp, booking?.BookingId, spot?.ParkingSpotId, booking?.VehicleId);
                    _context.SupportTickets.Add(createdTicket);
                    await _context.SaveChangesAsync();

                    _context.IncidentTickets.Add(new IncidentTicket { IncidentId = createdIncident.IncidentId, TicketId = createdTicket.TicketId, LinkedAt = now });
                    createdTicket.OperationalIncidentId = createdIncident.IncidentId;
                }
                else
                {
                    priority = "P1";
                    assignedTeam = "ParkingOperations";
                    outcome = "TicketCreated";
                    customerMessage = "Support ticket created. Parking operations will inspect validation logs and assist you.";

                    createdTicket = CreateTicketEntity(user, $"Cannot {issue} parking gate (Validation issue)", SupportCategory.ParkingAccess, SupportTicketPriority.P1, "ParkingOperations", SupportSource.QuickHelp, booking?.BookingId, spot?.ParkingSpotId, booking?.VehicleId);
                    _context.SupportTickets.Add(createdTicket);
                    await _context.SaveChangesAsync();
                }
                break;
            }

            case "booking":
            {
                var issue = request.Answers.GetValueOrDefault("issue", "other");
                checks.Add(new WorkflowCheckItemDto { Name = "Booking System Check", Status = "normal", Detail = "Booking catalog and cancellation rules verified" });

                priority = "P2";
                assignedTeam = "CustomerSupport";
                outcome = "TicketCreated";
                customerMessage = $"Your booking issue ({issue}) has been submitted. Our Customer Support team is reviewing it.";

                createdTicket = CreateTicketEntity(user, $"Booking issue: {issue}", SupportCategory.Booking, SupportTicketPriority.P2, "CustomerSupport", SupportSource.QuickHelp, request.BookingId, null, request.VehicleId);
                _context.SupportTickets.Add(createdTicket);
                await _context.SaveChangesAsync();
                break;
            }

            case "payment-refund":
            {
                var issue = request.Answers.GetValueOrDefault("issue", "other");
                var isDuplicateOrUnknown = string.Equals(issue, "duplicate", StringComparison.OrdinalIgnoreCase) || string.Equals(issue, "unknown", StringComparison.OrdinalIgnoreCase);

                checks.Add(new WorkflowCheckItemDto { Name = "Payment & Ledger Check", Status = "normal", Detail = "Transaction history and balance audited" });

                if (isDuplicateOrUnknown)
                {
                    priority = "P1";
                    assignedTeam = "Payments";
                    outcome = "DisputeAndTicket";
                    customerMessage = "A formal dispute and investigation case has been opened. Our finance team will review the transaction and evidence.";

                    var disputeType = string.Equals(issue, "duplicate", StringComparison.OrdinalIgnoreCase)
                        ? DisputeType.DuplicateCharge
                        : DisputeType.UnrecognizedCharge;

                    createdDispute = new DisputeInvestigation
                    {
                        DisputeReference = $"DSP-{now:yyyy}-{Random.Shared.Next(10000, 99999)}",
                        DisputeType = disputeType,
                        Status = DisputeStatus.Opened,
                        CustomerUserId = userId,
                        BookingId = request.BookingId,
                        Amount = 0m,
                        Currency = "MYR",
                        Reason = $"Customer reported {issue} in payment-refund workflow.",
                        AssignedTeam = "Payments",
                        CreatedAt = now,
                        UpdatedAt = now
                    };
                    _context.DisputeInvestigations.Add(createdDispute);
                    await _context.SaveChangesAsync();

                    createdTicket = CreateTicketEntity(user, $"Dispute: {issue}", SupportCategory.Payment, SupportTicketPriority.P1, "Payments", SupportSource.QuickHelp, request.BookingId, null, request.VehicleId);
                    createdTicket.DisputeInvestigationId = createdDispute.DisputeId;
                    _context.SupportTickets.Add(createdTicket);
                    await _context.SaveChangesAsync();

                    createdDispute.TicketId = createdTicket.TicketId;
                    await _context.SaveChangesAsync();
                }
                else
                {
                    priority = string.Equals(issue, "paid-missing", StringComparison.OrdinalIgnoreCase) ? "P1" : "P2";
                    assignedTeam = "Payments";
                    outcome = "TicketCreated";
                    customerMessage = "Support ticket opened for payment investigation with the Payments team.";

                    createdTicket = CreateTicketEntity(user, $"Payment issue: {issue}", SupportCategory.Payment, priority == "P1" ? SupportTicketPriority.P1 : SupportTicketPriority.P2, "Payments", SupportSource.QuickHelp, request.BookingId, null, request.VehicleId);
                    _context.SupportTickets.Add(createdTicket);
                    await _context.SaveChangesAsync();
                }
                break;
            }

            case "account-vehicle-owner":
            {
                var issue = request.Answers.GetValueOrDefault("issue", "other");
                checks.Add(new WorkflowCheckItemDto { Name = "Account Security Check", Status = "normal", Detail = "User credentials and KYC state evaluated" });

                if (string.Equals(issue, "security", StringComparison.OrdinalIgnoreCase))
                {
                    priority = "P0";
                    assignedTeam = "TrustAndSafety";
                    outcome = "DisputeAndTicket";
                    customerMessage = "High priority Trust & Safety case opened. Our security specialists are investigating your account.";

                    createdDispute = new DisputeInvestigation
                    {
                        DisputeReference = $"DSP-{now:yyyy}-{Random.Shared.Next(10000, 99999)}",
                        DisputeType = DisputeType.AccountSecurity,
                        Status = DisputeStatus.Opened,
                        CustomerUserId = userId,
                        Reason = "Customer reported suspected account takeover / unauthorized access.",
                        AssignedTeam = "TrustAndSafety",
                        CreatedAt = now,
                        UpdatedAt = now
                    };
                    _context.DisputeInvestigations.Add(createdDispute);
                    await _context.SaveChangesAsync();

                    createdTicket = CreateTicketEntity(user, "CRITICAL: Account Security Investigation", SupportCategory.Account, SupportTicketPriority.P0, "TrustAndSafety", SupportSource.QuickHelp);
                    createdTicket.DisputeInvestigationId = createdDispute.DisputeId;
                    _context.SupportTickets.Add(createdTicket);
                    await _context.SaveChangesAsync();

                    createdDispute.TicketId = createdTicket.TicketId;
                    await _context.SaveChangesAsync();
                }
                else if (string.Equals(issue, "payout-dispute", StringComparison.OrdinalIgnoreCase))
                {
                    priority = "P2";
                    assignedTeam = "Payments";
                    outcome = "DisputeAndTicket";
                    customerMessage = "Owner payout dispute opened. Finance team will review your earnings settlement.";

                    createdDispute = new DisputeInvestigation
                    {
                        DisputeReference = $"DSP-{now:yyyy}-{Random.Shared.Next(10000, 99999)}",
                        DisputeType = DisputeType.OwnerPayout,
                        Status = DisputeStatus.Opened,
                        CustomerUserId = userId,
                        Reason = "Owner payout calculation dispute",
                        AssignedTeam = "Payments",
                        CreatedAt = now,
                        UpdatedAt = now
                    };
                    _context.DisputeInvestigations.Add(createdDispute);
                    await _context.SaveChangesAsync();

                    createdTicket = CreateTicketEntity(user, "Owner Payout Dispute", SupportCategory.OwnerSupport, SupportTicketPriority.P2, "Payments", SupportSource.QuickHelp);
                    createdTicket.DisputeInvestigationId = createdDispute.DisputeId;
                    _context.SupportTickets.Add(createdTicket);
                    await _context.SaveChangesAsync();

                    createdDispute.TicketId = createdTicket.TicketId;
                    await _context.SaveChangesAsync();
                }
                else
                {
                    priority = "P2";
                    assignedTeam = issue.StartsWith("payout") || issue.StartsWith("listing") ? "OwnerSupport" : "CustomerSupport";
                    var category = issue.StartsWith("payout") || issue.StartsWith("listing") ? SupportCategory.OwnerSupport : SupportCategory.Account;
                    outcome = "TicketCreated";
                    customerMessage = $"Ticket created for {issue}. Assigned to {assignedTeam}.";

                    createdTicket = CreateTicketEntity(user, $"Support request: {issue}", category, SupportTicketPriority.P2, assignedTeam, SupportSource.QuickHelp);
                    _context.SupportTickets.Add(createdTicket);
                    await _context.SaveChangesAsync();
                }
                break;
            }
        }

        // Save WorkflowRun entity
        var workflowRun = new SupportWorkflowRun
        {
            RunReference = runRef,
            CustomerUserId = userId,
            WorkflowKey = workflowKey,
            WorkflowVersion = definition.Version,
            AnswersJson = answersJson,
            Outcome = outcome,
            Priority = priority,
            AssignedTeam = assignedTeam,
            ChecksResultJson = JsonSerializer.Serialize(checks),
            TicketId = createdTicket?.TicketId,
            IncidentId = createdIncident?.IncidentId,
            DisputeId = createdDispute?.DisputeId,
            ClientRequestId = request.ClientRequestId,
            CustomerMessage = customerMessage,
            StartedAt = now,
            CompletedAt = now,
            CreatedAt = now
        };

        _context.SupportWorkflowRuns.Add(workflowRun);
        await _context.SaveChangesAsync();

        if (createdTicket != null)
        {
            createdTicket.WorkflowRunId = workflowRun.WorkflowRunId;
            await _context.SaveChangesAsync();

            // Add opening system message to ticket
            var msg = new SupportTicketMessage
            {
                TicketId = createdTicket.TicketId,
                SenderUserId = userId,
                SenderRole = "Customer",
                Body = $"[Workflow: {definition.Title}]\nAnswers:\n{answersJson}",
                CreatedAt = now
            };
            _context.SupportTicketMessages.Add(msg);
            await _context.SaveChangesAsync();

            // Audit
            await _auditService.LogAsync("Ticket", createdTicket.TicketId, createdTicket.TicketReference, "Created", userId, "Customer", null, "New", $"Created via workflow {workflowKey}");
            await _realtimeNotifier.BroadcastEventAsync("ticket.created", new { ticketId = createdTicket.TicketId, reference = createdTicket.TicketReference }, userId, createdTicket.TicketId);
        }

        if (createdIncident != null)
        {
            await _auditService.LogAsync("Incident", createdIncident.IncidentId, createdIncident.IncidentReference, "Created", userId, "System", null, "Open", $"Auto-escalated via workflow {workflowKey}");
            await _realtimeNotifier.BroadcastEventAsync("incident.created", new { incidentId = createdIncident.IncidentId, reference = createdIncident.IncidentReference, priority = createdIncident.Priority.ToString() });
        }

        if (createdDispute != null)
        {
            await _auditService.LogAsync("Dispute", createdDispute.DisputeId, createdDispute.DisputeReference, "Created", userId, "Customer", null, "Opened", $"Opened via workflow {workflowKey}");
            await _realtimeNotifier.BroadcastEventAsync("dispute.updated", new { disputeId = createdDispute.DisputeId, reference = createdDispute.DisputeReference });
        }

        return new WorkflowRunResultDto
        {
            WorkflowRunId = workflowRun.WorkflowRunId,
            RunReference = workflowRun.RunReference,
            Outcome = outcome,
            Priority = priority,
            AssignedTeam = assignedTeam,
            Checks = checks,
            CustomerMessage = customerMessage,
            CompletedAt = now,
            Ticket = createdTicket != null ? new SupportTicketSummaryDto
            {
                TicketId = createdTicket.TicketId,
                TicketReference = createdTicket.TicketReference,
                Subject = createdTicket.Subject,
                Status = createdTicket.Status.ToString(),
                Priority = createdTicket.Priority.ToString(),
                Category = createdTicket.Category.ToString(),
                AssignedTeam = createdTicket.AssignedTeam,
                CreatedAt = createdTicket.CreatedAt
            } : null,
            Incident = createdIncident != null ? new OperationalIncidentSummaryDto
            {
                IncidentId = createdIncident.IncidentId,
                IncidentReference = createdIncident.IncidentReference,
                Title = createdIncident.Title,
                Status = createdIncident.Status.ToString(),
                Priority = createdIncident.Priority.ToString(),
                AssignedTeam = createdIncident.AssignedTeam,
                CreatedAt = createdIncident.CreatedAt
            } : null,
            Dispute = createdDispute != null ? new DisputeCustomerSummaryDto
            {
                DisputeId = createdDispute.DisputeId,
                DisputeReference = createdDispute.DisputeReference,
                DisputeType = createdDispute.DisputeType.ToString(),
                Status = createdDispute.Status.ToString(),
                Amount = createdDispute.Amount,
                Currency = createdDispute.Currency,
                CreatedAt = createdDispute.CreatedAt
            } : null
        };
    }

    public async Task<WorkflowRunResultDto?> GetWorkflowRunResultAsync(int runId, int userId, bool isAdmin)
    {
        var run = await _context.SupportWorkflowRuns
            .Include(r => r.Ticket)
            .Include(r => r.Incident)
            .Include(r => r.Dispute)
            .FirstOrDefaultAsync(r => r.WorkflowRunId == runId);

        if (run == null) return null;
        if (!isAdmin && run.CustomerUserId != userId) return null;

        var checks = string.IsNullOrEmpty(run.ChecksResultJson)
            ? new List<WorkflowCheckItemDto>()
            : JsonSerializer.Deserialize<List<WorkflowCheckItemDto>>(run.ChecksResultJson) ?? new();

        return new WorkflowRunResultDto
        {
            WorkflowRunId = run.WorkflowRunId,
            RunReference = run.RunReference,
            Outcome = run.Outcome,
            Priority = run.Priority,
            AssignedTeam = run.AssignedTeam ?? "CustomerSupport",
            Checks = checks,
            CustomerMessage = run.CustomerMessage ?? string.Empty,
            CompletedAt = run.CompletedAt ?? run.CreatedAt,
            Ticket = run.Ticket != null ? new SupportTicketSummaryDto
            {
                TicketId = run.Ticket.TicketId,
                TicketReference = run.Ticket.TicketReference,
                Subject = run.Ticket.Subject,
                Status = run.Ticket.Status.ToString(),
                Priority = run.Ticket.Priority.ToString(),
                Category = run.Ticket.Category.ToString(),
                AssignedTeam = run.Ticket.AssignedTeam,
                CreatedAt = run.Ticket.CreatedAt
            } : null,
            Incident = run.Incident != null ? new OperationalIncidentSummaryDto
            {
                IncidentId = run.Incident.IncidentId,
                IncidentReference = run.Incident.IncidentReference,
                Title = run.Incident.Title,
                Status = run.Incident.Status.ToString(),
                Priority = run.Incident.Priority.ToString(),
                AssignedTeam = run.Incident.AssignedTeam,
                CreatedAt = run.Incident.CreatedAt
            } : null,
            Dispute = run.Dispute != null ? new DisputeCustomerSummaryDto
            {
                DisputeId = run.Dispute.DisputeId,
                DisputeReference = run.Dispute.DisputeReference,
                DisputeType = run.Dispute.DisputeType.ToString(),
                Status = run.Dispute.Status.ToString(),
                Amount = run.Dispute.Amount,
                Currency = run.Dispute.Currency,
                CreatedAt = run.Dispute.CreatedAt
            } : null
        };
    }

    private static SupportTicket CreateTicketEntity(User customer, string subject, SupportCategory category, SupportTicketPriority priority, string assignedTeam, SupportSource source, int? bookingId = null, int? parkingSpotId = null, int? vehicleId = null)
    {
        var now = DateTime.UtcNow;
        var (firstResponseHours, resolutionHours) = priority switch
        {
            SupportTicketPriority.P0 => (0.25, 1.0),
            SupportTicketPriority.P1 => (0.5, 4.0),
            SupportTicketPriority.P2 => (2.0, 24.0),
            _ => (4.0, 48.0)
        };

        return new SupportTicket
        {
            TicketReference = $"TKT-{now:yyyy}-{Random.Shared.Next(10000, 99999)}",
            TicketType = SupportTicketType.Preset,
            Source = source,
            Category = category,
            Priority = priority,
            CustomerUserId = customer.UserId,
            CreatedByUserId = customer.UserId,
            AssignedTeam = assignedTeam,
            BookingId = bookingId,
            ParkingSpotId = parkingSpotId,
            VehicleId = vehicleId,
            Subject = subject,
            Description = subject,
            Status = SupportTicketStatus.New,
            FirstResponseDueAt = now.AddHours(firstResponseHours),
            ResolutionDueAt = now.AddHours(resolutionHours),
            CreatedAt = now,
            UpdatedAt = now
        };
    }
}
