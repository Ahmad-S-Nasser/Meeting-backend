using System.Security.Cryptography;
using System.Text;

namespace Coon.Meeting.Api.Helpers;

public static class SecretComparer
{
    /// <summary>
    /// Constant-time, case-sensitive string comparison - ordinary string equality (or even
    /// case-insensitive comparison) leaks a timing side-channel byte by byte for anything
    /// compared against a secret.
    /// </summary>
    public static bool Equals(string? expected, string? received)
    {
        if (string.IsNullOrEmpty(expected) || string.IsNullOrEmpty(received)) return false;
        if (expected.Length != received.Length) return false;

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expected),
            Encoding.UTF8.GetBytes(received));
    }
}
