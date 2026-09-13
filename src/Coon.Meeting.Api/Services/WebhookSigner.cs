using System.Security.Cryptography;
using System.Text;

namespace Coon.Meeting.Api.Services;

/// <summary>
/// HMAC-SHA256-hex signing for outgoing webhooks, generalized from
/// Coon.Payment\Kashier\KashierSignature.cs's signing pattern for a payload this service
/// controls the shape of (Kashier's ordered-keys quirk doesn't apply here - see that class's
/// own remarks on why field order there is part of the signed message).
/// </summary>
public static class WebhookSigner
{
    /// <summary>
    /// Builds the "X-CoonMeeting-Signature" header value: "t={unixSeconds},v1={hex}", where the
    /// hex is HMAC-SHA256 of "{t}.{rawBody}". Binding the timestamp into the signed message (not
    /// just alongside it) is what stops a captured header+body pair from being replayed with a
    /// different timestamp.
    /// </summary>
    public static string BuildHeader(long unixTimestamp, string rawBody, string secret)
    {
        var signature = HmacSha256Hex($"{unixTimestamp}.{rawBody}", secret);
        return $"t={unixTimestamp},v1={signature}";
    }

    /// <summary>Lowercase hex HMAC-SHA256, same primitive KashierSignature uses.</summary>
    public static string HmacSha256Hex(string message, string key)
    {
        using var mac = new HMACSHA256(Encoding.UTF8.GetBytes(key));
        var hash = mac.ComputeHash(Encoding.UTF8.GetBytes(message));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>
    /// Constant-time comparison, for an integrator (or our own tests) verifying a received
    /// signature - ordinary string equality leaks a timing side-channel byte by byte.
    /// </summary>
    public static bool Matches(string? expected, string? received)
    {
        if (string.IsNullOrEmpty(expected) || string.IsNullOrEmpty(received)) return false;
        if (expected.Length != received.Length) return false;

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expected.ToLowerInvariant()),
            Encoding.UTF8.GetBytes(received.ToLowerInvariant()));
    }
}
