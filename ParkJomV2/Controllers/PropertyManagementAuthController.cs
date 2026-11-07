using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Security.Claims;
using ParkJomV2.Data;
using ParkJomV2.DTOs;
using ParkJomV2.Models;
using ParkJomV2.Services;

namespace ParkJomV2.Controllers;

/// <summary>Standalone PropertyManagement authentication; this login does not use Google OAuth or Users.</summary>
[ApiController]
[Route("api/auth/management")]
public sealed class PropertyManagementAuthController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly JwtTokenService _jwt;
    private readonly CurrentUserService _currentUser;
    private readonly AccessLogService _accessLogs;
    private readonly PasswordHasher<PropertyManagement> _passwordHasher = new();

    public PropertyManagementAuthController(
        ApplicationDbContext db,
        JwtTokenService jwt,
        CurrentUserService currentUser,
        AccessLogService accessLogs)
    {
        _db = db;
        _jwt = jwt;
        _currentUser = currentUser;
        _accessLogs = accessLogs;
    }

    /// <summary>Signs in a standalone PropertyManagement account with email and password.</summary>
    /// <remarks>Only accounts provisioned by an administrator can log in. New accounts must first set a password through their email invitation link.</remarks>
    /// <response code="200">Credentials accepted; returns a manager JWT and assigned property.</response>
    /// <response code="401">Email or password is invalid.</response>
    /// <response code="403">The account is suspended, deactivated, or not activated.</response>
    [AllowAnonymous]
    [HttpPost("login")]
    [ProducesResponseType(typeof(PropertyManagementLoginResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<PropertyManagementLoginResponse>> Login(
        [FromBody] PropertyManagementLoginRequest request,
        CancellationToken cancellationToken)
    {
        var email = request.Email.Trim();
        var account = await _db.PropertyManagements
            .Include(item => item.Property)
            .FirstOrDefaultAsync(item => item.Email.ToUpper() == email.ToUpper(), cancellationToken);

        if (account is null || string.IsNullOrWhiteSpace(account.PasswordHash))
        {
            await AuditAsync(null, null, "Login", false, "Invalid credentials.");
            return Unauthorized(Error("Invalid email or password."));
        }

        if (!string.Equals(account.AccountStatus, "Active", StringComparison.OrdinalIgnoreCase))
        {
            await AuditAsync(account.ManagementAccountId, account.PropertyId, "Login", false,
                $"Account status={account.AccountStatus}.");
            return StatusCode(StatusCodes.Status403Forbidden, Error("This account is suspended or deactivated.", StatusCodes.Status403Forbidden));
        }

        if (account.MustChangePassword)
        {
            await AuditAsync(account.ManagementAccountId, account.PropertyId, "Login", false,
                "Account must be activated using its invitation link.");
            return Unauthorized(Error("Invalid email or password."));
        }

        var verification = _passwordHasher.VerifyHashedPassword(account, account.PasswordHash, request.Password);
        if (verification == PasswordVerificationResult.Failed)
        {
            await AuditAsync(account.ManagementAccountId, account.PropertyId, "Login", false, "Invalid password.");
            return Unauthorized(Error("Invalid email or password."));
        }

        var now = DateTime.UtcNow;
        if (verification == PasswordVerificationResult.SuccessRehashNeeded)
            account.PasswordHash = _passwordHasher.HashPassword(account, request.Password);
        account.LastLoginAt = now;
        account.UpdatedAt = now;
        await _db.SaveChangesAsync(cancellationToken);

        var token = _jwt.GenerateManagementToken(account);
        await AuditAsync(account.ManagementAccountId, account.PropertyId, "Login", true,
            $"mustChangePassword={account.MustChangePassword}.");
        return Ok(new PropertyManagementLoginResponse
        {
            Code = StatusCodes.Status200OK,
            Success = true,
            Message = "Login successful.",
            JwtToken = token,
            MustChangePassword = account.MustChangePassword,
            Data = MapProfile(account)
        });
    }

    /// <summary>Consumes a 14-day invitation link and sets the account's first password.</summary>
    /// <remarks>The frontend reads the token from the invitation URL and posts it with the chosen password. The invitation is single-use; no temporary password or bearer login is required.</remarks>
    /// <response code="200">Password set, invitation consumed, and a manager JWT returned.</response>
    /// <response code="400">The invitation token is invalid or malformed.</response>
    /// <response code="409">The invitation was already used, revoked, or the account is inactive.</response>
    /// <response code="410">The invitation has expired.</response>
    [AllowAnonymous]
    [HttpPost("/api/management/invitation/activate")]
    [ProducesResponseType(typeof(PropertyManagementLoginResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status410Gone)]
    public async Task<ActionResult<PropertyManagementLoginResponse>> ActivateInvitation(
        [FromBody] PropertyManagementInvitationActivationRequest request,
        CancellationToken cancellationToken)
    {
        ClaimsPrincipal invitation;
        try
        {
            invitation = _jwt.ValidateManagementInvitationToken(request.InvitationToken);
        }
        catch (SecurityTokenExpiredException)
        {
            await AuditAsync(null, null, "InvitationActivation", false, "Invitation token expired.");
            return StatusCode(StatusCodes.Status410Gone, Error("The invitation link has expired.", StatusCodes.Status410Gone));
        }
        catch (SecurityTokenException)
        {
            await AuditAsync(null, null, "InvitationActivation", false, "Invitation token was invalid.");
            return BadRequest(Error("The invitation link is invalid.", StatusCodes.Status400BadRequest));
        }

        var idText = invitation.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? invitation.FindFirst("sub")?.Value;
        var versionText = invitation.FindFirst("CredentialVersion")?.Value;
        var invitationEmail = invitation.FindFirst("email")?.Value ?? invitation.FindFirst(ClaimTypes.Email)?.Value;
        if (!int.TryParse(idText, out var accountId) || !int.TryParse(versionText, out var credentialVersion))
        {
            await AuditAsync(null, null, "InvitationActivation", false, "Invitation claims were incomplete.");
            return BadRequest(Error("The invitation link is invalid.", StatusCodes.Status400BadRequest));
        }

        var account = await _db.PropertyManagements.AsNoTracking()
            .FirstOrDefaultAsync(item => item.ManagementAccountId == accountId, cancellationToken);
        if (account is null || !account.MustChangePassword ||
            !string.Equals(account.AccountStatus, "Active", StringComparison.OrdinalIgnoreCase) ||
            account.CredentialVersion != credentialVersion ||
            !string.Equals(account.Email, invitationEmail, StringComparison.OrdinalIgnoreCase))
        {
            await AuditAsync(accountId, account?.PropertyId, "InvitationActivation", false,
                "Invitation was already used, revoked, or account is inactive.");
            return Conflict(Error("This invitation has already been used, revoked, or the account is inactive.", StatusCodes.Status409Conflict));
        }

        var now = DateTime.UtcNow;
        var passwordHash = _passwordHasher.HashPassword(account, request.NewPassword);
        var updated = await _db.PropertyManagements
            .Where(item => item.ManagementAccountId == accountId &&
                           item.CredentialVersion == credentialVersion &&
                           item.MustChangePassword &&
                           item.AccountStatus == "Active")
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.PasswordHash, passwordHash)
                .SetProperty(item => item.MustChangePassword, false)
                .SetProperty(item => item.CredentialVersion, credentialVersion + 1)
                .SetProperty(item => item.UpdatedAt, now), cancellationToken);

        if (updated != 1)
        {
            await AuditAsync(accountId, account.PropertyId, "InvitationActivation", false, "Invitation was consumed concurrently or revoked.");
            return Conflict(Error("This invitation has already been used or revoked.", StatusCodes.Status409Conflict));
        }

        account = await _db.PropertyManagements.AsNoTracking().Include(item => item.Property)
            .FirstAsync(item => item.ManagementAccountId == accountId, cancellationToken);
        var newToken = _jwt.GenerateManagementToken(account);
        await AuditAsync(account.ManagementAccountId, account.PropertyId, "InvitationActivation", true,
            "First password set; invitation consumed.");
        return Ok(new PropertyManagementLoginResponse
        {
            Code = StatusCodes.Status200OK,
            Success = true,
            Message = "Password set. Your management account is ready.",
            JwtToken = newToken,
            MustChangePassword = false,
            Data = MapProfile(account)
        });
    }

    /// <summary>Logs out the manager by incrementing the account credential version.</summary>
    /// <response code="200">The current manager token is invalidated.</response>
    /// <response code="401">Authentication is required.</response>
    [Authorize(Policy = "PropertyManagementOnly")]
    [HttpPost("logout")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<ErrorResponse>> Logout(CancellationToken cancellationToken)
    {
        var id = _currentUser.UserId;
        var account = id.HasValue
            ? await _db.PropertyManagements.FirstOrDefaultAsync(item => item.ManagementAccountId == id.Value, cancellationToken)
            : null;
        if (account is null) return Unauthorized(Error("Management account was not found."));

        account.CredentialVersion++;
        account.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        await AuditAsync(account.ManagementAccountId, account.PropertyId, "Logout", true,
            "Current manager tokens invalidated.");
        return Ok(new ErrorResponse { Code = StatusCodes.Status200OK, Success = true, Message = "Logged out successfully." });
    }

    private static PropertyManagementProfileData MapProfile(PropertyManagement account) => new()
    {
        ManagementAccountId = account.ManagementAccountId,
        Email = account.Email,
        Username = account.Username,
        MustChangePassword = account.MustChangePassword,
        PropertyId = account.PropertyId,
        PropertyName = account.Property.PropertyName,
        PropertyAddress = account.Property.Address
    };

    private Task AuditAsync(int? managementAccountId, int? propertyId, string action, bool succeeded, string? detail = null)
    {
        HttpContext.Items["PropertyManagementAuditRecorded"] = true;
        return _accessLogs.LogManagementAsync(managementAccountId, propertyId, action, succeeded, detail);
    }

    private static ErrorResponse Error(string message, int code = StatusCodes.Status401Unauthorized)
        => new() { Code = code, Success = false, Message = message };
}
