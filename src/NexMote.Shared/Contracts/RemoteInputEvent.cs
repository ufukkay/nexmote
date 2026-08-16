namespace NexMote.Shared.Contracts;

/// <summary>
/// Teknisyen uygulamasından hedef bilgisayara gönderilen fare veya klavye girdi paketi.
/// </summary>
/// <param name="SessionId">Oturum kimliği.</param>
/// <param name="Kind">Olay türü ("mousemove", "mousedown", "mouseup", "keydown", "keyup", "wheel").</param>
/// <param name="X">Seçili ekranın X koordinatı.</param>
/// <param name="Y">Seçili ekranın Y koordinatı.</param>
/// <param name="Button">Fare tuşu ("left", "right", "middle").</param>
/// <param name="IsDown">Tuşun basılı olup olmadığı.</param>
/// <param name="KeyCode">Klavye tuş kodu (VK code).</param>
/// <param name="WheelDelta">Fare tekerleği dönüş miktarı (+120 / -120).</param>
/// <param name="DisplayIndex">Hedef monitör dizin numarası.</param>
public sealed record RemoteInputEvent(
    Guid SessionId,
    string Kind,
    int X = 0,
    int Y = 0,
    string? Button = null,
    bool IsDown = false,
    int KeyCode = 0,
    int WheelDelta = 0,
    int DisplayIndex = 0);
