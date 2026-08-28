using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using ParkJomV2.Data;
using ParkJomV2.Models;

namespace ParkJomV2.Services;

/// <summary>
/// Resolves the authenticated user from the current HTTP context and centralizes the
/// user/account DB lookup. JWT validation and account-suspension checks stay in
/// Program.cs (OnTokenValidated); this service only exposes reusable lookups.
/// </summary>
public class CurrentUserService
{
    private readonly ApplicationDbContext _context;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CurrentUserService(ApplicationDbContext context, IHttpContextAccessor httpContextAccessor)
    {
        _context = context;
        _httpContextAccessor = httpContextAccessor;
    }

    /// <summary>The authenticated user's numeric id from the JWT claim, or null when unauthenticated/invalid.</summary>
    public int? UserId
    {
        get
        {
            var userIdText = _httpContextAccessor.HttpContext?.User
                ?.FindFirstValue(ClaimTypes.NameIdentifier);
            return int.TryParse(userIdText, out var userId) ? userId : null;
        }
    }

    /// <summary>
    /// Loads the current user from the database, or null when the token is missing/invalid
    /// or the account no longer exists.
    /// </summary>
    public Task<User?> GetCurrentUserAsync(CancellationToken cancellationToken = default)
    {
        var userId = UserId;
        return userId.HasValue
            ? _context.Users.FirstOrDefaultAsync(u => u.UserId == userId.Value, cancellationToken)
            : Task.FromResult<User?>(null);
    }

    /// <summary>Loads a user by id, or null when not found.</summary>
    public Task<User?> GetUserAsync(int userId, CancellationToken cancellationToken = default)
        => _context.Users.FirstOrDefaultAsync(u => u.UserId == userId, cancellationToken);
}