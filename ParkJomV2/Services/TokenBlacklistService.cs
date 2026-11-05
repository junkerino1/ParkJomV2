using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using ParkJomV2.Data;
using ParkJomV2.Models;

namespace ParkJomV2.Services;

public interface ITokenBlacklistService
{
    Task RevokeTokenAsync(string rawToken, int? userId, DateTime? expiresAt, CancellationToken ct = default);
    Task<bool> IsTokenRevokedAsync(string rawToken, CancellationToken ct = default);
}

public class TokenBlacklistService : ITokenBlacklistService
{
    private readonly ApplicationDbContext _db;
    private readonly IMemoryCache _cache;
    private readonly ILogger<TokenBlacklistService> _logger;

    public TokenBlacklistService(
        ApplicationDbContext db,
        IMemoryCache cache,
        ILogger<TokenBlacklistService> logger)
    {
        _db = db;
        _cache = cache;
        _logger = logger;
    }

    public static string ComputeTokenHash(string rawToken)
    {
        if (string.IsNullOrWhiteSpace(rawToken))
            return string.Empty;

        var bytes = Encoding.UTF8.GetBytes(rawToken.Trim());
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash);
    }

    public async Task RevokeTokenAsync(string rawToken, int? userId, DateTime? expiresAt, CancellationToken ct = default)
    {
        var tokenHash = ComputeTokenHash(rawToken);
        if (string.IsNullOrEmpty(tokenHash))
            return;

        // Cache in memory for fast validation until expiration (or default 24 hours)
        var cacheExpiry = expiresAt.HasValue && expiresAt.Value > DateTime.UtcNow
            ? expiresAt.Value - DateTime.UtcNow
            : TimeSpan.FromHours(24);

        _cache.Set($"revoked_token:{tokenHash}", true, cacheExpiry);

        try
        {
            var exists = await _db.RevokedTokens.AnyAsync(r => r.TokenHash == tokenHash, ct);
            if (!exists)
            {
                _db.RevokedTokens.Add(new RevokedToken
                {
                    TokenHash = tokenHash,
                    UserId = userId,
                    RevokedAt = DateTime.UtcNow,
                    ExpiresAt = expiresAt
                });
                await _db.SaveChangesAsync(ct);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist revoked token to database (hash: {Hash})", tokenHash);
        }
    }

    public async Task<bool> IsTokenRevokedAsync(string rawToken, CancellationToken ct = default)
    {
        var tokenHash = ComputeTokenHash(rawToken);
        if (string.IsNullOrEmpty(tokenHash))
            return false;

        var cacheKey = $"revoked_token:{tokenHash}";
        if (_cache.TryGetValue(cacheKey, out bool isRevoked) && isRevoked)
        {
            return true;
        }

        try
        {
            var dbRevoked = await _db.RevokedTokens.AnyAsync(r => r.TokenHash == tokenHash, ct);
            if (dbRevoked)
            {
                _cache.Set(cacheKey, true, TimeSpan.FromHours(1));
                return true;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to query RevokedTokens table; falling back to cache.");
        }

        return false;
    }
}
