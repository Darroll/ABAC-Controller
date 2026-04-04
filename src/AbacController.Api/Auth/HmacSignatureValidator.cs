using System.Security.Cryptography;
using System.Text;

namespace AbacController.Api.Auth;

/// <summary>
/// HMAC-SHA256 request signature validation for PEP-to-PDP communication.
/// Validates X-ABAC-Signature header against the request body.
/// </summary>
public static class HmacSignatureValidator
{
    public const string SignatureHeader = "X-ABAC-Signature";
    public const string TimestampHeader = "X-ABAC-Timestamp";
    public const string NonceHeader = "X-ABAC-Nonce";

    /// <summary>Maximum age of a signed request (5 minutes).</summary>
    private static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Compute HMAC-SHA256 signature for a request.
    /// Format: HMAC-SHA256(key, timestamp + nonce + body)
    /// </summary>
    public static string ComputeSignature(string secretKey, string timestamp, string nonce, string body)
    {
        var payload = $"{timestamp}\n{nonce}\n{body}";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secretKey));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(hash);
    }

    /// <summary>
    /// Validate a signed request.
    /// </summary>
    public static SignatureValidationResult Validate(
        string secretKey,
        string? signature,
        string? timestamp,
        string? nonce,
        string body)
    {
        if (string.IsNullOrWhiteSpace(signature))
            return SignatureValidationResult.Failed("Missing signature header");

        if (string.IsNullOrWhiteSpace(timestamp))
            return SignatureValidationResult.Failed("Missing timestamp header");

        if (string.IsNullOrWhiteSpace(nonce))
            return SignatureValidationResult.Failed("Missing nonce header");

        if (!DateTimeOffset.TryParse(timestamp, out var ts))
            return SignatureValidationResult.Failed("Invalid timestamp format");

        var age = DateTimeOffset.UtcNow - ts;
        if (age > MaxAge || age < -MaxAge)
            return SignatureValidationResult.Failed("Request timestamp expired or too far in the future");

        var expected = ComputeSignature(secretKey, timestamp, nonce, body);
        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(expected),
                Encoding.UTF8.GetBytes(signature)))
        {
            return SignatureValidationResult.Failed("Invalid signature");
        }

        return SignatureValidationResult.Valid();
    }
}

public sealed record SignatureValidationResult(bool IsValid, string? Error = null)
{
    public static SignatureValidationResult Valid() => new(true);
    public static SignatureValidationResult Failed(string error) => new(false, error);
}
