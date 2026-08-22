using System.Windows;
using NexMote.Shared.Network;

namespace NexMote.TechnicianApp;

/// <summary>
/// Sunucu adresi ve yönetici giriş bilgilerini (e-posta ve parola) doğrulayan ve saklayan giriş penceresi.
/// Sessiz otomatik giriş başarısız olduğunda veya parola değiştiğinde gösterilir.
/// </summary>
public partial class ServerLoginWindow : Window
{
    /// <summary>Doğrulanan sunucu URL'i.</summary>
    public string ServerUrl { get; private set; } = "https://nexmote.com";

    /// <summary>Giriş yapılan kullanıcı e-postası.</summary>
    public string Email { get; private set; } = "admin@nexmote.com";

    /// <summary>Kullanıcı parolası.</summary>
    public string Password { get; private set; } = string.Empty;

    /// <summary>Bilgilerin kaydedilip kaydedilmeyeceği seçimi.</summary>
    public bool RememberMe => RememberMeCheckBox.IsChecked == true;

    public ServerLoginWindow(string defaultServerUrl, string defaultEmail = "admin@nexmote.com")
    {
        InitializeComponent();
        
        ServerUrlBox.Text = string.IsNullOrWhiteSpace(defaultServerUrl) ? "https://nexmote.com" : defaultServerUrl;
        EmailBox.Text = string.IsNullOrWhiteSpace(defaultEmail) ? "admin@nexmote.com" : defaultEmail;
        PasswordBox.Password = string.Empty;
    }

    /// <summary>
    /// Giriş yap butonuna tıklandığında form girdilerini doğrular ve formu kapatır.
    /// </summary>
    private void Connect_Click(object sender, RoutedEventArgs e)
    {
        ErrorText.Text = string.Empty;

        var url = NexMoteHttp.NormalizeUrl(ServerUrlBox.Text);
        var email = EmailBox.Text.Trim();
        var password = PasswordBox.Password.Trim();

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

        ServerUrl = url;
        Email = email;
        Password = password;
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
