using System.Security.Cryptography;
using System.Text;

namespace Coon.Meeting.Api.Services;

public class ApiKeyService : IApiKeyService
{
    private const int RandomBytes = 24;

    public string GenerateKey(bool live)
    {
        var bytes = RandomNumberGenerator.GetBytes(RandomBytes);
        var token = Convert.ToBase64String(bytes)
            .Replace("+", "")
            .Replace("/", "")
            .Replace("=", "");
        return $"{(live ? "sk_live_" : "sk_test_")}{token}";
    }

    public string Hash(string rawKey)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(rawKey));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public string Prefix(string rawKey) =>
        rawKey.Length <= 16 ? rawKey : rawKey[..16];
}
