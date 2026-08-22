using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Options;
using Microsoft.Win32;
using System.Net;
using System.Text.Json;
using NexMote.Shared.Commands;
using NexMote.Shared.Identity;
using NexMote.Shared.Network;
using Polly;

namespace NexMote.Agent.Windows;

/// <summary>
/// Hedef Windows makinesinde LocalSystem ayrıcalığıyla arka planda çalışan ana Windows Servis işçisi (Worker).
/// </summary>
public sealed class Worker : BackgroundService
{
    /// <summary>
    /// Heartbeat/enroll'dan art arda kaç kez 404 (cihaz bulunamadı) alınırsa, "cihaz panelden silindi"
    /// kabul edilip sessiz kendi kendini kaldırma tetiklenir. Tek seferlik 404, geçici bir sunucu/ağ
    /// sorunundan (redeploy, ters proxy takılması) kaynaklanabileceğinden doğrudan silme yapılmaz.
    /// </summary>
    private const int NotFoundConfirmationThreshold = 3;

    private readonly AgentClient _client;
    private readonly DeviceIdentityStore _identityStore;
    private readonly ILogger<Worker> _logger;
    private readonly IOptionsMonitor<AgentOptions> _optionsMonitor;
    private HubConnection? _hubConnection;

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
        var consecutiveNotFoundCount = 0;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Kimlik yoksa veya sıfırlandıysa diskten yükle veya sunucuya kaydol
                if (identity is null)
                {
                    identity = _identityStore.Load();

                    if (identity is null)
                    {
                        _logger.LogInformation("NexMote cihaz kaydı başlatılıyor...");
                        identity = await _client.EnrollAsync(stoppingToken);
                        _identityStore.Save(identity);
                        _logger.LogInformation("Kayıt başarıyla tamamlandı. DeviceId: {DeviceId}", identity.DeviceId);
                    }
                }

                // Sunucuya telemetri ve canlılık sinyali gönder
                await _client.SendHeartbeatAsync(identity, stoppingToken);
                _logger.LogInformation("Heartbeat iletildi. DeviceId: {DeviceId}", identity.DeviceId);
                isFirstSuccess = true;
                consecutiveNotFoundCount = 0;

                // SignalR üzerinden doğrudan web terminal komutlarını dinle (SYSTEM yetkisiyle)
                await EnsureHubConnectedAsync(identity, stoppingToken);

                // Oturum süreçlerini kontrol et ve bekleyen güncellemeleri kur
                EnsureTrayRunning();
                EnsureInputHelperRunning();
                CheckPendingUpdate();

