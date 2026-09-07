using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace NexMote.Api.Services;

public sealed class ManifestSignatureVerifier(IConfiguration configuration)
{
    private readonly bool _required = configuration.GetValue("Updates:ManifestSignature:Required", false);
    private readonly string[] _trustedKeyIds = configuration.GetSection("Updates:ManifestSignature:TrustedKeyIds").Get<string[]>() ?? [];
    private readonly Dictionary<string, string> _trustedKeys =
        configuration.GetSection("Updates:ManifestSignature:TrustedKeys").Get<Dictionary<string, string>>() ?? [];
    private readonly string[] _trustedCertificateThumbprints =
        configuration.GetSection("Updates:ManifestSignature:TrustedCertificateThumbprints").Get<string[]>() ?? [];

    public bool IsRequired => _required;

    public ManifestSignatureVerificationResult Verify(string manifestPath, string signaturePath)
    {
        if (!File.Exists(manifestPath))
        {
            return ManifestSignatureVerificationResult.Fail("Update manifest file was not found.");
        }

        if (!File.Exists(signaturePath))
        {
            return _required
                ? ManifestSignatureVerificationResult.Fail("Update manifest signature is required but versions.json.sig was not found.")
                : ManifestSignatureVerificationResult.NotConfigured();
        }

        ManifestSignatureEnvelope? envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<ManifestSignatureEnvelope>(
                File.ReadAllText(signaturePath),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException)
        {
            return ManifestSignatureVerificationResult.Fail("Update manifest signature JSON is invalid.");
        }

        if (envelope is null ||
            string.IsNullOrWhiteSpace(envelope.KeyId) ||
            string.IsNullOrWhiteSpace(envelope.Algorithm) ||
            string.IsNullOrWhiteSpace(envelope.Certificate) ||
            string.IsNullOrWhiteSpace(envelope.CertificateThumbprint) ||
            string.IsNullOrWhiteSpace(envelope.Signature))
        {
            return ManifestSignatureVerificationResult.Fail("Update manifest signature is missing required fields.");
        }

        if (!string.Equals(envelope.Algorithm, "RSASSA-PKCS1-v1_5-SHA256", StringComparison.Ordinal))
        {
            return ManifestSignatureVerificationResult.Fail($"Unsupported update manifest signature algorithm: {envelope.Algorithm}");
        }

        X509Certificate2 certificate;
        byte[] signatureBytes;
        try
        {
            signatureBytes = Convert.FromBase64String(envelope.Signature);
            certificate = new X509Certificate2(Convert.FromBase64String(envelope.Certificate));
        }
        catch (FormatException)
        {
            return ManifestSignatureVerificationResult.Fail("Update manifest signature contains invalid base64 data.");
        }
        catch (CryptographicException)
        {
            return ManifestSignatureVerificationResult.Fail("Update manifest signer certificate is invalid.");
        }

        using var ownedCertificate = certificate;
        var actualThumbprint = Normalize(certificate.Thumbprint);
        if (!string.Equals(actualThumbprint, Normalize(envelope.CertificateThumbprint), StringComparison.OrdinalIgnoreCase))
        {
            return ManifestSignatureVerificationResult.Fail("Update manifest signer certificate does not match the declared thumbprint.");
        }

        // Legacy key ids were certificate thumbprints; arbitrary labels never establish trust.
        var trusted = _trustedKeys.TryGetValue(envelope.KeyId, out var pinnedThumbprint)
            ? string.Equals(actualThumbprint, Normalize(pinnedThumbprint), StringComparison.OrdinalIgnoreCase)
            : IsTrusted(actualThumbprint, _trustedCertificateThumbprints) ||
              (IsTrusted(envelope.KeyId, _trustedKeyIds) &&
               string.Equals(actualThumbprint, Normalize(envelope.KeyId), StringComparison.OrdinalIgnoreCase));
        if (!trusted)
        {
            return ManifestSignatureVerificationResult.Fail("Update manifest signer certificate is not pinned to a trusted key.");
        }

        using var rsa = certificate.GetRSAPublicKey();
        if (rsa is null)
        {
            return ManifestSignatureVerificationResult.Fail("Update manifest signer certificate does not contain an RSA public key.");
        }

        var manifestBytes = File.ReadAllBytes(manifestPath);
        var valid = rsa.VerifyData(
            manifestBytes,
            signatureBytes,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        return valid
            ? ManifestSignatureVerificationResult.Valid(envelope.KeyId, certificate.Thumbprint)
            : ManifestSignatureVerificationResult.Fail("Update manifest signature does not match versions.json.");
    }

    private static bool IsTrusted(string value, IReadOnlyCollection<string> trustedValues)
    {
        if (trustedValues.Count == 0)
        {
            return false;
        }

        var normalized = Normalize(value);
        return trustedValues.Any(item => string.Equals(Normalize(item), normalized, StringComparison.OrdinalIgnoreCase));
    }

    private static string Normalize(string value) => value.Replace(" ", "", StringComparison.Ordinal).Trim();
}

public sealed record ManifestSignatureEnvelope(
    string KeyId,
    string Algorithm,
    string CertificateThumbprint,
    string Certificate,
    string Signature,
    string? SignedAtUtc = null);

public sealed record ManifestSignatureVerificationResult(
    bool IsValid,
    bool IsConfigured,
    string? KeyId,
    string? CertificateThumbprint,
    string? Error)
{
    public static ManifestSignatureVerificationResult Valid(string keyId, string certificateThumbprint) =>
        new(true, true, keyId, certificateThumbprint, null);

    public static ManifestSignatureVerificationResult Fail(string error) =>
        new(false, true, null, null, error);

    public static ManifestSignatureVerificationResult NotConfigured() =>
        new(false, false, null, null, null);
}
