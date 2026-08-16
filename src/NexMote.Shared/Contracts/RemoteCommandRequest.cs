namespace NexMote.Shared.Contracts;

/// <summary>
/// Hedef bilgisayarda uzaktan CMD veya PowerShell komutu çalıştırma isteği.
/// </summary>
/// <param name="SessionId">Komutun ait olduğu aktif teknisyen oturum kimliği.</param>
/// <param name="RequestId">Sonucu eşleştirmek için kullanılan benzersiz istek kimliği.</param>
/// <param name="Shell">Kullanılacak kabuk türü ("cmd" veya "powershell").</param>
/// <param name="Command">Yürütülecek komut satırı.</param>
/// <param name="RunAsAdmin">Komutun Windows UAC yönetici ayrıcalıklarıyla (runas) çalıştırılıp çalıştırılmayacağı.</param>
public sealed record RemoteCommandRequest(
    Guid SessionId,
    string RequestId,
    string Shell,
    string Command,
    bool RunAsAdmin = false);
