using System.Runtime.InteropServices;

namespace NexMote.Agent.Windows;

/// <summary>
/// Arka planda Win32 GetSystemTimes API'sini kullanarak 15 saniyede bir gerçek CPU kullanımını örnekleyen
/// ve 10 dakikalık kayan pencere (rolling window) ortalamasını hesaplayan telemetri toplayıcı.
/// Anlık dalgalanmaları yumuşatarak stabil ve güvenilir CPU kullanım yüzdesi sağlar.
/// </summary>
public sealed class CpuUsageSampler : IDisposable
{
    private static readonly TimeSpan SampleInterval = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan WindowSize = TimeSpan.FromMinutes(10);

    private readonly object _lock = new();
    private readonly List<(DateTimeOffset Timestamp, double Percent)> _samples = new();
    private readonly Timer _timer;

    private long _lastIdle;
    private long _lastKernel;
    private long _lastUser;
    private bool _hasBaseline;

    public CpuUsageSampler()
    {
        _timer = new Timer(_ => Sample(), null, TimeSpan.Zero, SampleInterval);
    }

    /// <summary>
    /// Son 10 dakika içindeki CPU kullanım örneklerinin yuvarlanmış ortalama yüzdesini (0-100) döner.
    /// </summary>
    public int GetAveragePercent()
    {
        lock (_lock)
        {
            if (_samples.Count == 0)
            {
                return 0;
            }

            return (int)Math.Round(_samples.Average(s => s.Percent));
        }
    }

    /// <summary>
    /// Win32 GetSystemTimes çağrısı yaparak çekirdek, kullanıcı ve boşta kalma süreleri arasındaki farktan CPU yükünü hesaplar.
    /// </summary>
    private void Sample()
    {
        if (!GetSystemTimes(out var idle, out var kernel, out var user))
        {
            return;
        }

        var idleTicks = ToInt64(idle);
        var kernelTicks = ToInt64(kernel);
        var userTicks = ToInt64(user);

        lock (_lock)
        {
            if (!_hasBaseline)
            {
                _lastIdle = idleTicks;
                _lastKernel = kernelTicks;
                _lastUser = userTicks;
                _hasBaseline = true;
                return;
            }

            var idleDelta = idleTicks - _lastIdle;
            var kernelDelta = kernelTicks - _lastKernel;
            var userDelta = userTicks - _lastUser;
            _lastIdle = idleTicks;
            _lastKernel = kernelTicks;
            _lastUser = userTicks;

            var totalDelta = kernelDelta + userDelta;
            if (totalDelta <= 0)
            {
                return;
            }

            var busyDelta = totalDelta - idleDelta;
            var percent = Math.Clamp(busyDelta * 100.0 / totalDelta, 0, 100);

            var now = DateTimeOffset.UtcNow;
            _samples.Add((now, percent));
            _samples.RemoveAll(s => now - s.Timestamp > WindowSize);
        }
    }

    public void Dispose() => _timer.Dispose();

    private static long ToInt64(FILETIME ft) => ((long)ft.dwHighDateTime << 32) | ft.dwLowDateTime;

    [StructLayout(LayoutKind.Sequential)]
    private struct FILETIME
    {
        public uint dwLowDateTime;
        public uint dwHighDateTime;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemTimes(out FILETIME lpIdleTime, out FILETIME lpKernelTime, out FILETIME lpUserTime);
}
