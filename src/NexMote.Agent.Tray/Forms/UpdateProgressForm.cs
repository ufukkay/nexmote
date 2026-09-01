namespace NexMote.Agent.Tray;

/// <summary>
/// Ajan güncellemesi indirilirken aşamaları ve yüzdelik ilerleme çubuğunu görsel olarak gösteren modern diyalog formu.
/// </summary>
internal sealed class UpdateProgressForm : Form
{
    private readonly string _downloadUrl;
    private readonly string _targetVersion;
    private readonly ProgressBar _progressBar;
    private readonly Label _lblStage;
    private readonly Label _lblDetails;
    private readonly Label _lblPercent;
    private readonly Button _btnAction;
    private readonly CancellationTokenSource _cts = new();
    private bool _isFinished;

    public UpdateProgressForm(string downloadUrl, string targetVersion)
    {
        _downloadUrl = downloadUrl;
        _targetVersion = targetVersion;

        Text = "NexMote Ajan Güncellemesi";
        ClientSize = new Size(520, 230);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MaximizeBox = false;
        MinimizeBox = false;
        BackColor = Color.FromArgb(0xF8, 0xFA, 0xFC);
        Font = new Font("Segoe UI", 9F);
        Icon = IconHelper.GetAppIcon();

        // 1. Üst Başlık Paneli
        var header = new Panel { Left = 0, Top = 0, Width = 520, Height = 64, BackColor = Color.White };
        header.Paint += (_, e) =>
        {
            using var pen = new Pen(Color.FromArgb(0xE2, 0xE8, 0xF0));
            e.Graphics.DrawLine(pen, 0, 63, 520, 63);
        };

        var icon = new Label
        {
            Text = "🚀",
            Font = new Font("Segoe UI", 18F),
            Left = 18,
            Top = 14,
            Width = 36,
            Height = 36,
            TextAlign = ContentAlignment.MiddleCenter
        };

        var title = new Label
        {
            Text = $"NexMote Ajanı Güncelleniyor (v{_targetVersion})",
            Font = new Font("Segoe UI", 11.5F, FontStyle.Bold),
            ForeColor = Color.FromArgb(0x0F, 0x17, 0x2A),
            Left = 62,
            Top = 12,
            Width = 440,
            Height = 24
        };

        _lblStage = new Label
        {
            Text = "Güncelleme paketi hazırlanıyor...",
            Font = new Font("Segoe UI", 9F),
            ForeColor = Color.FromArgb(0x64, 0x74, 0x8B),
            Left = 62,
            Top = 36,
            Width = 440,
            Height = 20
        };

        header.Controls.AddRange(new Control[] { icon, title, _lblStage });

        // 2. İlerleme Çubuğu ve Detaylar
        _progressBar = new ProgressBar
        {
            Left = 24,
            Top = 86,
            Width = 472,
            Height = 22,
            Minimum = 0,
            Maximum = 100,
            Value = 0,
            Style = ProgressBarStyle.Continuous
        };

        _lblDetails = new Label
        {
            Text = "Sunucuya bağlanılıyor...",
            Font = new Font("Segoe UI", 9F, FontStyle.Bold),
            ForeColor = Color.FromArgb(0x33, 0x41, 0x55),
            Left = 24,
            Top = 116,
            Width = 370,
            Height = 20
        };

        _lblPercent = new Label
        {
            Text = "%0",
            Font = new Font("Segoe UI", 10.5F, FontStyle.Bold),
            ForeColor = Color.FromArgb(0x25, 0x63, 0xEB),
            Left = 400,
            Top = 114,
            Width = 96,
            Height = 22,
            TextAlign = ContentAlignment.MiddleRight
        };

        // 3. Alt Eylem Butonu
        _btnAction = new Button
        {
            Text = "İptal",
            Font = new Font("Segoe UI", 9F, FontStyle.Bold),
            ForeColor = Color.FromArgb(0x47, 0x55, 0x69),
            BackColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Left = 396,
            Top = 168,
            Width = 100,
            Height = 36,
            Cursor = Cursors.Hand
        };
        _btnAction.FlatAppearance.BorderColor = Color.FromArgb(0xE2, 0xE8, 0xF0);
        _btnAction.Click += (_, _) =>
        {
            if (_isFinished)
            {
                Close();
            }
            else
            {
                _cts.Cancel();
                _btnAction.Enabled = false;
                _lblStage.Text = "İptal ediliyor...";
            }
        };

        Controls.AddRange(new Control[] { header, _progressBar, _lblDetails, _lblPercent, _btnAction });

        Shown += async (_, _) => await StartDownloadAsync();
    }

    private async Task StartDownloadAsync()
    {
        var progress = new Progress<(long CurrentVal, long TotalVal, string Stage)>(info =>
        {
            if (IsDisposed) return;

            _lblStage.Text = info.Stage;

            if (info.TotalVal > 0)
            {
                var pct = (int)Math.Clamp((info.CurrentVal * 100.0) / info.TotalVal, 0, 100);
                _progressBar.Value = pct;
                _lblPercent.Text = $"%{pct}";
                _lblDetails.Text = info.Stage;
            }
        });

        try
        {
            await RemoteScreenStreamer.PerformSelfUpdateAsync(_downloadUrl, progress, _cts.Token);
            _isFinished = true;

            _progressBar.Value = 100;
            _lblPercent.Text = "%100";
            _lblPercent.ForeColor = Color.FromArgb(0x10, 0xB9, 0x81);
            _lblStage.Text = "✓ Güncelleme ve kurulum başarıyla tamamlandı!";
            _lblDetails.Text = "Yeni sürüm devrede. Ajan yenileniyor...";
            _btnAction.Text = "Kapat";
            _btnAction.ForeColor = Color.White;
            _btnAction.BackColor = Color.FromArgb(0x10, 0xB9, 0x81);
            _btnAction.FlatAppearance.BorderSize = 0;

            await Task.Delay(2500);
            if (!IsDisposed)
            {
                Close();
            }
        }
        catch (OperationCanceledException)
        {
            _isFinished = true;
            _lblStage.Text = "Güncelleme kullanıcı tarafından iptal edildi.";
            _lblDetails.Text = "İndirme ve kurulum durduruldu.";
            _btnAction.Text = "Kapat";
            _btnAction.Enabled = true;
        }
        catch (Exception ex)
        {
            _isFinished = true;
            _lblStage.Text = "Güncelleme sırasında hata oluştu.";
            _lblDetails.Text = ex.Message;
            _btnAction.Text = "Kapat";
            _btnAction.Enabled = true;
        }
    }
}
