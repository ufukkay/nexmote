using Microsoft.Extensions.Options;
using Microsoft.Win32;
using System.Net;
using System.Text.Json;

namespace NexMote.Agent.Windows;

/// <summary>
/// Hedef Windows makinesinde LocalSystem ayrıcalığıyla arka planda çalışan ana Windows Servis işçisi (Worker).
/// 
/// Temel Görevleri:
/// 1. Cihazın sunucuya ilk kaydını (Enrollment) ve 20 saniyelik periyodik canlılık/telemetri bildirimlerini (Heartbeat) yönetir.
/// 2. UAC (Kullanıcı Hesabı Denetimi) pencerelerinin uzaktan görünür olmasını sağlamak için PromptOnSecureDesktop registry kaydını düzenler.
/// 3. Tray uygulamasını ve SYSTEM yetkili Girdi Yardımcısını (--input-helper) aktif kullanıcı oturumunda sürekli ayakta tutar.
/// 4. Tray tarafından indirilen bekleyen güncellemeleri (pending-update.msi) LocalSystem yetkisiyle sessizce kurar.
/// 5. 1 saniyelik Session Watchdog ile kullanıcı değişimi veya kilit ekranı durumlarında süreçleri anında canlandırır.
/// </summary>
public sealed class Worker : BackgroundService
{
    private readonly AgentClient _client;
    private readonly DeviceIdentityStore _identityStore;
    private readonly ILogger<Worker> _logger;
    private readonly IOptionsMonitor<AgentOptions> _optionsMonitor;

    public Worker(
        AgentClient client,
        DeviceIdentityStore identityStore,
        IOptionsMonitor<AgentOptions> optionsMonitor,
        ILogger<Worker> logger)
    {
        _client = client;
        _identityStore = identityStore;
        _logger = logger;
        _optionsMonitor = optionsMonitor;

        // appsettings.json değiştiğinde kimliği sıfırlayıp yeni sunucuya yeniden kaydol
        _optionsMonitor.OnChange(options =>
        {
            _logger.LogInformation("Agent ayarları değişti. Yeni ServerUrl için kimlik sıfırlanıyor: {ServerUrl}", options.ServerUrl);
            _identityStore.Delete();
        });
    }

    /// <summary>
    /// Servis ana yürütme döngüsü.
    /// </summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        DeviceIdentity? identity = null;

        // 1. UAC ve SAS politikalarını uzaktan desteğe uygun hale getir
        EnsureUacVisibleToRemoteSupport();

        // 2. Açılışta hiç beklemeden Tray ve InputHelper süreçlerini aktif konsol oturumuna (Winlogon / Default) enjekte et
        EnsureTrayRunning();
        EnsureInputHelperRunning();

        // 3. Kullanıcı değişimi ve kilit ekranı için 1 saniyelik hızlı oturum gözlemcisini başlat
        _ = Task.Run(() => RunSessionWatchdogAsync(stoppingToken), stoppingToken);

        var isFirstSuccess = false;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Kimlik yoksa veya sıfırlandıysa diskten yükle veya sunucuya kaydol
                if (identity is null)
                {
                    identity = await _identityStore.LoadAsync(stoppingToken);

                    if (identity is null)
                    {
                        _logger.LogInformation("NexMote cihaz kaydı başlatılıyor...");
                        identity = await _client.EnrollAsync(stoppingToken);
                        await _identityStore.SaveAsync(identity, stoppingToken);
                        _logger.LogInformation("Kayıt başarıyla tamamlandı. DeviceId: {DeviceId}", identity.DeviceId);
                    }
                }

                // Sunucuya telemetri ve canlılık sinyali gönder
                await _client.SendHeartbeatAsync(identity, stoppingToken);
                _logger.LogInformation("Heartbeat iletildi. DeviceId: {DeviceId}", identity.DeviceId);
                isFirstSuccess = true;

                // Oturum süreçlerini kontrol et ve bekleyen güncellemeleri kur
                EnsureTrayRunning();
                EnsureInputHelperRunning();
                CheckPendingUpdate();