                await Task.Delay(TimeSpan.FromSeconds(_optionsMonitor.CurrentValue.HeartbeatSeconds), stoppingToken);
            }
            catch (HttpRequestException ex) when (ex.StatusCode is HttpStatusCode.NotFound)
            {
                consecutiveNotFoundCount++;
                _logger.LogWarning(
                    "Sunucu cihaz kimliğini bulamadı (404). ({Count}/{Threshold} art arda doğrulama)",
                    consecutiveNotFoundCount, NotFoundConfirmationThreshold);

                if (consecutiveNotFoundCount < NotFoundConfirmationThreshold)
                {
                    // Tek seferlik 404, sunucunun kısa süreli redeploy'u, ters proxy takılması ya da başka
                    // geçici bir aksaklıktan kaynaklanmış olabilir. Hemen kendi kendini kaldırmak yerine
                    // kimliği sıfırlayıp kısa süre sonra tekrar dene; sadece art arda NotFoundConfirmationThreshold
                    // kez doğrulanırsa "cihaz panelden silindi" kabul edilip sessiz temizleme tetiklenir.
                    _identityStore.Delete();
                    identity = null;
                    await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
                    continue;
                }

                _logger.LogWarning(
                    "Sunucu cihazı {Count} kez art arda bulamadı. Cihaz panelden silinmiş kabul ediliyor, sessiz temizleme başlatılıyor...",
                    consecutiveNotFoundCount);
                try
                {
                    var cleanerExe = Path.Combine(AppContext.BaseDirectory, "NexMote.Cleaner.exe");
                    if (File.Exists(cleanerExe))
                    {
                        var tempExe = Path.Combine(Path.GetTempPath(), $"NexMote_DeepCleaner_{Guid.NewGuid():N}.exe");
                        File.Copy(cleanerExe, tempExe, overwrite: true);
                        var psi = new System.Diagnostics.ProcessStartInfo(tempExe, "--silent --from-temp")
                        {
                            UseShellExecute = false,
                            CreateNoWindow = true
                        };
                        System.Diagnostics.Process.Start(psi);
                        return;
                    }
                    else
                    {
                        var cmd = "timeout /t 2 /nobreak & sc.exe stop \"NexMote Agent\" & sc.exe delete \"NexMote Agent\" & taskkill /F /T /IM NexMote* & reg delete \"HKLM\\Software\\Microsoft\\Windows\\CurrentVersion\\Run\" /v \"NexMoteAgentTray\" /f & reg delete \"HKLM\\Software\\NexMote\" /f & rmdir /s /q \"%ProgramFiles%\\NexMote\" & rmdir /s /q \"%ProgramData%\\NexMote\"";
                        var psi = new System.Diagnostics.ProcessStartInfo("cmd.exe", $"/c {cmd}")
                        {
                            UseShellExecute = false,
                            CreateNoWindow = true
                        };
                        System.Diagnostics.Process.Start(psi);
                        return;
                    }
                }
                catch { }

                _identityStore.Delete();
                identity = null;
                await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
            }
            catch (HttpRequestException ex) when (ex.StatusCode is HttpStatusCode.Unauthorized)
            {
                // 401 sadece enrollment sırasında yanlış/eskimiş EnrollmentKey durumunda döner (heartbeat
                // endpoint'i asla 401 döndürmez) — bu bir yapılandırma sorunudur, "cihaz silindi" anlamına
                // gelmez. Ajanı silmek yerine sadece kimliği sıfırlayıp tekrar dene.
                _logger.LogWarning("Sunucu isteği yetkisiz (401) reddetti — EnrollmentKey hatalı/eskimiş olabilir. Kimlik sıfırlanıp tekrar denenecek.");
                _identityStore.Delete();
                identity = null;
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
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
    /// Windows Servisinin SignalR Hub'ına doğrudan bağlanarak web konsolundan gelen uzak komutları
    /// tam NT AUTHORITY\SYSTEM (Yönetici) ayrıcalığıyla ve UAC engeli olmadan çalıştırmasını sağlar.
    /// </summary>
    private async Task EnsureHubConnectedAsync(DeviceIdentity identity, CancellationToken cancellationToken)
    {
        if (_hubConnection is not null && _hubConnection.State == HubConnectionState.Connected)
        {
            return;
        }

        try
        {
            if (_hubConnection is not null)
            {
                try { await _hubConnection.DisposeAsync(); } catch { }
                _hubConnection = null;
            }

            var serverUrl = _optionsMonitor.CurrentValue.ServerUrl?.TrimEnd('/') ?? "https://nexmote.com";
            var hubUrl = $"{serverUrl}/hubs/signaling";

            _hubConnection = new HubConnectionBuilder()
                .WithUrl(hubUrl, options =>
                {
                    options.HttpMessageHandlerFactory = _ => NexMoteHttp.CreateHandler();
                })
                .WithAutomaticReconnect()
                .Build();

            _hubConnection.On<Guid, string, string, bool>("ExecuteWebCommand", async (requestId, shell, command, runAsAdmin) =>
            {
                _logger.LogInformation("Web terminal komutu alındı: [{Shell}] {Command}", shell, command);
                var result = await CommandRunner.RunAsync(shell, command, 60000);
                try
                {
                    if (_hubConnection?.State == HubConnectionState.Connected)
                    {
                        await _hubConnection.InvokeAsync("SubmitCommandResult",
                            requestId,
                            result.ExitCode,
                            result.StdOut,
                            result.StdErr,
                            result.DurationMs,
                            result.TimedOut,
                            result.ElevationDenied);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Komut sonucu sunucuya iletilemedi.");
                }

                // Bu, üründeki en yetkili (tam SYSTEM) komut çalıştırma yoludur — Tray'in eşdeğer
                // handler'larıyla tutarlı olması için denetim kaydı da burada yazılmalı, aksi halde
                // CommandAudits tablosunda sessiz bir uyumluluk boşluğu oluşur.
                _ = _client.PostCommandAuditAsync(identity, Guid.Empty, shell, command, result.ExitCode, result.StdOut, result.StdErr, result.DurationMs, cancellationToken);
            });

            _hubConnection.On("RemoteUninstallRequested", () =>
            {
                _logger.LogInformation("Sunucudan uzaktan sessiz ajan kaldırma isteği alındı. Temizleme süreci başlatılıyor...");
                try
                {
                    var cleanerExe = Path.Combine(AppContext.BaseDirectory, "NexMote.Cleaner.exe");
                    if (File.Exists(cleanerExe))
                    {
                        var tempExe = Path.Combine(Path.GetTempPath(), $"NexMote_DeepCleaner_{Guid.NewGuid():N}.exe");
                        File.Copy(cleanerExe, tempExe, overwrite: true);
                        var psi = new System.Diagnostics.ProcessStartInfo(tempExe, "--silent --from-temp")
                        {
                            UseShellExecute = false,
                            CreateNoWindow = true
                        };
                        System.Diagnostics.Process.Start(psi);
                    }
                    else
                    {
                        var cmd = "timeout /t 2 /nobreak & sc.exe stop \"NexMote Agent\" & sc.exe delete \"NexMote Agent\" & taskkill /F /T /IM NexMote* & reg delete \"HKLM\\Software\\Microsoft\\Windows\\CurrentVersion\\Run\" /v \"NexMoteAgentTray\" /f & reg delete \"HKLM\\Software\\NexMote\" /f & rmdir /s /q \"%ProgramFiles%\\NexMote\" & rmdir /s /q \"%ProgramData%\\NexMote\"";
                        var psi = new System.Diagnostics.ProcessStartInfo("cmd.exe", $"/c {cmd}")
                        {
                            UseShellExecute = false,
                            CreateNoWindow = true
                        };
                        System.Diagnostics.Process.Start(psi);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Uzaktan kaldırma başlatılamadı.");
                }
            });

            await _hubConnection.StartAsync(cancellationToken);
            await _hubConnection.InvokeAsync("JoinDevice", identity.DeviceId, identity.AgentToken, cancellationToken);
            _logger.LogInformation("Windows Servisi SignalR Hub'ına başarıyla bağlandı ve dinlemeye başladı.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SignalR Hub bağlantısı kurulamadı.");
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
            var logPath = Path.Combine(programDataDir, "update.log");
            var psi = new System.Diagnostics.ProcessStartInfo("msiexec.exe", $"/i \"{pendingMsi}\" /qn /norestart /l*v \"{logPath}\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true
            };
            var process = System.Diagnostics.Process.Start(psi);
            if (process != null)
            {
                _logger.LogInformation("Bekleyen Agent güncellemesi başlatıldı: {Path}", pendingMsi);
                _ = Task.Run(() =>
                {
                    try
                    {
                        process.WaitForExit(180_000); // 3 dakikaya kadar kurulumu bekle
                        _logger.LogInformation("Güncelleme yükleyicisi tamamlandı. Çıkış Kodu: {Code}", process.ExitCode);
                    }
                    catch { }
                    finally
                    {
                        try
                        {
                            if (File.Exists(pendingMsi))
                            {
                                File.Delete(pendingMsi);
                            }
                        }
                        catch { }
                    }
                });
            }
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
            var activeSession = SessionProcessLauncher.GetActiveConsoleSessionId();
            if (activeSession != 0xFFFFFFFF && SessionProcessLauncher.IsInputHelperRunningInSession(activeSession))
            {
                return;
            }

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
                    var isUserLoggedIn = SessionProcessLauncher.IsUserLoggedIn(activeSession);
                    var isTrayRunning = SessionProcessLauncher.IsTrayRunningInSession(activeSession);
                    var isInputHelperRunning = SessionProcessLauncher.IsInputHelperRunningInSession(activeSession);
                    var isSystemStreamerRunning = SessionProcessLauncher.IsSystemSessionStreamerRunningInSession(activeSession);

                    var trayExePath = Path.Combine(AppContext.BaseDirectory, "NexMote.Agent.Tray.exe");
                    if (File.Exists(trayExePath))
                    {
                        if (isUserLoggedIn)
                        {
                            // 1. Kullanıcı oturum açtığında bildirim alanı (Tray) ve Durum Paneli için kullanıcı yetkisiyle başlat
                            if (!isTrayRunning)
                            {
                                _logger.LogInformation("Kullanıcı oturumu aktif ({SessionId}). Tray başlatılıyor...", activeSession);
                                if (!SessionProcessLauncher.TryLaunchInActiveSessionAsUser(trayExePath, "--tray", out var launchErr))
                                {
                                    _logger.LogDebug("TryLaunchInActiveSessionAsUser ({Error}), SYSTEM olarak deneniyor...", launchErr);
                                    SessionProcessLauncher.TryLaunchInActiveSession(trayExePath, "--tray", out _);
                                }
                            }

                            // 2. SYSTEM yetkili Girdi Yardımcısı her zaman aktif olmalıdır (Kilit ekranı ve UAC pencereleri için)
                            if (!isInputHelperRunning)
                            {
                                EnsureInputHelperRunning();
                            }
                        }
                        else
                        {
                            // 3. Kullanıcı henüz giriş yapmamış (Windows Giriş/Kilit Ekranı):
                            // SYSTEM yetkili Canlı Oturum Yayıncısını başlat ve aktif tut
                            if (!isSystemStreamerRunning)
                            {
                                _logger.LogInformation("Giriş/Kilit ekranı aktif ({SessionId}). SYSTEM oturum yayıncısı başlatılıyor...", activeSession);
                                SessionProcessLauncher.TryLaunchInActiveSession(trayExePath, "--system-session", out _);
                            }
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

            var isRunningInActiveSession = SessionProcessLauncher.IsTrayRunningInSession(activeSession);
            if (!isRunningInActiveSession)
            {
                var trayExePath = Path.Combine(AppContext.BaseDirectory, "NexMote.Agent.Tray.exe");
                if (File.Exists(trayExePath))
                {
                    if (!SessionProcessLauncher.TryLaunchInActiveSessionAsUser(trayExePath, "--tray", out var error))
                    {
                        _logger.LogDebug("Tray kullanıcı olarak başlatılamadı ({Error}), SYSTEM fallback deneniyor...", error);
                        if (!SessionProcessLauncher.TryLaunchInActiveSession(trayExePath, "--tray", out var sysError))
                        {
                            _logger.LogWarning("Aktif oturumda ({SessionId}) Tray başlatılamadı: {Error}", activeSession, sysError);
                        }
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
