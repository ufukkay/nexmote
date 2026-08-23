using System.Diagnostics;
using System.Net.Http.Json;
using System.Security.Principal;
using System.ServiceProcess;
using System.Text.Json;
using Microsoft.Win32;
using NexMote.Shared.Contracts;
using NexMote.Shared.Identity;
using NexMote.Shared.Network;

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

        // Script'li/yetkili kaldırma için: --password=<sifre> (silent modda etkileşimli soru sorulamaz)
        var passwordArg = args
            .Select(a => a.StartsWith("--password=", StringComparison.OrdinalIgnoreCase) ? a["--password=".Length..] : null)
            .FirstOrDefault(v => v is not null);

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

        // 2.5 Kurumsal Kaldırma Koruması: cihaza atanmış bir güvenlik profili varsa ve kaldırma şifresi
        // istiyorsa, sunucuda doğrulanmadan devam edilmez (fail-closed — ağ/sunucu erişilemezse de durur).
        if (!VerifyUninstallProtectionAsync(passwordArg, isSilent).GetAwaiter().GetResult())
        {
            if (!isSilent)
            {
                MessageBox.Show(
                    "Kaldırma işlemi iptal edildi: şifre doğrulanamadı veya sunucuya ulaşılamadı.",
                    "NexMote Tam Kaldırıcı & Derin Temizleyici",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
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

    /// <summary>
    /// Cihaza atanmış bir güvenlik profili "kaldırma şifresi" istiyorsa, kullanıcıdan (veya <c>--password=</c>
    /// argümanından) alınan şifreyi sunucuda doğrular. Profil yoksa/kısıtlama kapalıysa true (izinli) döner.
    /// Doğrulanamazsa (yanlış şifre, ağ/sunucu erişilemez, kimlik dosyası yok) false döner — fail-closed.
    /// </summary>
    private static async Task<bool> VerifyUninstallProtectionAsync(string? passwordArg, bool isSilent)
    {
        DeviceIdentity? identity;
        try
        {
            identity = new DeviceIdentityStore().Load();
        }
        catch
        {
            identity = null;
        }

        // Ajan hiç kaydolmamış/kimliği bulunamıyorsa koruyacak bir profil de yok demektir — engellemeye gerek yok.
        if (identity is null)
        {
            return true;
        }

        var serverUrl = LoadServerUrl();

        AgentSecurityProfileResponse? profile;
        try
        {
            using var http = NexMoteHttp.CreateClient(TimeSpan.FromSeconds(10));
            var url = $"{serverUrl.TrimEnd('/')}/api/agents/{identity.DeviceId}/security-profile?agentToken={Uri.EscapeDataString(identity.AgentToken)}";
            var response = await http.GetAsync(url);
            profile = response.IsSuccessStatusCode ? await response.Content.ReadFromJsonAsync<AgentSecurityProfileResponse>() : null;
        }
        catch
        {
            // Sunucuya ulaşılamıyor — koruma gerektirip gerektirmediğini bilemeyiz, güvenli taraf: devam etme.
            return false;
        }

        if (profile?.RequireUninstallPassword != true)
        {
            return true;
        }

        if (isSilent)
        {
            // Silent modda etkileşimli soru sorulamaz — sadece --password= argümanıyla geçilebilir.
            return !string.IsNullOrEmpty(passwordArg) && await VerifyPasswordAsync(serverUrl, identity, passwordArg);
        }

        if (!string.IsNullOrEmpty(passwordArg) && await VerifyPasswordAsync(serverUrl, identity, passwordArg))
        {
            return true;
        }

        while (true)
        {
            var password = PromptForPassword();
            if (password is null) return false; // kullanıcı iptal etti

            if (await VerifyPasswordAsync(serverUrl, identity, password))
            {
                return true;
            }

            MessageBox.Show("Şifre hatalı.", "NexMote", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private static async Task<bool> VerifyPasswordAsync(string serverUrl, DeviceIdentity identity, string password)
    {
        try
        {
            using var http = NexMoteHttp.CreateClient(TimeSpan.FromSeconds(10));
            var url = $"{serverUrl.TrimEnd('/')}/api/agents/{identity.DeviceId}/security/verify";
            var response = await http.PostAsJsonAsync(url, new SecurityVerifyRequest(identity.AgentToken, "uninstall", password));
            if (!response.IsSuccessStatusCode) return false;
            var result = await response.Content.ReadFromJsonAsync<SecurityVerifyResponse>();
            return result?.Ok == true;
        }
        catch
        {
            return false;
        }
    }

    private static string? PromptForPassword()
    {
        using var form = new Form
        {
            Text = "Ajanı Kaldır",
            Width = 380,
            Height = 170,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterScreen,
            MaximizeBox = false,
            MinimizeBox = false,
            TopMost = true
        };

        var label = new Label { Text = "Ajanı kaldırmak için şifre girin:", Left = 16, Top = 16, Width = 336, Height = 40 };
        var textBox = new TextBox { Left = 16, Top = 58, Width = 336, PasswordChar = '●' };
        var okButton = new Button { Text = "Tamam", Left = 196, Top = 92, Width = 75, DialogResult = DialogResult.OK };
        var cancelButton = new Button { Text = "İptal", Left = 277, Top = 92, Width = 75, DialogResult = DialogResult.Cancel };

        form.Controls.Add(label);
        form.Controls.Add(textBox);
        form.Controls.Add(okButton);
        form.Controls.Add(cancelButton);
        form.AcceptButton = okButton;
        form.CancelButton = cancelButton;

        return form.ShowDialog() == DialogResult.OK ? textBox.Text : null;
    }

    /// <summary>Windows Servisi/Tray ile aynı kaynaktan (%ProgramData%\NexMote\Agent\appsettings.json) sunucu URL'ini okur.</summary>
    private static string LoadServerUrl()
    {
        try
        {
            var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            var path = Path.Combine(programData, "NexMote", "Agent", "appsettings.json");
            if (File.Exists(path))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(path));
                if (doc.RootElement.TryGetProperty("Agent", out var agent) &&
                    agent.TryGetProperty("ServerUrl", out var prop) &&
                    prop.GetString() is { Length: > 0 } url)
                {
                    return NexMoteHttp.EnforceProductionUrl(url);
                }
            }
        }
        catch { }

        return "https://nexmote.com";
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
