using System.Windows;

namespace NexMote.TechnicianApp;

public partial class ServerLoginWindow : Window
{
    public string ServerUrl { get; private set; } = string.Empty;
    public string TechnicianKey { get; private set; } = string.Empty;

    public ServerLoginWindow(string defaultServerUrl)
    {
        InitializeComponent();
        ServerUrlBox.Text = defaultServerUrl;
    }

    private void Connect_Click(object sender, RoutedEventArgs e)
    {
        var url = ServerUrlBox.Text.Trim();
        var key = TechnicianKeyBox.Password.Trim();

        if (string.IsNullOrEmpty(url) || !Uri.TryCreate(url, UriKind.Absolute, out _))
        {
            ErrorText.Text = "Geçerli bir sunucu adresi girin (ör. http://192.168.0.104:5080).";
            return;
        }

        if (string.IsNullOrEmpty(key))
        {
            ErrorText.Text = "Teknisyen erişim anahtarı boş olamaz.";
            return;
        }

        ServerUrl = url.TrimEnd('/');
        TechnicianKey = key;
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
