using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace ParkJomV2.Services;

/// <summary>Short-lived, single-use unlock challenges held in process memory.</summary>
public sealed class UnlockChallengeService
{
    private static readonly TimeSpan ChallengeLifetime = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan UsedMarkerLifetime = TimeSpan.FromMinutes(2);
    private readonly object _stateLock = new();
    private readonly ConcurrentDictionary<string, UnlockChallenge> _challenges = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, DateTime> _usedChallenges = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, DateTime> _expiredChallenges = new(StringComparer.Ordinal);

    public UnlockChallenge Create(int bookingId, int renterId, int deviceId, string deviceUuid)
    {
        PruneExpired();
        while (true)
        {
            var challengeId = $"CHL-{Convert.ToHexString(RandomNumberGenerator.GetBytes(8)).ToLowerInvariant()}";
            var now = DateTimeOffset.UtcNow;
            var challenge = new UnlockChallenge(
                challengeId,
                Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
                bookingId,
                renterId,
                deviceId,
                deviceUuid,
                now,
                now.Add(ChallengeLifetime));

            if (_challenges.TryAdd(challengeId, challenge))
            {
                return challenge;
            }
        }
    }

    public ChallengeLookup Find(string challengeId, DateTimeOffset now)
    {
        lock (_stateLock)
        {
            if (_usedChallenges.TryGetValue(challengeId, out var usedUntil) && usedUntil > now.UtcDateTime)
                return new ChallengeLookup(ChallengeState.Used, null);
            if (_expiredChallenges.TryGetValue(challengeId, out var expiredUntil) && expiredUntil > now.UtcDateTime)
                return new ChallengeLookup(ChallengeState.Expired, null);

            if (!_challenges.TryGetValue(challengeId, out var challenge))
                return new ChallengeLookup(ChallengeState.Invalid, null);

            if (challenge.ExpiresAt <= now)
            {
                _challenges.TryRemove(challengeId, out _);
                _expiredChallenges[challengeId] = now.Add(UsedMarkerLifetime).UtcDateTime;
                return new ChallengeLookup(ChallengeState.Expired, null);
            }

            return new ChallengeLookup(ChallengeState.Available, challenge);
        }
    }

    /// <summary>Called only after all checks pass; competing requests can consume it only once.</summary>
    public bool TryConsume(string challengeId, DateTimeOffset now)
    {
        lock (_stateLock)
        {
            if (!_challenges.TryRemove(challengeId, out var challenge))
                return false;

            if (challenge.ExpiresAt <= now)
            {
                _expiredChallenges[challengeId] = now.Add(UsedMarkerLifetime).UtcDateTime;
                return false;
            }

            _usedChallenges[challengeId] = now.Add(UsedMarkerLifetime).UtcDateTime;
            return true;
        }
    }

    /// <summary>Removes a challenge that could not be activated on its assigned device.</summary>
    public void Discard(string challengeId)
    {
        lock (_stateLock)
        {
            _challenges.TryRemove(challengeId, out _);
        }
    }

    private void PruneExpired()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var pair in _challenges)
        {
            if (pair.Value.ExpiresAt <= now)
            {
                lock (_stateLock)
                {
                    if (_challenges.TryRemove(pair.Key, out _))
                        _expiredChallenges[pair.Key] = now.Add(UsedMarkerLifetime).UtcDateTime;
                }
            }
        }

        foreach (var pair in _usedChallenges)
        {
            if (pair.Value <= now.UtcDateTime)
            {
                _usedChallenges.TryRemove(pair.Key, out _);
            }
        }

        foreach (var pair in _expiredChallenges)
        {
            if (pair.Value <= now.UtcDateTime)
            {
                _expiredChallenges.TryRemove(pair.Key, out _);
            }
        }
    }
}

public sealed record UnlockChallenge(
    string ChallengeId,
    string Nonce,
    int BookingId,
    int RenterId,
    int DeviceId,
    string DeviceUuid,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt);

public sealed record ChallengeLookup(ChallengeState State, UnlockChallenge? Challenge);

public enum ChallengeState
{
    Available,
    Invalid,
    Expired,
    Used
}