                await Task.Delay(TimeSpan.FromSeconds(_optionsMonitor.CurrentValue.HeartbeatSeconds), stoppingToken);
            }
            catch (HttpRequestException ex) when (ex.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Unauthorized)
            {
                _logger.LogWarning(ex, "Sunucu cihaz kimliğini reddetti (404/401). Cihaz yeniden kaydolacak.");
                _identityStore.Delete();
                identity = null;
                await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Kayıtlı kimlik dosyası bozuk. Yeniden oluşturulacak.");
                _identityStore.Delete();
                identity = null;
                await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Agent döngüsünde ağ/bağlantı hatası oluştu. Yeniden denenecek.");

                // Açılış anında ağ henüz bağlanmamışsa 20 saniye beklemek yerine 1 saniyede bir hızlıca tekrar dene
                var delaySeconds = isFirstSuccess ? _optionsMonitor.CurrentValue.HeartbeatSeconds : 1;
                await Task.Delay(TimeSpan.FromSeconds(delaySeconds), stoppingToken);
            }
        }
    }

    /// <summary>
    /// UAC istemleri varsayılan olarak izole "Secure Desktop" üzerinde açılır ve ekran yakalama araçları tarafından görülemez.
    /// PromptOnSecureDesktop = 0 ayarı yapılarak UAC istemlerinin normal masaüstünde açılması ve uzaktan teknisyen tarafından görülebilmesi sağlanır.
    /// SoftwareSASGeneration = 3 ayarı ile yazılımsal Ctrl+Alt+Del (SAS) gönderimine izin verilir.
    /// </summary>
    private void EnsureUacVisibleToRemoteSupport()
    {
        try
        {
            using var key = Registry.LocalMachine.CreateSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System", writable: true);
            key?.SetValue("PromptOnSecureDesktop", 0, RegistryValueKind.DWord);
            key?.SetValue("SoftwareSASGeneration", 3, RegistryValueKind.DWord);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "UAC ve SAS kayıt defteri politikaları ayarlanamadı.");
        }
    }

    /// <summary>
    /// Kullanıcı oturumundaki Tray uygulaması tarafından indirilen bekleyen güncellemeyi (pending-update.msi)
    /// LocalSystem ayrıcalığı ile sessizce kurar. Kullanıcıya herhangi bir UAC istemi çıkmaz.
    /// </summary>
    private void CheckPendingUpdate()
    {
        var programDataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "NexMote", "Agent");
        var pendingMsi = Path.Combine(programDataDir, "pending-update.msi");

        if (!File.Exists(pendingMsi))
        {
            return;
        }

        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo("msiexec.exe", $"/i \"{pendingMsi}\" /qn /norestart")
            {
                UseShellExecute = false,
                CreateNoWindow = true
            };
            System.Diagnostics.Process.Start(psi);
            File.Delete(pendingMsi);
            _logger.LogInformation("Bekleyen Agent güncellemesi başlatıldı: {Path}", pendingMsi);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Bekleyen güncelleme kurulumu başlatılamadı.");
        }
    }

    /// <summary>
    /// SYSTEM yetkisinde çalışan Girdi Yardımcısını (Agent.Tray.exe --input-helper) aktif konsol oturumunda başlatır.
    /// Bu yardımcı, UIPI sınırını aşarak UAC onay pencerelerine tıklama yapılabilmesini sağlar.
    /// </summary>
    private void EnsureInputHelperRunning()
    {
        try
        {
            var trayExePath = Path.Combine(AppContext.BaseDirectory, "NexMote.Agent.Tray.exe");
            if (!File.Exists(trayExePath))
            {
                return;
            }

            if (!SessionProcessLauncher.TryLaunchInActiveSession(trayExePath, "--input-helper", out var error))
            {
                _logger.LogWarning("Aktif oturumda input-helper başlatılamadı: {Error}", error);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "EnsureInputHelperRunning çağrısında hata.");
        }
    }

    /// <summary>
    /// Oturum değişimlerini (kullanıcı değiştirme, oturum açma/kapatma) 1 saniye aralıklarla izleyerek
    /// tepsi simgesi ve yardımcı süreçlerin her an aktif olmasını sağlayan hızlı gözlemci.
    /// </summary>
    private async Task RunSessionWatchdogAsync(CancellationToken stoppingToken)
    {
        uint lastSessionId = uint.MaxValue;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var activeSession = SessionProcessLauncher.GetActiveConsoleSessionId();
                if (activeSession != 0xFFFFFFFF)
                {
                    var isSessionChanged = activeSession != lastSessionId;
                    var isTrayRunning = SessionProcessLauncher.IsProcessRunningInSession("NexMote.Agent.Tray", activeSession);

                    if (isSessionChanged || !isTrayRunning)
                    {
                        lastSessionId = activeSession;
                        _logger.LogInformation("Aktif oturum: {SessionId} (Değişti: {Changed}, TrayÇalışıyor: {Running}). Süreçler başlatılıyor.", activeSession, isSessionChanged, isTrayRunning);

                        var trayExePath = Path.Combine(AppContext.BaseDirectory, "NexMote.Agent.Tray.exe");
                        if (File.Exists(trayExePath))
                        {
                            if (!isTrayRunning)
                            {
                                SessionProcessLauncher.TryLaunchInActiveSession(trayExePath, "--tray", out _);
                            }
                            EnsureInputHelperRunning();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Oturum gözlemci kontrolünde hata.");
            }

            try
            {
                await Task.Delay(1000, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    /// <summary>
    /// Kullanıcı masaüstünde Tray uygulamasının çalıştığından emin olur.
    /// </summary>
    private void EnsureTrayRunning()
    {
        try
        {
            var activeSession = SessionProcessLauncher.GetActiveConsoleSessionId();
            if (activeSession == 0xFFFFFFFF) return;

            var isRunningInActiveSession = SessionProcessLauncher.IsProcessRunningInSession("NexMote.Agent.Tray", activeSession);
            if (!isRunningInActiveSession)
            {
                var trayExePath = Path.Combine(AppContext.BaseDirectory, "NexMote.Agent.Tray.exe");
                if (File.Exists(trayExePath))
                {
                    if (!SessionProcessLauncher.TryLaunchInActiveSession(trayExePath, "--tray", out var error))
                    {
                        _logger.LogWarning("Aktif oturumda ({SessionId}) Tray başlatılamadı: {Error}", activeSession, error);

                        // Alternatif olarak Windows Zamanlanmış Görevi üzerinden tetikle (yalnızca başlatılamadıysa fallback)
                        try
                        {
                            var psi = new System.Diagnostics.ProcessStartInfo("schtasks", "/run /tn \"NexMote Agent Tray\"")
                            {
                                CreateNoWindow = true,
                                UseShellExecute = false
                            };
                            System.Diagnostics.Process.Start(psi);
                        }
                        catch { }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "EnsureTrayRunning çağrısında hata.");
        }
    }
}
