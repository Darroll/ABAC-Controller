using AbacController.Api.Auth;

namespace AbacController.Tests.Unit;

public sealed class HmacSignatureValidatorTests
{
    private const string SecretKey = "super-secret-key-for-testing-purposes-only";

    [Fact]
    public void ComputeSignature_ReturnsConsistentResult()
    {
        var timestamp = "2025-01-15T10:00:00Z";
        var nonce = "abc123";
        var body = "{\"test\":true}";

        var sig1 = HmacSignatureValidator.ComputeSignature(SecretKey, timestamp, nonce, body);
        var sig2 = HmacSignatureValidator.ComputeSignature(SecretKey, timestamp, nonce, body);

        Assert.Equal(sig1, sig2);
        Assert.NotEmpty(sig1);
    }

    [Fact]
    public void ComputeSignature_DifferentKeysDifferentSignatures()
    {
        var timestamp = "2025-01-15T10:00:00Z";
        var nonce = "abc123";
        var body = "{\"test\":true}";

        var sig1 = HmacSignatureValidator.ComputeSignature("key-one", timestamp, nonce, body);
        var sig2 = HmacSignatureValidator.ComputeSignature("key-two", timestamp, nonce, body);

        Assert.NotEqual(sig1, sig2);
    }

    [Fact]
    public void Validate_AcceptsValidSignature()
    {
        var timestamp = DateTimeOffset.UtcNow.ToString("O");
        var nonce = Guid.NewGuid().ToString("N");
        var body = "{\"subject\":{\"type\":\"user\",\"id\":\"alice\"}}";

        var signature = HmacSignatureValidator.ComputeSignature(SecretKey, timestamp, nonce, body);
        var result = HmacSignatureValidator.Validate(SecretKey, signature, timestamp, nonce, body);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_RejectsTamperedBody()
    {
        var timestamp = DateTimeOffset.UtcNow.ToString("O");
        var nonce = Guid.NewGuid().ToString("N");
        var body = "{\"subject\":{\"type\":\"user\",\"id\":\"alice\"}}";

        var signature = HmacSignatureValidator.ComputeSignature(SecretKey, timestamp, nonce, body);
        var result = HmacSignatureValidator.Validate(SecretKey, signature, timestamp, nonce, body + "tampered");

        Assert.False(result.IsValid);
        Assert.Equal("Invalid signature", result.Error);
    }

    [Fact]
    public void Validate_RejectsExpiredTimestamp()
    {
        var timestamp = DateTimeOffset.UtcNow.AddMinutes(-10).ToString("O");
        var nonce = Guid.NewGuid().ToString("N");
        var body = "{}";

        var signature = HmacSignatureValidator.ComputeSignature(SecretKey, timestamp, nonce, body);
        var result = HmacSignatureValidator.Validate(SecretKey, signature, timestamp, nonce, body);

        Assert.False(result.IsValid);
        Assert.Contains("expired", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_RejectsMissingSignature()
    {
        var result = HmacSignatureValidator.Validate(SecretKey, null, "ts", "nonce", "body");
        Assert.False(result.IsValid);
    }

    [Fact]
    public void Validate_RejectsMissingTimestamp()
    {
        var result = HmacSignatureValidator.Validate(SecretKey, "sig", null, "nonce", "body");
        Assert.False(result.IsValid);
    }

    [Fact]
    public void Validate_RejectsMissingNonce()
    {
        var result = HmacSignatureValidator.Validate(SecretKey, "sig", "ts", null, "body");
        Assert.False(result.IsValid);
    }
}
