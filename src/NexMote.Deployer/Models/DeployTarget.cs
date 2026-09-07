using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace NexMote.Deployer.Models;

public enum DeployStatus
{
    Pending,
    Connecting,
    Copying,
    Installing,
    Verifying,
    Success,
    Failed,
    Skipped
}

public class DeployTarget : INotifyPropertyChanged
{
    private string _ip = string.Empty;
    private string _hostname = "-";
    private long _pingMs = -1;
    private DeployStatus _status = DeployStatus.Pending;
    private string _statusText = "Kuyrukta";
    private string _details = string.Empty;
    private string _duration = "-";

    public string Ip
    {
        get => _ip;
        set => SetField(ref _ip, value);
    }

    public string Hostname
    {
        get => _hostname;
        set => SetField(ref _hostname, value);
    }

    public long PingMs
    {
        get => _pingMs;
        set
        {
            SetField(ref _pingMs, value);
            OnPropertyChanged(nameof(PingDisplay));
        }
    }

    public string PingDisplay => PingMs >= 0 ? $"{PingMs} ms" : "-";

    public DeployStatus Status
    {
        get => _status;
        set
        {
            SetField(ref _status, value);
            OnPropertyChanged(nameof(StatusBadgeColor));
        }
    }

    public string StatusText
    {
        get => _statusText;
        set => SetField(ref _statusText, value);
    }

    public string Details
    {
        get => _details;
        set => SetField(ref _details, value);
    }

    public string Duration
    {
        get => _duration;
        set => SetField(ref _duration, value);
    }

    public string StatusBadgeColor => Status switch
    {
        DeployStatus.Pending => "#94A3B8",    // Slate Gray
        DeployStatus.Connecting => "#3B82F6", // Blue
        DeployStatus.Copying => "#6366F1",    // Indigo
        DeployStatus.Installing => "#F59E0B", // Amber
        DeployStatus.Verifying => "#06B6D4",  // Cyan
        DeployStatus.Success => "#10B981",    // Emerald Green
        DeployStatus.Failed => "#EF4444",     // Rose Red
        DeployStatus.Skipped => "#64748B",    // Muted Gray
        _ => "#94A3B8"
    };

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}
