using System.Runtime.InteropServices;

namespace NexMote.Agent.Windows.Platform;

/// <summary>
/// Windows Servisi (Session 0 - LocalSystem) seviyesinde en üst düzey çekirdek yetkisiyle
/// Güvenli Dikkat Dizisi (Secure Attention Sequence - Ctrl+Alt+Del) üreten yardımcı sınıf.
/// </summary>
internal static class SasServiceHelper
{
    [DllImport("sas.dll", SetLastError = true)]
    private static extern void SendSAS(bool asUser);

    public static void SendSas()
    {
        try
        {
            // LocalSystem yetkisiyle çağrıldığında asUser=false zorunludur ve Winlogon'a doğrudan SAS iletir
            SendSAS(false);
        }
        catch { }
    }
}
