using NexMote.Shared.Network;
using NexMote.Shared.Security;
using Xunit;

namespace NexMote.Tests;

public sealed class NetworkValidationTests
{
    [Theory]
    [InlineData("http://192.168.0.219", "http://192.168.0.219")]
    [InlineData("http://192.168.0.219:8080", "http://192.168.0.219:8080")]
    [InlineData("192.168.0.219", "http://192.168.0.219")]
    [InlineData("http://10.0.5.10", "http://10.0.5.10")]
    [InlineData("http://172.16.1.50", "http://172.16.1.50")]
    [InlineData("http://localhost:5000", "http://localhost:5000")]
    [InlineData("127.0.0.1:5000", "http://127.0.0.1:5000")]
    [InlineData("https://nexmote.com", "https://nexmote.com")]
    [InlineData("http://remote.company.com", "https://remote.company.com")]
    [InlineData("", "https://nexmote.com")]
    [InlineData(null, "https://nexmote.com")]
    public void EnforceProductionUrl_HandlesPrivateAndPublicHosts(string? input, string expected)
    {
        var result = NexMoteHttp.EnforceProductionUrl(input);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("192.168.0.219", "http://192.168.0.219")]
    [InlineData("http://192.168.0.219/", "http://192.168.0.219")]
    [InlineData("nexmote.com", "https://nexmote.com")]
    [InlineData("http://nexmote.com", "https://nexmote.com")]
    [InlineData("localhost:5173", "http://localhost:5173")]
    public void NormalizeUrl_ProperlyNormalizesPrivateAndPublicHosts(string? input, string expected)
    {
        var result = NexMoteHttp.NormalizeUrl(input);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void DeepLinkValidator_AcceptsLocalNetworkServerUrl()
    {
        var sessionId = Guid.NewGuid();
        var token = "12345678901234567890";
        var uri = $"nexmote://session/?sessionId={sessionId}&token={token}&serverUrl={Uri.EscapeDataString("http://192.168.0.219")}";

        var valid = DeepLinkValidator.TryValidate(uri, null, out var validated, out var errorMessage, out var requiresConfirmation);

        Assert.True(valid, errorMessage);
        Assert.NotNull(validated);
        Assert.Equal("http://192.168.0.219", validated.ServerUrl);
        Assert.False(requiresConfirmation);
    }
}
