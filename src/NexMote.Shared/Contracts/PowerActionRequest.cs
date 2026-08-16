namespace NexMote.Shared.Contracts;

/// <summary>
/// Hedef bilgisayara uzaktan güç eylemi (yeniden başlatma, kapatma veya kilitleme) tetikleme isteği.
/// </summary>
/// <param name="SessionId">Eylemin ait olduğu aktif oturum kimliği.</param>
/// <param name="Action">Uygulanacak güç eylemi türü ("reboot", "shutdown", "lock").</param>
public sealed record PowerActionRequest(Guid SessionId, string Action);
