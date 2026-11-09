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

    /// <summary>Creates a PropertyManagement account, or sends a fresh activation/password-reset link if the same account already exists for that property.</summary>
    /// <remarks>Admin only. A duplicate email and matching property reissues a 14-day single-use link. For an already activated account, this starts a password reset and invalidates existing sessions. The recipient uses the existing frontend and POST /api/management/invitation/activate.</remarks>
    /// <response code="200">The existing account was sent a fresh activation or password-reset link.</response>
    /// <response code="201">Account created and invitation email accepted by the SMTP server.</response>
    /// <response code="502">Invitation email could not be sent; the account was not created.</response>
    /// <response code="400">The request is invalid.</response>
    /// <response code="409">The email belongs to an account assigned to another property or an inactive account.</response>
    /// <response code="401">Authentication is required.</response>
    /// <response code="403">The caller is not an administrator.</response>
    /// <response code="404">The property was not found.</response>
    [HttpPost]
    [ProducesResponseType(typeof(PropertyManagementAccountMutationDTO), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(PropertyManagementAccountMutationDTO), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(InvitationDeliveryErrorResponse), StatusCodes.Status502BadGateway)]
    public async Task<ActionResult<PropertyManagementAccountMutationDTO>> Create(
        [FromBody] PropertyManagementAccountCreateRequest request,
        CancellationToken cancellationToken)
    {
        var email = request.Email.Trim();
        var property = await _db.Properties
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.PropertyId == request.PropertyId, cancellationToken);
        if (property is null)
            return NotFound(Error(StatusCodes.Status404NotFound, "PROPERTY_NOT_FOUND", "The selected property was not found."));

        var existingAccount = await _db.PropertyManagements
            .FirstOrDefaultAsync(item => item.Email == email, cancellationToken);
        if (existingAccount is not null)
        {
            if (existingAccount.PropertyId != property.PropertyId)
                return Conflict(Error(StatusCodes.Status409Conflict, "EMAIL_ALREADY_IN_USE", "This email already belongs to a management account assigned to another property."));
            if (!string.Equals(existingAccount.AccountStatus, "Active", StringComparison.OrdinalIgnoreCase))
                return Conflict(Error(StatusCodes.Status409Conflict, "ACCOUNT_NOT_ACTIVE", "The management account is not active, so an activation or reset link cannot be sent."));

            var isPasswordReset = !existingAccount.MustChangePassword;
            await using var resetTransaction = await _db.Database.BeginTransactionAsync(cancellationToken);
            existingAccount.PasswordHash = HashUninitializedPassword(existingAccount);
            existingAccount.MustChangePassword = true;
            existingAccount.CredentialVersion++;
            existingAccount.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);

            var duplicateEmailResult = await SendInvitationEmailAsync(existingAccount, cancellationToken, isPasswordReset);
            if (!duplicateEmailResult.Success || duplicateEmailResult.Simulated)
            {
                await resetTransaction.RollbackAsync(cancellationToken);
                _db.ChangeTracker.Clear();
                var errorCode = duplicateEmailResult.ErrorCode ?? (duplicateEmailResult.Simulated ? "SMTP_SIMULATED" : "INVITATION_EMAIL_FAILED");
                var details = duplicateEmailResult.Error ?? "The email sender did not confirm delivery.";
                await _accessLogs.LogAsync(User, "ResendManagementAccountAccessLink", false,
                    $"ManagerAccountId={existingAccount.ManagementAccountId}; ErrorCode={errorCode}; Details={details}");
                return StatusCode(StatusCodes.Status502BadGateway, InvitationError(errorCode, details));
            }

            await resetTransaction.CommitAsync(cancellationToken);
            await _accessLogs.LogAsync(User, isPasswordReset ? "ResetManagementAccountPasswordByEmail" : "ResendManagementAccountInvitation", true,
                $"ManagerAccountId={existingAccount.ManagementAccountId}; PropertyId={property.PropertyId}; InvitationEmailStatus=sent.");
            return Ok(MapMutation(existingAccount, property, "sent"));
        }

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

        var isPasswordReset = issueInvitation && !account.MustChangePassword;
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
        var emailResult = issueInvitation ? await SendInvitationEmailAsync(account, cancellationToken, isPasswordReset) : null;
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

    private async Task<EmailSendResult> SendInvitationEmailAsync(
        PropertyManagement account,
        CancellationToken cancellationToken,
        bool isPasswordReset = false)
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
        var apiBaseUrl = _configuration["ApiBaseUrl"]?.TrimEnd('/')
                         ?? $"{Request.Scheme}://{Request.Host}";
        var logoUrl = _configuration["PropertyManagement:EmailLogoUrl"];
        if (string.IsNullOrWhiteSpace(logoUrl))
            logoUrl = $"{apiBaseUrl}/images/parking_app_logo_transparent.png";
        var safeLogoUrl = WebUtility.HtmlEncode(logoUrl);
        var subject = isPasswordReset
            ? "Reset your ParkJom Property Management password"
            : "You’re invited to ParkJom Property Management";
        var expiryText = $"{expiresAt:dd MMM yyyy, HH:mm} UTC";
        var preheader = isPasswordReset
            ? "A password reset link for your ParkJom management account."
            : "Your ParkJom property management account is ready to activate.";
        var emailLabel = isPasswordReset ? "Password reset" : "Account invitation";
        var heading = isPasswordReset ? "Reset your password" : "You’re invited to ParkJom";
        var intro = isPasswordReset
            ? "An administrator requested a password reset for your property management account. Use the secure link below to choose a new password. This will sign out existing sessions."
            : "An administrator created a property management account for you. Set your password to activate your access to property bookings and parking access activity.";
        var buttonText = isPasswordReset ? "Set a new password" : "Accept invitation";
        var unexpectedMessage = isPasswordReset
            ? "If you weren’t expecting this reset, contact your ParkJom administrator. You’ll need to set a new password before signing in again."
            : "If you weren’t expecting this invitation, you can ignore this email. Your account won’t be activated until you set a password.";
        var htmlBody = $"""
            <!doctype html>
            <html lang="en">
            <head>
              <meta charset="utf-8">
              <meta name="viewport" content="width=device-width,initial-scale=1">
              <meta name="color-scheme" content="dark">
              <title>ParkJom account access</title>
            </head>
            <body style="margin:0;padding:0;background-color:#080d1b;font-family:Arial,Helvetica,sans-serif;color:#e8eefc;">
              <div style="display:none;max-height:0;overflow:hidden;opacity:0;color:transparent;">
                {preheader}
              </div>
              <table role="presentation" width="100%" cellspacing="0" cellpadding="0" border="0" style="background-color:#080d1b;padding:32px 12px;">
                <tr>
                  <td align="center">
                    <table role="presentation" width="100%" cellspacing="0" cellpadding="0" border="0" style="max-width:560px;background-color:#111a2e;border:1px solid #263451;border-radius:16px;overflow:hidden;">
                      <tr>
                        <td align="center" style="padding:25px 30px 22px;background-color:#0b1224;border-bottom:1px solid #1d2a44;">
                          <img src="{safeLogoUrl}" alt="ParkJom" width="88" height="88" style="display:block;width:88px;height:88px;margin:0 auto 10px;border:0;outline:none;text-decoration:none;">
                          <div style="font-size:12px;line-height:18px;font-weight:700;letter-spacing:1.5px;text-transform:uppercase;color:#9fb5d8;">Property Management</div>
                        </td>
                      </tr>
                      <tr>
                        <td style="padding:34px 30px 30px;">
                          <div style="display:inline-block;padding:6px 10px;border-radius:999px;background-color:#10294d;color:#60a5fa;font-size:11px;line-height:14px;font-weight:700;letter-spacing:0.8px;text-transform:uppercase;">{emailLabel}</div>
                          <h1 style="margin:18px 0 12px;font-size:26px;line-height:34px;font-weight:700;letter-spacing:-0.5px;color:#f8fbff;">{heading}</h1>
                          <p style="margin:0 0 14px;font-size:15px;line-height:24px;color:#d9e3f5;">Hello {safeName},</p>
                          <p style="margin:0 0 22px;font-size:15px;line-height:24px;color:#afbdd5;">{intro}</p>
                          <table role="presentation" width="100%" cellspacing="0" cellpadding="0" border="0" style="margin:0 0 24px;background-color:#0c1427;border:1px solid #263451;border-radius:10px;">
                            <tr>
                              <td style="padding:14px 16px;">
                                <div style="font-size:11px;line-height:16px;font-weight:700;letter-spacing:0.8px;text-transform:uppercase;color:#8ba1c4;">Your account</div>
                                <div style="margin-top:4px;font-size:14px;line-height:21px;font-weight:600;color:#eff6ff;">{safeEmail}</div>
                              </td>
                            </tr>
                          </table>
                          <table role="presentation" cellspacing="0" cellpadding="0" border="0" style="margin:0 auto 22px;">
                            <tr>
                              <td align="center" style="border-radius:9px;background-color:#2563eb;">
                                <a href="{safeUrl}" target="_blank" style="display:inline-block;padding:14px 26px;border:1px solid #3b82f6;border-radius:9px;color:#ffffff;text-decoration:none;font-size:15px;line-height:20px;font-weight:700;">{buttonText}&nbsp;&nbsp;→</a>
                              </td>
                            </tr>
                          </table>
                          <p style="margin:0 0 8px;text-align:center;font-size:13px;line-height:20px;color:#9aabc5;">This link can be used once and expires on <strong style="color:#e2eaff;">{expiryText}</strong>.</p>
                          <p style="margin:0 0 8px;text-align:center;font-size:12px;line-height:19px;color:#91a3c0;">If the button doesn’t work, copy this link into your browser:</p>
                          <p style="margin:0 0 22px;text-align:center;font-size:12px;line-height:19px;word-break:break-all;"><a href="{safeUrl}" style="color:#38bdf8;text-decoration:underline;">{safeUrl}</a></p>
                          <div style="height:1px;background-color:#263451;margin:0 0 18px;"></div>
                          <p style="margin:0;font-size:13px;line-height:20px;color:#91a3c0;">{unexpectedMessage}</p>
                        </td>
                      </tr>
                      <tr>
                        <td style="padding:18px 30px;background-color:#0b1224;border-top:1px solid #1d2a44;text-align:center;">
                          <p style="margin:0;font-size:12px;line-height:18px;color:#7184a4;">ParkJom · Smart parking, made simpler</p>
                        </td>
                      </tr>
                    </table>
                  </td>
                </tr>
              </table>
            </body>
            </html>
            """;
        var textBody = isPasswordReset
            ? $"PARKJOM — PASSWORD RESET\n\nHello {account.Username},\n\nAn administrator requested a password reset for your property management account. Use this secure link to choose a new password: {invitationUrl}\n\nThis link can be used once and expires on {expiryText}. Completing the reset signs out existing sessions.\nAccount: {account.Email}\n\nIf you weren’t expecting this request, contact ParkJom support.\n"
            : $"PARKJOM — PROPERTY MANAGEMENT INVITATION\n\nHello {account.Username},\n\nAn administrator created a property management account for you. Set your password to activate access to property bookings and parking access activity.\n\nAccept invitation: {invitationUrl}\n\nThis link can be used once and expires on {expiryText}.\nAccount: {account.Email}\n\nIf you weren’t expecting this invitation, you can ignore this email.\n";

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
