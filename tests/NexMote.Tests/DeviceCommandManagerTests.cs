using System.Security.Cryptography;
using System.Text;
using NexMote.Api.Services;
using NexMote.Shared.Contracts;
using Xunit;

namespace NexMote.Tests;

public class DeviceCommandManagerTests
{
    [Fact]
    public async Task RegisterCommand_CompleteCommand_ResolvesTaskWithResult()
    {
        var manager = new DeviceCommandManager();
        var requestId = Guid.NewGuid();

        var tcs = manager.RegisterCommand(requestId);

        var expectedResult = new DeviceCommandExecutionResult(
            requestId,
            ExitCode: 0,
            StdOut: "Windows IP Configuration\r\nIPv4 Address: 192.168.1.1",
            StdErr: "",
            DurationMs: 120,
            TimedOut: false,
            ElevationDenied: false);

        var completed = manager.CompleteCommand(expectedResult);
        Assert.True(completed);

        var actualResult = await tcs.Task;
        Assert.Equal(0, actualResult.ExitCode);
        Assert.Contains("192.168.1.1", actualResult.StdOut);
        Assert.False(actualResult.TimedOut);
    }

    [Fact]
    public void RegisterCommand_CancelCommand_CancelsTask()
    {
        var manager = new DeviceCommandManager();
        var requestId = Guid.NewGuid();

        var tcs = manager.RegisterCommand(requestId);
        manager.CancelCommand(requestId);

        Assert.True(tcs.Task.IsCanceled);
    }
}

public class SecurityAndAuthTests
{
    [Fact]
    public void SessionTokens_Hash_ProducesDeterministicSha256()
    {
        var rawToken = "test_token_secret_123456789";
        var hash1 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken)));
        var hash2 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken)));

        Assert.Equal(hash1, hash2);
        Assert.Equal(64, hash1.Length);
    }

    [Fact]
    public void TimingSafeEquals_MatchesIdenticalStrings()
    {
        var token1 = "abcdef1234567890abcdef1234567890";
        var token2 = "abcdef1234567890abcdef1234567890";
        var token3 = "different_token_1234567890";

        Assert.True(CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(token1),
            Encoding.UTF8.GetBytes(token2)));

        Assert.False(CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(token1),
            Encoding.UTF8.GetBytes(token3)));
    }
}
