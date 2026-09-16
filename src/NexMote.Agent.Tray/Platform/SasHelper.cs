using System.Runtime.InteropServices;

namespace NexMote.Agent.Tray;

/// <summary>
/// Hedef makinede yazılımsal olarak Güvenli Dikkat Dizisi (Secure Attention Sequence - Ctrl+Alt+Del) üreten yardımcı sınıf.
/// sas.dll / SendSAS API'sini veya sentetik klavye olaylarını kullanır.
/// </summary>
internal static class SasHelper
{
    [DllImport("sas.dll", SetLastError = true)]
    private static extern void SendSAS(bool asUser);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern void mouse_event(uint dwFlags, int dx, int dy, uint dwData, UIntPtr dwExtraInfo);

    private const int KEYEVENTF_KEYUP = 0x0002;
    private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    private const uint MOUSEEVENTF_LEFTUP = 0x0004;

    private const byte VK_SPACE = 0x20;
    private const byte VK_RETURN = 0x0D;
    private const byte VK_ESCAPE = 0x1B;
    private const byte VK_CONTROL = 0x11;
    private const byte VK_MENU = 0x12; // Alt
    private const byte VK_DELETE = 0x2E;

    public static void SendSas()
    {
        // 1. Zorunlu olarak aktif masaüstüne (Default veya Winlogon) bağlan
        DesktopHelper.AttachToActiveDesktop(force: true);

        // 2. sas.dll politika denetimi ve Güvenli Dikkat Dizisi (Ctrl+Alt+Del) gönderimi
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System", true);
            key?.SetValue("SoftwareSASGeneration", 3, Microsoft.Win32.RegistryValueKind.DWord);
            key?.SetValue("PromptOnSecureDesktop", 0, Microsoft.Win32.RegistryValueKind.DWord);
        }
        catch { }

        try
        {
            SendSAS(false);
        }
        catch { }

        try
        {
            SendSAS(true);
        }
        catch { }

        // 3. Windows 10/11 Kilit Ekranı (Duvar kağıdı/Saat ekranı) perde kaldırma:
        // Space, Enter, Escape ve Mouse Click göndererek kilit ekranını yukarı kaydırıp parola kutusunu aç
        try
        {
            // Sol fare tıklaması (Kilit ekranı perdesini tıklayarak yukarı iter)
            mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, UIntPtr.Zero);
            Thread.Sleep(20);
            mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, UIntPtr.Zero);
            Thread.Sleep(30);

            // Space tuşu
            keybd_event(VK_SPACE, 0, 0, UIntPtr.Zero);
            Thread.Sleep(20);
            keybd_event(VK_SPACE, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            Thread.Sleep(30);

            // Enter tuşu
            keybd_event(VK_RETURN, 0, 0, UIntPtr.Zero);
            Thread.Sleep(20);
            keybd_event(VK_RETURN, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            Thread.Sleep(30);

            // Donanımsal / yazılımsal Ctrl+Alt+Del sentetik kombinasyonu
            keybd_event(VK_CONTROL, 0, 0, UIntPtr.Zero);
            keybd_event(VK_MENU, 0, 0, UIntPtr.Zero);
            keybd_event(VK_DELETE, 0, 0, UIntPtr.Zero);
            Thread.Sleep(50);
            keybd_event(VK_DELETE, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            keybd_event(VK_MENU, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            keybd_event(VK_CONTROL, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            Thread.Sleep(30);

            // Escape tuşu (UAC veya geçici ekran kilitlerini serbest bırakmak için)
            keybd_event(VK_ESCAPE, 0, 0, UIntPtr.Zero);
            Thread.Sleep(20);
            keybd_event(VK_ESCAPE, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        }
        catch { }
    }
}
