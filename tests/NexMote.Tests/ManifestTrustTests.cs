using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using NexMote.Api.Services;
using Xunit;

namespace NexMote.Tests;

public sealed class ManifestTrustTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "nexmote-tests-" + Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CopiedKeyIdDoesNotAuthorizeAnotherCertificate(bool legacyConfiguration)
    {
        using var trusted = CreateCertificate();
        using var attacker = CreateCertificate();
        var keyId = legacyConfiguration ? trusted.Thumbprint : "release";
        var settings = new Dictionary<string, string?>
        {
            [legacyConfiguration ? "Updates:ManifestSignature:TrustedKeyIds:0" : "Updates:ManifestSignature:TrustedKeys:release"] = trusted.Thumbprint
        };
        Assert.False(Verify(attacker, keyId, settings).IsValid);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PinnedCertificateIsAcceptedUnlessManifestChanged(bool tampered)
    {
        using var certificate = CreateCertificate();
        var settings = new Dictionary<string, string?>
        {
            ["Updates:ManifestSignature:TrustedCertificateThumbprints:0"] = certificate.Thumbprint
        };
        Assert.Equal(!tampered, Verify(certificate, "release", settings, tampered).IsValid);
    }

    private ManifestSignatureVerificationResult Verify(X509Certificate2 certificate, string keyId,
        Dictionary<string, string?> settings, bool tampered = false)
    {
        Directory.CreateDirectory(_directory);
        var manifest = Path.Combine(_directory, "versions.json");
        var signature = manifest + ".sig";
        File.WriteAllText(manifest, "{\"version\":\"1.0.0\"}");
        using var rsa = certificate.GetRSAPrivateKey()!;
        var envelope = new ManifestSignatureEnvelope(keyId, "RSASSA-PKCS1-v1_5-SHA256",
            certificate.Thumbprint, Convert.ToBase64String(certificate.Export(X509ContentType.Cert)),
            Convert.ToBase64String(rsa.SignData(File.ReadAllBytes(manifest), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)));
        File.WriteAllText(signature, JsonSerializer.Serialize(envelope));
        if (tampered) File.AppendAllText(manifest, " ");
        return new ManifestSignatureVerifier(new ConfigurationBuilder().AddInMemoryCollection(settings).Build())
            .Verify(manifest, signature);
    }

    [Fact]
    public void NamedKeyAcceptsOnlyItsPinnedCertificate()
    {
        using var certificate = CreateCertificate();
        Assert.True(Verify(certificate, "release", new()
        {
            ["Updates:ManifestSignature:TrustedKeys:release"] = certificate.Thumbprint
        }).IsValid);
    }

    [Fact]
    public void RequiredMissingSignatureIsRejected()
    {
        Directory.CreateDirectory(_directory);
        var manifest = Path.Combine(_directory, "versions.json");
        File.WriteAllText(manifest, "{}");
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Updates:ManifestSignature:Required"] = "true"
        }).Build();
        Assert.False(new ManifestSignatureVerifier(config).Verify(manifest, manifest + ".sig").IsValid);
    }

    private static X509Certificate2 CreateCertificate()
    {
        using var rsa = RSA.Create(2048);
        return new CertificateRequest("CN=NexMote Test", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)
            .CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }
}
