using Microsoft.AspNetCore.SignalR.Client;
using System.Security.Cryptography;

namespace NexMote.Shared.Network;

/// <summary>
/// Ağ dalgalanmalarında veya sunucu yeniden başlatmalarında SignalR istemcisinin asla pes etmemesini sağlayan
/// sonsuz üstel yeniden bağlanma politikası (Madde 1).
/// </summary>
public sealed class InfiniteRetryPolicy : IRetryPolicy
{
    public TimeSpan? NextRetryDelay(RetryContext retryContext)
    {
        // Exponential-ish backoff with light random jitter to avoid thundering herd
        if (retryContext.PreviousRetryCount <= 0) return TimeSpan.Zero;

        // Base exponential backoff (seconds), capped to 30s
        var exp = Math.Min(30, (int)Math.Pow(2, Math.Min(retryContext.PreviousRetryCount, 5)));

        // Jitter factor in range [0.6, 1.4]
        var rand = RandomNumberGenerator.GetInt32(0, 1000) / 1000.0;
        var jitterFactor = 0.6 + (rand * 0.8);

        var seconds = Math.Max(1, (int)(exp * jitterFactor));
        return TimeSpan.FromSeconds(seconds);
    }
}
