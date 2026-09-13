namespace Coon.Meeting.Api.Services;

public interface IApiKeyService
{
    /// <summary>Generates a new raw API key - "sk_live_..." or "sk_test_...". Never persisted raw.</summary>
    string GenerateKey(bool live);

    /// <summary>SHA-256 hex of the raw key, the only form ever stored.</summary>
    string Hash(string rawKey);

    /// <summary>First few characters of the raw key, safe to display/log after issuance.</summary>
    string Prefix(string rawKey);
}
