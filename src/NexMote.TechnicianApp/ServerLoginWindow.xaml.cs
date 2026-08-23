using System.Net.Http;
using System.Net.Http.Json;
using System.Windows;
using NexMote.Shared.Contracts;
using NexMote.Shared.Network;

namespace NexMote.TechnicianApp;

/// <summary>
/// Sunucu adresi ve kullanıcı giriş bilgilerini (e-posta, parola, gerekirse MFA kodu) alıp
/// sunucuda gerçek bir oturum token'ı üreten iki adımlı giriş penceresi. Parola hiçbir zaman
/// bu pencerenin dışına (diske) taşınmaz — sadece üretilen oturum token'ı çağırana döner.
/// </summary>
public partial class ServerLoginWindow : Window
{
    private readonly HttpClient _http = NexMoteHttp.CreateClient();
    private string? _mfaChallengeToken;

    /// <summary>Doğrulanan sunucu URL'i.</summary>
    public string ServerUrl { get; private set; } = "https://nexmote.com";

    /// <summary>Giriş yapılan kullanıcı e-postası.</summary>
    public string Email { get; private set; } = string.Empty;

    /// <summary>Başarılı girişte üretilen opak oturum token'ı.</summary>
    public string Token { get; private set; } = string.Empty;

    /// <summary>Bilgilerin kaydedilip kaydedilmeyeceği seçimi.</summary>
    public bool RememberMe => RememberMeCheckBox.IsChecked == true;

    public ServerLoginWindow(string defaultServerUrl, string defaultEmail = "")
    {
        InitializeComponent();

        ServerUrlBox.Text = string.IsNullOrWhiteSpace(defaultServerUrl) ? "https://nexmote.com" : defaultServerUrl;
        EmailBox.Text = defaultEmail;
        PasswordBox.Password = string.Empty;
    }

    private async void Primary_Click(object sender, RoutedEventArgs e)
    {
        if (MfaPanel.Visibility == Visibility.Visible)
        {
            await SubmitMfaAsync();
        }
        else
        {
            await SubmitCredentialsAsync();
        }
    }

    private async Task SubmitCredentialsAsync()
    {
        ErrorText.Text = string.Empty;

        var url = NexMoteHttp.NormalizeUrl(ServerUrlBox.Text);
        var email = EmailBox.Text.Trim();
        var password = PasswordBox.Password;

        if (string.IsNullOrEmpty(email))
        {
            ErrorText.Text = "Lütfen e-posta adresinizi girin.";
            return;
        }

        if (string.IsNullOrEmpty(password))
        {
            ErrorText.Text = "Lütfen parolanızı girin.";
            return;
        }

        SetBusy(true);
        try
        {
            var response = await _http.PostAsync(
                $"{url.TrimEnd('/')}/api/auth/login?rememberMe={RememberMe}",
                JsonContent.Create(new AdminLoginRequest(email, password)));

            if (!response.IsSuccessStatusCode)
            {
                ErrorText.Text = "E-posta veya parola hatalı.";
                return;
            }

            var body = await response.Content.ReadFromJsonAsync<LoginResponse>();
            if (body is null)
            {
                ErrorText.Text = "Sunucudan geçersiz yanıt alındı.";
                return;
            }

            ServerUrl = url;
            Email = email;

            if (body.RequiresMfa && !string.IsNullOrWhiteSpace(body.ChallengeToken))
            {
                _mfaChallengeToken = body.ChallengeToken;
                ShowMfaStep();
                return;
            }

            if (string.IsNullOrWhiteSpace(body.Token))
            {
                ErrorText.Text = "Sunucudan geçersiz yanıt alındı.";
                return;
            }

            Token = body.Token;
            DialogResult = true;
            Close();
        }
        catch
        {
            ErrorText.Text = "Sunucuya bağlanılamadı.";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task SubmitMfaAsync()
    {
        ErrorText.Text = string.Empty;

        var code = MfaCodeBox.Text.Trim();
        if (string.IsNullOrEmpty(code) || _mfaChallengeToken is null)
        {
            ErrorText.Text = "Lütfen doğrulama kodunu girin.";
            return;
        }

        SetBusy(true);
        try
        {
            var response = await _http.PostAsync(
                $"{ServerUrl.TrimEnd('/')}/api/auth/mfa/verify?rememberMe={RememberMe}",
                JsonContent.Create(new MfaVerifyRequest(_mfaChallengeToken, code)));

            if (!response.IsSuccessStatusCode)
            {
                ErrorText.Text = "Kod hatalı veya süresi dolmuş.";
                return;
            }

            var body = await response.Content.ReadFromJsonAsync<LoginResponse>();
            if (body is null || string.IsNullOrWhiteSpace(body.Token))
            {
                ErrorText.Text = "Sunucudan geçersiz yanıt alındı.";
                return;
            }

            Token = body.Token;
            DialogResult = true;
            Close();
        }
        catch
        {
            ErrorText.Text = "Sunucuya bağlanılamadı.";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void ShowMfaStep()
    {
        CredentialsPanel.Visibility = Visibility.Collapsed;
        MfaPanel.Visibility = Visibility.Visible;
        SubtitleText.Text = "Doğrulama kodu gerekiyor";
        PrimaryButton.Content = "Doğrula ve Giriş Yap";
        MfaCodeBox.Focus();
    }

    private void BackToCredentials_Click(object sender, RoutedEventArgs e)
    {
        _mfaChallengeToken = null;
        MfaCodeBox.Text = string.Empty;
        ErrorText.Text = string.Empty;
        MfaPanel.Visibility = Visibility.Collapsed;
        CredentialsPanel.Visibility = Visibility.Visible;
        SubtitleText.Text = "Teknisyen oturumu ve sunucu bağlantısı";
        PrimaryButton.Content = "Giriş yap";
    }

    private void SetBusy(bool busy)
    {
        PrimaryButton.IsEnabled = !busy;
        IsEnabled = true;
        Cursor = busy ? System.Windows.Input.Cursors.Wait : System.Windows.Input.Cursors.Arrow;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
