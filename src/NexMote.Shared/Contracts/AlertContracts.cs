namespace NexMote.Shared.Contracts;

/// <summary>
/// Şu an açık (çözülmemiş) olan bir cihaz uyarısını temsil eder — web konsolunda "Dikkat" filtresi
/// ve cihaz detay panelindeki uyarı rozeti için kullanılır.
/// </summary>
public sealed record ActiveDeviceAlert(
    Guid DeviceId,
    string AlertType,
    DateTimeOffset TriggeredAt);
