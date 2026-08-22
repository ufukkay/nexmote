using System.Diagnostics;
using System.Security.Principal;
using System.ServiceProcess;
using Microsoft.Win32;

namespace NexMote.Cleaner;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        var isSilent = args.Any(a => a.Equals("--silent", StringComparison.OrdinalIgnoreCase) ||
                                     a.Equals("/quiet", StringComparison.OrdinalIgnoreCase) ||
                                     a.Equals("/qn", StringComparison.OrdinalIgnoreCase));

        var fromTemp = args.Any(a => a.Equals("--from-temp", StringComparison.OrdinalIgnoreCase));

        // 1. Eğer temp dışından çalışıyorsa, kendini %TEMP%'e kopyalayıp oradan çalıştır
        // Böylece Program Files veya ProgramData altındaki dosyaların silinmesi engellenmez.
        var currentExe = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName;
        var tempDir = Path.GetTempPath();

        if (!fromTemp && !string.IsNullOrEmpty(currentExe) && !currentExe.StartsWith(tempDir, StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var tempExe = Path.Combine(tempDir, $"NexMote_DeepCleaner_{Guid.NewGuid():N}.exe");
                File.Copy(currentExe, tempExe, overwrite: true);

                var passArgs = string.Join(" ", args) + " --from-temp";
                var psi = new ProcessStartInfo(tempExe, passArgs)
                {
                    UseShellExecute = true,
                    Verb = IsAdministrator() ? "" : "runas"
                };

                Process.Start(psi);
                return;
            }
            catch
            {
                // Kopyalama başarısız olursa mevcut konumdan devam et
            }
        }

        // 2. Yönetici İzni Kontrolü
        if (!IsAdministrator())
        {
            if (!isSilent)
            {
                try
                {
                    var psi = new ProcessStartInfo(currentExe!)
                    {
                        UseShellExecute = true,
                        Verb = "runas",
                        Arguments = string.Join(" ", args)
                    };
                    Process.Start(psi);
                }
                catch { }
            }
            return;
        }

        // 3. Kullanıcı Onayı (Sessiz modda değilse ve MSI tarafından tetiklenmemişse)
        var fromMsi = args.Any(a => a.Equals("--from-msi", StringComparison.OrdinalIgnoreCase));
        if (!isSilent && !fromMsi)
        {
            var confirm = MessageBox.Show(
                "Bu işlem bilgisayarınızda bulunan tüm NexMote Ajanı, Teknisyen Konsolu, Windows Servisi, Kayıt Defteri ve AppData verilerini tamamen silecektir.\n\nDevam etmek istiyor musunuz?",
                "NexMote Tam Kaldırıcı & Derin Temizleyici",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);

            if (confirm != DialogResult.Yes)
            {
                return;
            }
        }

        // 4. Derinlemesine Temizliği Çalıştır
        PerformDeepCleanup();

        // 5. Tamamlandı Bildirimi
        if (!isSilent)
        {
            MessageBox.Show(
                "NexMote başarıyla ve tamamen temizlendi!\n\n" +
                "✓ Çalışan tüm servisler ve arka plan süreçleri durduruldu.\n" +
                "✓ Windows Servisi sistemden kaldırıldı.\n" +
                "✓ Program Files ve tüm kullanıcıların AppData / ProgramData verileri silindi.\n" +
                "✓ Kayıt Defteri ve başlangıç anahtarları temizlendi.",
                "NexMote Temizleme Başarılı",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
    }

    public static void PerformDeepCleanup()
    {
        // 1. Önce Çalışan Tüm NexMote Süreçlerini ve Ağaçlarını Zorla Durdur
        KillAllProcesses();

        // 2. Windows Servisini Durdur ve Sil
        StopAndRemoveService();

        // 3. Dosya kilitlerinin tamamen serbest kalması için kısa bekleme
        Thread.Sleep(1500);

        // 4. Tekrar emniyet amaçlı süreç kontrolü
        KillAllProcesses();

        // 5. Kayıt Defteri (Registry) ve Başlangıç Anahtarlarını Sil
        CleanRegistry();

        // 6. Dosya Sistemi ve Tüm Kullanıcıların AppData / ProgramData Verilerini Sil
        CleanAllFilesAndDirectories();

        // 7. Masaüstü ve Başlat Menüsü Kısayollarını Temizle
        CleanShortcuts();
    }

    private static bool IsAdministrator()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    private static void KillAllProcesses()
    {
        var targetNames = new[] { "NexMote.Agent.Windows", "NexMote.Agent.Tray", "NexMote.TechnicianApp", "NexMote.Api", "NexMote.Cleaner" };
        var currentPid = Process.GetCurrentProcess().Id;

        foreach (var name in targetNames)
        {
            try
            {
                foreach (var p in Process.GetProcessesByName(name))
                {
                    if (p.Id == currentPid) continue;
                    try
                    {
                        p.Kill(entireProcessTree: true);
                        p.WaitForExit(2000);
                    }
                    catch { }
                }
            }
            catch { }
        }

        // Genel taskkill ile tüm NexMote* süreçlerini sonlandır
        RunHiddenCommand("taskkill.exe", $"/F /T /FI \"PID ne {currentPid}\" /IM NexMote*");
    }

    private static void StopAndRemoveService()
    {
        const string serviceName = "NexMote Agent";

        try
        {
            using var sc = new ServiceController(serviceName);
            if (sc.Status != ServiceControllerStatus.Stopped && sc.Status != ServiceControllerStatus.StopPending)
            {
                sc.Stop();
                sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(5));
            }
        }
        catch { }

        RunHiddenCommand("net.exe", $"stop \"{serviceName}\" /y");
        RunHiddenCommand("sc.exe", $"stop \"{serviceName}\"");
        RunHiddenCommand("sc.exe", $"delete \"{serviceName}\"");
    }

    private static void CleanRegistry()
    {
        // 1. Run keys (Başlangıçta otomatik açılma)
        TryDeleteRegistryValue(Registry.LocalMachine, @"Software\Microsoft\Windows\CurrentVersion\Run", "NexMoteAgentTray");
        TryDeleteRegistryValue(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Run", "NexMoteAgentTray");

        // 2. NexMote Software Keys
        TryDeleteRegistrySubKeyTree(Registry.LocalMachine, @"Software\NexMote");
        TryDeleteRegistrySubKeyTree(Registry.CurrentUser, @"Software\NexMote");
        TryDeleteRegistrySubKeyTree(Registry.LocalMachine, @"Software\WOW6432Node\NexMote");

        // 3. nexmote:// URL Protocol Kayıtları
        TryDeleteRegistrySubKeyTree(Registry.ClassesRoot, @"nexmote");
        TryDeleteRegistrySubKeyTree(Registry.LocalMachine, @"SOFTWARE\Classes\nexmote");
        TryDeleteRegistrySubKeyTree(Registry.CurrentUser, @"Software\Classes\nexmote");
    }

    private static void TryDeleteRegistryValue(RegistryKey root, string subKey, string valueName)
    {
        try
        {
            using var key = root.OpenSubKey(subKey, writable: true);
            key?.DeleteValue(valueName, throwOnMissingValue: false);
        }
        catch { }
    }

    private static void TryDeleteRegistrySubKeyTree(RegistryKey root, string subKey)
    {
        try
        {
            root.DeleteSubKeyTree(subKey, throwOnMissingSubKey: false);
        }
        catch { }
    }

    private static void CleanAllFilesAndDirectories()
    {
        var targetDirectories = new List<string>
        {
            // Program Files
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "NexMote"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "NexMote"),
            
            // ProgramData
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "NexMote"),

            // Current User AppData
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NexMote"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NexMote")
        };

        // Bilgisayardaki TÜM kullanıcı profillerinin AppData\Local ve AppData\Roaming dizinlerini tara
        try
        {
            var usersBase = Path.GetDirectoryName(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
            if (!string.IsNullOrEmpty(usersBase) && Directory.Exists(usersBase))
            {
                foreach (var userDir in Directory.GetDirectories(usersBase))
                {
                    try
                    {
                        var localNexMote = Path.Combine(userDir, "AppData", "Local", "NexMote");
                        var roamingNexMote = Path.Combine(userDir, "AppData", "Roaming", "NexMote");

                        if (!targetDirectories.Contains(localNexMote, StringComparer.OrdinalIgnoreCase))
                            targetDirectories.Add(localNexMote);

                        if (!targetDirectories.Contains(roamingNexMote, StringComparer.OrdinalIgnoreCase))
                            targetDirectories.Add(roamingNexMote);
                    }
                    catch { }
                }
            }
        }
        catch { }

        // Dizinleri sil
        foreach (var dir in targetDirectories)
        {
            ForceDeleteDirectory(dir);
        }
    }

    private static void ForceDeleteDirectory(string path)
    {
        if (!Directory.Exists(path)) return;

        try
        {
            // Önce tüm alt dosya ve klasörlerin ReadOnly / System özniteliklerini kaldır
            var dirInfo = new DirectoryInfo(path);
            foreach (var file in dirInfo.GetFiles("*", SearchOption.AllDirectories))
            {
                try
                {
                    file.Attributes = FileAttributes.Normal;
                    file.Delete();
                }
                catch { }
            }

            foreach (var subDir in dirInfo.GetDirectories("*", SearchOption.AllDirectories))
            {
                try
                {
                    subDir.Attributes = FileAttributes.Normal;
                }
                catch { }
            }

            Directory.Delete(path, recursive: true);
        }
        catch
        {
            // Windows rmdir ile ikinci kez zorla silmeyi dene
            RunHiddenCommand("cmd.exe", $"/c rmdir /s /q \"{path}\"");
        }
    }

    private static void CleanShortcuts()
    {
        try
        {
            var publicDesktop = Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);
            var userDesktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);

            foreach (var desktop in new[] { publicDesktop, userDesktop })
            {
                if (Directory.Exists(desktop))
                {
                    foreach (var file in Directory.GetFiles(desktop, "*NexMote*.lnk"))
                    {
                        try
                        {
                            File.SetAttributes(file, FileAttributes.Normal);
                            File.Delete(file);
                        }
                        catch { }
                    }
                }
            }

            var commonPrograms = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), "NexMote");
            var userPrograms = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "NexMote");

            ForceDeleteDirectory(commonPrograms);
            ForceDeleteDirectory(userPrograms);
        }
        catch { }
    }

    private static void RunHiddenCommand(string exe, string arguments)
    {
        try
        {
            var psi = new ProcessStartInfo(exe, arguments)
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            using var proc = Process.Start(psi);
            proc?.WaitForExit(3000);
        }
        catch { }
    }
}
