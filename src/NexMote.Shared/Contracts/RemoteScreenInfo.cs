namespace NexMote.Shared.Contracts;

/// <summary>
/// Tekil bir monitörün ekran sıra numarası, adı, çözünürlük ve sol-üst koordinat bilgileri.
/// </summary>
/// <param name="Index">Monitör sıra numarası.</param>
/// <param name="Name">Ekran donanım adı (Örn: \\.\DISPLAY1).</param>
/// <param name="Width">Genişlik (Piksel).</param>
/// <param name="Height">Yükseklik (Piksel).</param>
/// <param name="Left">Sanal masaüstündeki sol (X) koordinatı.</param>
/// <param name="Top">Sanal masaüstündeki üst (Y) koordinatı.</param>
public sealed record DisplayItem(int Index, string Name, int Width, int Height, int Left = 0, int Top = 0);

/// <summary>
/// Hedef makinenin bağlı monitörlerini ve sanal ekran boyutlarını içeren ekran bilgisi kontratı.
/// </summary>
/// <param name="Left">Sanal ekran sol koordinatı.</param>
/// <param name="Top">Sanal ekran üst koordinatı.</param>
/// <param name="Width">Sanal ekran toplam genişliği.</param>
/// <param name="Height">Sanal ekran toplam yüksekliği.</param>
/// <param name="ActiveDisplayIndex">Varsayılan aktif monitör dizini.</param>
/// <param name="Displays">Mevcut monitörlerin listesi.</param>
public sealed record RemoteScreenInfo(
    int Left,
    int Top,
    int Width,
    int Height,
    int ActiveDisplayIndex = 0,
    DisplayItem[]? Displays = null);
