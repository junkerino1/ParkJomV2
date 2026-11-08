using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ParkJomV2.Data;
using ParkJomV2.DTOs;
using ParkJomV2.Models;
using ParkJomV2.Models.Enums;
using ParkJomV2.Services;
using ParkJomV2.Services.Notifications.Channels;

namespace ParkJomV2.Controllers;

[ApiController]
[Authorize(Policy = "AdminOnly")]
[Route("api/admin/management/account")]
public sealed class AdminPropertyManagementAccountsController : ControllerBase
{
    private const int MaximumPageSize = 100;
    private readonly ApplicationDbContext _db;
    private readonly AccessLogService _accessLogs;
    private readonly JwtTokenService _jwt;
    private readonly IEmailSender _emailSender;
    private readonly IConfiguration _configuration;
    private readonly PasswordHasher<PropertyManagement> _passwordHasher = new();

    public AdminPropertyManagementAccountsController(
        ApplicationDbContext db,
        AccessLogService accessLogs,
        JwtTokenService jwt,
        IEmailSender emailSender,
        IConfiguration configuration)
    {
        _db = db;
        _accessLogs = accessLogs;
        _jwt = jwt;
        _emailSender = emailSender;
        _configuration = configuration;
    }

    /// <summary>Creates a PropertyManagement account assigned to one property.</summary>
    /// <remarks>Admin only. The server sends a 14-day, single-use invitation link to the new management account's email. The recipient sets their password through the existing frontend, which posts the invitation token and chosen password to POST /api/management/invitation/activate.</remarks>
    /// <response code="201">Account created and invitation email accepted by the SMTP server.</response>
    /// <response code="502">Invitation email could not be sent; the account was not created.</response>
    /// <response code="400">The request is invalid or the email is already in use.</response>
    /// <response code="401">Authentication is required.</response>
    /// <response code="403">The caller is not an administrator.</response>
    /// <response code="404">The property was not found.</response>
    [HttpPost]
    [ProducesResponseType(typeof(PropertyManagementAccountMutationDTO), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(InvitationDeliveryErrorResponse), StatusCodes.Status502BadGateway)]
    public async Task<ActionResult<PropertyManagementAccountMutationDTO>> Create(
        [FromBody] PropertyManagementAccountCreateRequest request,
        CancellationToken cancellationToken)
    {
        var email = request.Email.Trim();
        var emailExists = await _db.PropertyManagements.AnyAsync(item => item.Email == email, cancellationToken);
        if (emailExists)
            return BadRequest(Error(StatusCodes.Status400BadRequest, "EMAIL_ALREADY_IN_USE", "An account with this email already exists."));

        var property = await _db.Properties
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.PropertyId == request.PropertyId, cancellationToken);
        if (property is null)
            return NotFound(Error(StatusCodes.Status404NotFound, "PROPERTY_NOT_FOUND", "The selected property was not found."));

        var now = DateTime.UtcNow;
        var account = new PropertyManagement
        {
            Email = email,
            Username = request.Username.Trim(),
            PropertyId = request.PropertyId,
            AccountStatus = "Active",
            MustChangePassword = true,
            CredentialVersion = 1,
            CreatedAt = now,
            UpdatedAt = now
        };
        // The manager cannot sign in until the invitation is consumed. This random
        // unrecoverable value keeps a non-empty hash without creating a temp password.
        account.PasswordHash = HashUninitializedPassword(account);

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        _db.PropertyManagements.Add(account);
        await _db.SaveChangesAsync(cancellationToken);
        var emailResult = await SendInvitationEmailAsync(account, cancellationToken);
        if (!emailResult.Success || emailResult.Simulated)
        {
            await transaction.RollbackAsync(cancellationToken);
            _db.Entry(account).State = EntityState.Detached;
            var errorCode = emailResult.ErrorCode ?? (emailResult.Simulated ? "SMTP_SIMULATED" : "INVITATION_EMAIL_FAILED");
            var details = emailResult.Error ?? "The email sender did not confirm delivery.";
            await _accessLogs.LogAsync(User, "CreatePropertyManagementAccount", false,
                $"PropertyId={property.PropertyId}; InvitationEmailErrorCode={errorCode}; Details={details}");
            return StatusCode(StatusCodes.Status502BadGateway, InvitationError(errorCode, details));
        }

