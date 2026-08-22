namespace NexMote.Shared.Contracts;

/// <summary>
/// Çoklu ekran eş zamanlı akışında, belirli bir fiziksel ekrana ait canlı JPEG ekran karesi veya delta blok paketi.
/// </summary>
public sealed record MultiScreenFrame(
    int DisplayIndex,
    string? JpegBase64 = null,
    long Sequence = 0,
    long CapturedAtUnixMs = 0,
    bool IsKeyFrame = true,
    int ScreenWidth = 0,
    int ScreenHeight = 0,
    ScreenTile[]? Tiles = null);

/// <summary>
/// Ekranda sadece değişen 64x64 / 128x128 piksellik alanı temsil eden delta blok parçası.
/// </summary>
public sealed record ScreenTile(
    int X,
    int Y,
    int Width,
    int Height,
    string JpegBase64);

/// <summary>
/// Tekil bir monitörün ekran sıra numarası, adı, çözünürlük ve sol-üst koordinat bilgileri.
/// </summary>
public sealed record DisplayItem(int Index, string Name, int Width, int Height, int Left = 0, int Top = 0);

/// <summary>
/// Hedef makinenin bağlı monitörlerini ve sanal ekran boyutlarını içeren ekran bilgisi kontratı.
/// </summary>
public sealed record RemoteScreenInfo(
    int Left,
    int Top,
    int Width,
    int Height,
    int ActiveDisplayIndex = 0,
    DisplayItem[]? Displays = null);

/// <summary>
/// Teknisyen uygulamasından hedef bilgisayara gönderilen fare veya klavye girdi paketi.
/// </summary>
public sealed record RemoteInputEvent(
    Guid SessionId,
    string Kind,
    int X = 0,
    int Y = 0,
    string? Button = null,
    bool IsDown = false,
    int KeyCode = 0,
    int WheelDelta = 0,
    int DisplayIndex = 0,
    long Sequence = 0,
    long SentAtUnixMs = 0);

/// <summary>
/// Kare teslim onay ve ağ performans sinyalleri.
/// </summary>
public sealed record FrameAck(
    Guid SessionId,
    int DisplayIndex,
    long Sequence,
    long ReceivedAtUnixMs,
    long DisplayedAtUnixMs);

public sealed record InputAck(
    Guid SessionId,
    long Sequence,
    string Kind,
    long ReceivedAtUnixMs,
    bool Applied);

public sealed record NetworkProbe(
    Guid ProbeId,
    long SentAtUnixMs,
    int PayloadBytes = 0);

public sealed record NetworkProbeAck(
    Guid ProbeId,
    long SentAtUnixMs,
    long AgentReceivedAtUnixMs,
    long AgentSentAtUnixMs);

public sealed record NetworkSpeedResult(
    string Scope,
    double LatencyMs,
    double DownloadMbps,
    double UploadMbps,
    int DownloadBytes,
    int UploadBytes,
    DateTimeOffset MeasuredAt);

public static class QualityModes
{
    public const string Auto = "auto";
    public const string Speed = "speed";
    public const string Balanced = "balanced";
    public const string Quality = "quality";
}
