using Microsoft.AspNetCore.SignalR.Client;

namespace NexMote.Shared.Network;

/// <summary>
/// Ağ dalgalanmalarında veya sunucu yeniden başlatmalarında SignalR istemcisinin asla pes etmemesini sağlayan
/// sonsuz üstel yeniden bağlanma politikası (Madde 1).
/// </summary>
public sealed class InfiniteRetryPolicy : IRetryPolicy
{
    public TimeSpan? NextRetryDelay(RetryContext retryContext)
    {
        return retryContext.PreviousRetryCount switch
        {
            0 => TimeSpan.Zero,
            1 => TimeSpan.FromSeconds(2),
            2 => TimeSpan.FromSeconds(5),
            _ => TimeSpan.FromSeconds(10)
        };
    }
}
