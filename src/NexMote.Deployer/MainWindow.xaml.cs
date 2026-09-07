using System.Collections.ObjectModel;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Windows;
using Microsoft.Win32;
using NexMote.Deployer.Models;
using NexMote.Deployer.Services;
using NexMote.Shared.Network;

namespace NexMote.Deployer;

public partial class MainWindow : Window
{
    private readonly ObservableCollection<DeployTarget> _targets = [];
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(30) };
    private CancellationTokenSource? _deployCts;
    private string? _customMsiPath;

    public MainWindow()
    {
        InitializeComponent();
        TargetsGrid.ItemsSource = _targets;
    }

    private void RefreshMsiBtn_Click(object sender, RoutedEventArgs e)
    {
        _ = DownloadLatestMsiAsync(showNotice: true);
    }

    private void SelectLocalMsiBtn_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "MSI Kurulum Paketleri (*.msi)|*.msi|Tüm Dosyalar (*.*)|*.*",
            Title = "NexMote Agent MSI Paketi Seçin"
        };

        if (dialog.ShowDialog() == true)
        {
            _customMsiPath = dialog.FileName;
            StatusSummaryTxt.Text = $"Yerel paket seçildi: {Path.GetFileName(_customMsiPath)} ({new FileInfo(_customMsiPath).Length / 1024 / 1024} MB)";
        }
    }

    private async Task<string?> DownloadLatestMsiAsync(bool showNotice = false)
    {
        var serverUrl = ServerUrlBox.Text.Trim().TrimEnd('/');
        var downloadUrl = $"{serverUrl}/downloads/NexMote-Agent-Setup.msi";
        var localMsi = Path.Combine(Path.GetTempPath(), "NexMote-Agent-Setup-Deploy.msi");

        try
        {
            StatusSummaryTxt.Text = "Sunucudan en güncel Ajan paketi indiriliyor...";
            var bytes = await _http.GetByteArrayAsync(downloadUrl);
            await File.WriteAllBytesAsync(localMsi, bytes);
            StatusSummaryTxt.Text = $"Güncel Ajan paketi hazır ({bytes.Length / 1024 / 1024:F1} MB).";
            if (showNotice)
            {
                MessageBox.Show(this, "En güncel NexMote Agent MSI paketi sunucudan başarıyla indirildi ve önbelleğe alındı.", "Paket İndirildi", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            return localMsi;
        }
        catch (Exception ex)
        {
            StatusSummaryTxt.Text = $"Paket indirme uyarısı: {ex.Message}";
            if (File.Exists(localMsi)) return localMsi; // Use cached version
            if (showNotice)
            {
                MessageBox.Show(this, $"Sunucudan MSI indirilemedi: {ex.Message}\nLütfen internet bağlantınızı veya sunucu adresini kontrol edin.", "Hata", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            return null;
        }
    }

    private async void StartDeployBtn_Click(object sender, RoutedEventArgs e)
    {
        var ipInput = TargetIpsBox.Text;
        var ips = IpRangeParser.Parse(ipInput);

        if (ips.Count == 0)
        {
            MessageBox.Show(this, "Lütfen en az bir geçerli IP adresi, IP aralığı veya CIDR girin (Örn: 192.168.0.126 veya 192.168.0.10-50).", "Geçersiz IP Girişi", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var username = UsernameBox.Text.Trim();
        var password = PasswordBox.Password;
        var serverUrl = ServerUrlBox.Text.Trim();
        var enrollmentKey = EnrollmentKeyBox.Text.Trim();
        var bypassTokenFilter = BypassLocalTokenFilterCheck.IsChecked == true;
        var pingFirst = PingCheckFirstCheck.IsChecked == true;

        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            var res = MessageBox.Show(this, "Yönetici kullanıcı adı veya şifre boş bırakıldı. Mevcut Windows oturumuyla devam edilsin mi?", "Kimlik Bilgisi Eksik", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (res != MessageBoxResult.Yes) return;
        }

        // Prepare MSI Package
        string? msiPath = _customMsiPath;
        if (string.IsNullOrWhiteSpace(msiPath) || !File.Exists(msiPath))
        {
            msiPath = await DownloadLatestMsiAsync(showNotice: false);
        }

        if (string.IsNullOrWhiteSpace(msiPath) || !File.Exists(msiPath))
        {
            MessageBox.Show(this, "Kurulum yapılacak NexMote Agent MSI paketi bulunamadı. Lütfen 'Yerel MSI Seç' ile bir dosya belirtin veya sunucu bağlantınızı kontrol edin.", "MSI Paketi Yok", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        // Initialize Targets in Grid
        _targets.Clear();
        foreach (var ip in ips)
        {
            _targets.Add(new DeployTarget { Ip = ip });
        }

        UpdateMetrics();
        SetDeployingState(isDeploying: true);

        _deployCts = new CancellationTokenSource();
        var ct = _deployCts.Token;

        try
        {
            var semaphore = new SemaphoreSlim(5); // Concurrency: 5 machines parallel
            int completedCount = 0;
            int total = _targets.Count;

            var tasks = _targets.Select(async target =>
            {
                await semaphore.WaitAsync(ct);
                try
                {
                    if (ct.IsCancellationRequested)
                    {
                        target.Status = DeployStatus.Skipped;
                        target.StatusText = "İptal Edildi";
                        return;
                    }

                    if (pingFirst)
                    {
                        target.Status = DeployStatus.Connecting;
                        target.StatusText = "Ping Kontrolü...";
                        bool reachable = await RemoteDeployEngine.PingTargetAsync(target, ct);
                        if (!reachable)
                        {
                            target.Status = DeployStatus.Failed;
                            target.StatusText = "Ulaşılamadı";
                            target.Details = "Hedef makineye ping veya SMB (Port 445) üzerinden ulaşılamadı. Cihaz kapalı veya ağda değil.";
                            return;
                        }
                    }

                    await RemoteDeployEngine.DeployToTargetAsync(
                        target,
                        username,
                        password,
                        msiPath,
                        serverUrl,
                        enrollmentKey,
                        bypassTokenFilter,
                        ct);
                }
                finally
                {
                    semaphore.Release();
                    Interlocked.Increment(ref completedCount);
                    Dispatcher.Invoke(() =>
                    {
                        DeployProgressBar.Value = (double)completedCount / total * 100;
                        UpdateMetrics();
                    });
                }
            });

            await Task.WhenAll(tasks);
            StatusSummaryTxt.Text = $"Toplu dağıtım tamamlandı. ({_targets.Count(t => t.Status == DeployStatus.Success)} başarılı, {_targets.Count(t => t.Status == DeployStatus.Failed)} hatalı)";
        }
        catch (OperationCanceledException)
        {
            StatusSummaryTxt.Text = "Dağıtım işlemi kullanıcı tarafından durduruldu.";
        }
        catch (Exception ex)
        {
            StatusSummaryTxt.Text = $"Dağıtım hatası: {ex.Message}";
        }
        finally
        {
            SetDeployingState(isDeploying: false);
            UpdateMetrics();
        }
    }

    private void StopDeployBtn_Click(object sender, RoutedEventArgs e)
    {
        _deployCts?.Cancel();
        StopDeployBtn.IsEnabled = false;
        StatusSummaryTxt.Text = "Durduruluyor, lütfen bekleyin...";
    }

    private async void RetryFailedBtn_Click(object sender, RoutedEventArgs e)
    {
        var failedTargets = _targets.Where(t => t.Status == DeployStatus.Failed).ToList();
        if (failedTargets.Count == 0)
        {
            MessageBox.Show(this, "Yeniden denenecek hatalı cihaz bulunamadı.", "Bilgi", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var username = UsernameBox.Text.Trim();
        var password = PasswordBox.Password;
        var serverUrl = ServerUrlBox.Text.Trim();
        var enrollmentKey = EnrollmentKeyBox.Text.Trim();
        var bypassTokenFilter = BypassLocalTokenFilterCheck.IsChecked == true;

        string? msiPath = _customMsiPath ?? await DownloadLatestMsiAsync();
        if (string.IsNullOrWhiteSpace(msiPath) || !File.Exists(msiPath)) return;

        SetDeployingState(isDeploying: true);
        _deployCts = new CancellationTokenSource();
        var ct = _deployCts.Token;

        try
        {
            var semaphore = new SemaphoreSlim(5);
            var tasks = failedTargets.Select(async target =>
            {
                await semaphore.WaitAsync(ct);
                try
                {
                    target.Status = DeployStatus.Pending;
                    target.StatusText = "Kuyrukta";
                    target.Details = "Yeniden deneniyor...";

                    await RemoteDeployEngine.DeployToTargetAsync(
                        target,
                        username,
                        password,
                        msiPath,
                        serverUrl,
                        enrollmentKey,
                        bypassTokenFilter,
                        ct);
                }
                finally
                {
                    semaphore.Release();
                    Dispatcher.Invoke(UpdateMetrics);
                }
            });

            await Task.WhenAll(tasks);
        }
        finally
        {
            SetDeployingState(isDeploying: false);
            UpdateMetrics();
        }
    }

    private void ExportCsvBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_targets.Count == 0)
        {
            MessageBox.Show(this, "Dışa aktarılacak sonuç listesi boş.", "Bilgi", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new SaveFileDialog
        {
            Filter = "CSV Dosyası (*.csv)|*.csv",
            FileName = $"NexMote-Deploy-Results-{DateTime.Now:yyyyMMdd-HHmm}.csv"
        };

        if (dialog.ShowDialog() == true)
        {
            var sb = new StringBuilder();
            sb.AppendLine("IP Adresi,Bilgisayar Adi,Ping (ms),Durum,Detay,Sure");
            foreach (var t in _targets)
            {
                sb.AppendLine($"\"{t.Ip}\",\"{t.Hostname}\",\"{t.PingDisplay}\",\"{t.StatusText}\",\"{t.Details.Replace("\"", "\"\"")}\",\"{t.Duration}\"");
            }

            File.WriteAllText(dialog.FileName, sb.ToString(), Encoding.UTF8);
            MessageBox.Show(this, "Sonuçlar CSV olarak başarıyla kaydedildi.", "Dışa Aktarma Başarılı", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void ClearListBtn_Click(object sender, RoutedEventArgs e)
    {
        _targets.Clear();
        DeployProgressBar.Value = 0;
        UpdateMetrics();
        StatusSummaryTxt.Text = "Liste temizlendi.";
    }

    private void SetDeployingState(bool isDeploying)
    {
        StartDeployBtn.IsEnabled = !isDeploying;
        StopDeployBtn.IsEnabled = isDeploying;
        TargetIpsBox.IsEnabled = !isDeploying;
        UsernameBox.IsEnabled = !isDeploying;
        PasswordBox.IsEnabled = !isDeploying;
        RetryFailedBtn.IsEnabled = !isDeploying;
        ClearListBtn.IsEnabled = !isDeploying;
    }

    private void UpdateMetrics()
    {
        int total = _targets.Count;
        int success = _targets.Count(t => t.Status == DeployStatus.Success);
        int failed = _targets.Count(t => t.Status == DeployStatus.Failed);
        int pending = _targets.Count(t => t.Status == DeployStatus.Pending || t.Status == DeployStatus.Connecting || t.Status == DeployStatus.Copying || t.Status == DeployStatus.Installing || t.Status == DeployStatus.Verifying);

        TotalCountTxt.Text = total.ToString();
        SuccessCountTxt.Text = success.ToString();
        FailedCountTxt.Text = failed.ToString();
        PendingCountTxt.Text = pending.ToString();
    }
}