        await transaction.CommitAsync(cancellationToken);
        await _accessLogs.LogAsync(User, "CreatePropertyManagementAccount", true,
            $"ManagerAccountId={account.ManagementAccountId}; PropertyId={property.PropertyId}; InvitationEmailStatus=sent.");

        return CreatedAtAction(nameof(GetById), new { managementAccountId = account.ManagementAccountId },
            MapMutation(account, property, "sent"));
    }

    /// <summary>Lists PropertyManagement accounts with optional property/status filters and pagination.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(PropertyManagementAccountListResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PropertyManagementAccountListResponse>> List(
        [FromQuery] int? propertyId,
        [FromQuery] string? status,
        [FromQuery, Range(1, int.MaxValue)] int page = 1,
        [FromQuery, Range(1, MaximumPageSize)] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        if (!TryNormalizeStatus(status, out var normalizedStatus))
            return BadRequest(Error(StatusCodes.Status400BadRequest, "INVALID_STATUS", "status must be Active, Deactivated, or Suspended."));

        var query = _db.PropertyManagements.AsNoTracking().AsQueryable();
        if (propertyId.HasValue) query = query.Where(item => item.PropertyId == propertyId.Value);
        if (normalizedStatus is not null) query = query.Where(item => item.AccountStatus == normalizedStatus);

        var totalCount = await query.CountAsync(cancellationToken);
        var accounts = await query
            .Include(item => item.Property)
            .OrderBy(item => item.Email)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return Ok(new PropertyManagementAccountListResponse
        {
            Code = StatusCodes.Status200OK,
            Success = true,
            Message = "PropertyManagement accounts retrieved.",
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
            Data = accounts.Select(item => Map(item, item.Property)).ToList()
        });
    }

    /// <summary>Returns one PropertyManagement account without exposing credential data.</summary>
    [HttpGet("{managementAccountId:int}")]
    [ProducesResponseType(typeof(PropertyManagementAccountDTO), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PropertyManagementAccountDTO>> GetById(int managementAccountId, CancellationToken cancellationToken)
    {
        var account = await LoadAccountAsync(managementAccountId, cancellationToken);
        if (account is null) return NotFound(Error(StatusCodes.Status404NotFound, "ACCOUNT_NOT_FOUND", "PropertyManagement account was not found."));
        await _accessLogs.LogAsync(User, "ViewPropertyManagementAccount", true, $"ManagerAccountId={managementAccountId}; PropertyId={account.PropertyId}");
        return Ok(Map(account, account.Property));
    }

    /// <summary>Updates a manager's email, username, assigned property, or status; can resend a first-time invitation.</summary>
    /// <remarks>Set resendInvitation to true to invalidate previous invitation links and email a fresh 14-day link. If a pending account's email address changes, a replacement invitation is sent to the new address.</remarks>
    [HttpPost("{managementAccountId:int}")]
    [ProducesResponseType(typeof(PropertyManagementAccountMutationDTO), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(InvitationDeliveryErrorResponse), StatusCodes.Status502BadGateway)]
    public async Task<ActionResult<PropertyManagementAccountMutationDTO>> Update(
        int managementAccountId,
        [FromBody] PropertyManagementAccountUpdateRequest request,
        CancellationToken cancellationToken)
    {
        var account = await LoadAccountAsync(managementAccountId, cancellationToken);
        if (account is null) return NotFound(Error(StatusCodes.Status404NotFound, "ACCOUNT_NOT_FOUND", "PropertyManagement account was not found."));

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);

        if (request.PropertyId.HasValue)
        {
            var propertyExists = await _db.Properties.AnyAsync(item => item.PropertyId == request.PropertyId.Value, cancellationToken);
            if (!propertyExists) return NotFound(Error(StatusCodes.Status404NotFound, "PROPERTY_NOT_FOUND", "The selected property was not found."));
            account.PropertyId = request.PropertyId.Value;
        }

        var emailChanged = false;
        if (!string.IsNullOrWhiteSpace(request.Email))
        {
            var email = request.Email.Trim();
            var emailExists = await _db.PropertyManagements.AnyAsync(item => item.ManagementAccountId != managementAccountId && item.Email == email, cancellationToken);
            if (emailExists) return BadRequest(Error(StatusCodes.Status400BadRequest, "EMAIL_ALREADY_IN_USE", "An account with this email already exists."));
            emailChanged = !string.Equals(account.Email, email, StringComparison.OrdinalIgnoreCase);
            account.Email = email;
        }

        if (request.Username is not null) account.Username = request.Username.Trim();
        var statusChanged = false;
        if (!string.IsNullOrWhiteSpace(request.Status))
        {
            if (!TryNormalizeStatus(request.Status, out var status) || status is null)
                return BadRequest(Error(StatusCodes.Status400BadRequest, "INVALID_STATUS", "status must be Active, Deactivated, or Suspended."));
            statusChanged = !string.Equals(account.AccountStatus, status, StringComparison.OrdinalIgnoreCase);
            account.AccountStatus = status;
        }
        var returningToActive = statusChanged && string.Equals(account.AccountStatus, "Active", StringComparison.OrdinalIgnoreCase);
        var issueInvitation = request.ResendInvitation ||
                              (account.MustChangePassword && (emailChanged || returningToActive));
        if (issueInvitation && !string.Equals(account.AccountStatus, "Active", StringComparison.OrdinalIgnoreCase))
            return Conflict(Error(StatusCodes.Status409Conflict, "ACCOUNT_NOT_ACTIVE", "Activate the management account before sending an invitation."));

        if (issueInvitation)
        {
            account.PasswordHash = HashUninitializedPassword(account);
            account.MustChangePassword = true;
        }
        var now = DateTime.UtcNow;
        if (emailChanged || statusChanged || issueInvitation)
            account.CredentialVersion++;
        account.UpdatedAt = now;
        await _db.SaveChangesAsync(cancellationToken);
        var emailResult = issueInvitation ? await SendInvitationEmailAsync(account, cancellationToken) : null;
        if (emailResult is not null && (!emailResult.Success || emailResult.Simulated))
        {
            await transaction.RollbackAsync(cancellationToken);
            _db.ChangeTracker.Clear();
            var errorCode = emailResult.ErrorCode ?? (emailResult.Simulated ? "SMTP_SIMULATED" : "INVITATION_EMAIL_FAILED");
            var details = emailResult.Error ?? "The email sender did not confirm delivery.";
            await _accessLogs.LogAsync(User, "UpdatePropertyManagementAccount", false,
                $"ManagerAccountId={managementAccountId}; InvitationEmailErrorCode={errorCode}; Details={details}");
            return StatusCode(StatusCodes.Status502BadGateway, InvitationError(errorCode, details));
        }
        await transaction.CommitAsync(cancellationToken);
        var emailStatus = emailResult is null ? null : "sent";
        await _accessLogs.LogAsync(User, "UpdatePropertyManagementAccount", true,
            $"ManagerAccountId={managementAccountId}; PropertyId={account.PropertyId}; InvitationEmailStatus={emailStatus ?? "not_requested"}.");
        await _db.Entry(account).Reference(item => item.Property).LoadAsync(cancellationToken);
        return Ok(MapMutation(account, account.Property, emailStatus));
    }

    private Task<PropertyManagement?> LoadAccountAsync(int managementAccountId, CancellationToken cancellationToken)
        => _db.PropertyManagements
            .Include(item => item.Property)
            .FirstOrDefaultAsync(item => item.ManagementAccountId == managementAccountId, cancellationToken);

    private static PropertyManagementAccountDTO Map(PropertyManagement account, Property property)
        => new()
        {
            ManagementAccountId = account.ManagementAccountId,
            Email = account.Email,
            Username = account.Username,
            PropertyId = account.PropertyId,
            PropertyName = property.PropertyName,
            AccountStatus = account.AccountStatus,
            MustChangePassword = account.MustChangePassword,
            CreatedAt = account.CreatedAt,
            LastLoginAt = account.LastLoginAt
        };

    private static PropertyManagementAccountMutationDTO MapMutation(
        PropertyManagement account,
        Property property,
        string? invitationEmailStatus)
    {
        var mapped = Map(account, property);
        return new PropertyManagementAccountMutationDTO
        {
            ManagementAccountId = mapped.ManagementAccountId,
            Email = mapped.Email,
            Username = mapped.Username,
            PropertyId = mapped.PropertyId,
            PropertyName = mapped.PropertyName,
            AccountStatus = mapped.AccountStatus,
            MustChangePassword = mapped.MustChangePassword,
            CreatedAt = mapped.CreatedAt,
            LastLoginAt = mapped.LastLoginAt,
            InvitationEmailStatus = invitationEmailStatus
        };
    }

    private string HashUninitializedPassword(PropertyManagement account)
    {
        var unknownSecret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        return _passwordHasher.HashPassword(account, unknownSecret);
    }

    private async Task<EmailSendResult> SendInvitationEmailAsync(PropertyManagement account, CancellationToken cancellationToken)
    {
        var token = _jwt.GenerateManagementInvitationToken(account, out var expiresAt);
        var invitationPageUrl = _configuration["PropertyManagement:InvitationPageUrl"];
        if (string.IsNullOrWhiteSpace(invitationPageUrl))
        {
            var appBaseUrl = _configuration["Notifications:AppBaseUrl"]
                             ?? _configuration["ApiBaseUrl"]
                             ?? $"{Request.Scheme}://{Request.Host}";
            invitationPageUrl = $"{appBaseUrl.TrimEnd('/')}/management/invitation";
        }

        var separator = invitationPageUrl.Contains('?') ? "&" : "?";
        var invitationUrl = $"{invitationPageUrl}{separator}token={Uri.EscapeDataString(token)}";
        var safeName = WebUtility.HtmlEncode(account.Username);
        var safeEmail = WebUtility.HtmlEncode(account.Email);
        var safeUrl = WebUtility.HtmlEncode(invitationUrl);
        var subject = "Your ParkJom PropertyManagement invitation";
        var htmlBody = $"""
            <p>Hello {safeName},</p>
            <p>An administrator created a ParkJom property management account for you.</p>
            <p><a href="{safeUrl}">Accept invitation and set your password</a></p>
            <p>This invitation link expires on {expiresAt:yyyy-MM-dd HH:mm} UTC and can only be used once.</p>
            <p>Account: {safeEmail}</p>
            <p>If you were not expecting this invitation, you can ignore this email.</p>
            """;
        var textBody = $"Hello {account.Username},\n\nAn administrator created a ParkJom property management account for you.\n\nAccept the invitation and set your password: {invitationUrl}\n\nThis link expires on {expiresAt:yyyy-MM-dd HH:mm} UTC and can only be used once.\nAccount: {account.Email}\n";

        Console.WriteLine($"[DEBUG] Sending invitation email to {account.Email} with link: {invitationUrl}");

        try
        {
            var result = await _emailSender.SendEmailAsync(account.Email, account.Username, subject, htmlBody, textBody, cancellationToken);
            return result;
        }
        catch (Exception ex)
        {
            return EmailSendResult.Failed($"Invitation email failed ({ex.GetType().Name}). Check the application logs for details.", "INVITATION_EMAIL_FAILED");
        }
    }

    private static InvitationDeliveryErrorResponse InvitationError(string errorCode, string details)
        => new()
        {
            Code = StatusCodes.Status502BadGateway,
            Success = false,
            ErrorCode = errorCode,
            Message = "The invitation email could not be sent. The account change was rolled back.",
            Details = details
        };

    private static bool TryNormalizeStatus(string? status, out string? normalized)
    {
        normalized = null;
        if (string.IsNullOrWhiteSpace(status)) return true;
        var valid = new[] { "Active", "Deactivated", "Suspended" };
        normalized = valid.FirstOrDefault(candidate => string.Equals(candidate, status.Trim(), StringComparison.OrdinalIgnoreCase));
        return normalized is not null;
    }

    private static ErrorResponse Error(int code, string errorCode, string message)
        => new() { Code = code, Success = false, Message = message };
}
