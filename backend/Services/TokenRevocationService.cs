using System.Collections.Concurrent;

namespace Community360.Api.Services;

/// <summary>
/// Gestiona la revocación en memoria de tokens JWT cerrados mediante logout (/api/auth/logout).
/// </summary>
public sealed class TokenRevocationService
{
    private readonly ConcurrentDictionary<string, DateTime> _revokedTokens = new();

    public void RevokeToken(string jti, DateTime expiresAtUtc)
    {
        if (string.IsNullOrWhiteSpace(jti))
            return;

        _revokedTokens[jti] = expiresAtUtc;
        CleanupExpired();
    }

    public bool IsRevoked(string? jti)
    {
        if (string.IsNullOrWhiteSpace(jti))
            return false;

        return _revokedTokens.ContainsKey(jti);
    }

    private void CleanupExpired()
    {
        var now = DateTime.UtcNow;
        foreach (var kvp in _revokedTokens)
        {
            if (kvp.Value < now)
            {
                _revokedTokens.TryRemove(kvp.Key, out _);
            }
        }
    }
}
