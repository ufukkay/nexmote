using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using MimeKit;
using NexMote.Api.Data;
using NexMote.Shared.Contracts;

namespace NexMote.Api.Services;

/// <summary>
/// SMTP üzerinden e-posta gönderimi (test e-postası, kullanıcı davetleri, uyarı bildirimleri). Sunucu ayarları
/// (<see cref="ServerSettingEntity"/>) içindeki SMTP config'ini okur; şifre Data Protection ile
/// şifreli saklanır, düz metin asla veritabanına yazılmaz (MFA secret şifrelemesiyle aynı desen).
/// </summary>
public sealed class EmailService
{
    private const string SmtpProtectorPurpose = "NexMote.Api.SmtpPassword.v1";

    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly IDataProtector _protector;

    public EmailService(IDbContextFactory<AppDbContext> dbFactory, IDataProtectionProvider dataProtectionProvider)
    {
        _dbFactory = dbFactory;
        _protector = dataProtectionProvider.CreateProtector(SmtpProtectorPurpose);
    }

    public string EncryptPassword(string plainText) => _protector.Protect(plainText);

    public string? DecryptPassword(string? cipherText)
    {
        if (string.IsNullOrWhiteSpace(cipherText)) return null;
        try
        {
            return _protector.Unprotect(cipherText);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>SMTP yapılandırılmışsa ve verilen adrese gönderim başarılıysa true döner.</summary>
    public async Task<(bool Success, string? Error)> SendAsync(string toEmail, string subject, string htmlBody)
    {
        using var db = _dbFactory.CreateDbContext();
        var settings = db.ServerSettings.AsNoTracking().First();

        if (string.IsNullOrWhiteSpace(settings.SmtpHost) || string.IsNullOrWhiteSpace(settings.SmtpFromAddress))
        {
            return (false, "SMTP yapılandırılmamış. Önce Ayarlar > E-posta (SMTP) bölümünden sunucu bilgilerini kaydedin.");
        }

        string? password = null;
        if (!string.IsNullOrWhiteSpace(settings.SmtpUsername))
        {
            if (string.IsNullOrWhiteSpace(settings.SmtpPasswordEncrypted))
            {
                return (false, "SMTP kullanıcı adı girilmiş ancak şifre kaydedilmemiş.");
            }

            password = DecryptPassword(settings.SmtpPasswordEncrypted);
            if (password is null)
            {
                return (false, "SMTP şifresi çözülemedi, lütfen ayarlardan yeniden girin.");
            }
        }

        return await SendInternalAsync(
            toEmail,
            subject,
            htmlBody,
            settings.SmtpHost,
            settings.SmtpPort,
            settings.SmtpUsername,
            password,
            settings.SmtpFromAddress,
            settings.SmtpFromName,
            settings.SmtpSslMode);
    }

    /// <summary>İsteğe bağlı form parametreleri veya kayıtlı ayarları kullanarak test e-postası gönderir.</summary>
    public async Task<(bool Success, string? Error)> SendTestAsync(SmtpTestRequest request)
    {
        using var db = _dbFactory.CreateDbContext();
        var settings = db.ServerSettings.AsNoTracking().First();

        var host = !string.IsNullOrWhiteSpace(request.Host) ? request.Host : settings.SmtpHost;
        var port = request.Port.HasValue && request.Port.Value > 0 ? request.Port.Value : settings.SmtpPort;
        var username = request.Username ?? settings.SmtpUsername;
        var fromAddress = !string.IsNullOrWhiteSpace(request.FromAddress) ? request.FromAddress : settings.SmtpFromAddress;
        var fromName = request.FromName ?? settings.SmtpFromName;
        var sslMode = !string.IsNullOrWhiteSpace(request.SslMode) ? request.SslMode : settings.SmtpSslMode;

        if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(fromAddress))
        {
            return (false, "SMTP sunucu adresi ve gönderen e-posta adresi boş bırakılamaz.");
        }

        string? password = null;
        if (!string.IsNullOrWhiteSpace(username))
        {
            if (!string.IsNullOrWhiteSpace(request.Password))
            {
                password = request.Password;
            }
            else if (!string.IsNullOrWhiteSpace(settings.SmtpPasswordEncrypted))
            {
                password = DecryptPassword(settings.SmtpPasswordEncrypted);
            }

            if (string.IsNullOrEmpty(password))
            {
                return (false, "SMTP kullanıcı adı için şifre girilmemiş.");
            }
        }

        return await SendInternalAsync(
            request.ToEmail,
            "NexMote - Test E-postası",
            @"<div style=""font-family: sans-serif; max-width: 600px; margin: 0 auto; padding: 20px; border: 1px solid #e2e8f0; border-radius: 8px;"">
                <h2 style=""color: #0f172a; margin-top: 0;"">NexMote SMTP Testi Başarılı ✅</h2>
                <p style=""color: #334155; font-size: 15px;"">Bu e-posta, NexMote sunucunuzun SMTP e-posta yapılandırmasının sorunsuz çalıştığını doğrulamak amacıyla gönderilmiştir.</p>
                <div style=""background: #f8fafc; padding: 12px; border-radius: 6px; font-size: 13px; color: #64748b;"">
                    <strong>Tarih:</strong> " + DateTimeOffset.UtcNow.ToString("yyyy-MM-dd HH:mm:ss 'UTC'") + @"<br/>
                    <strong>Sunucu:</strong> " + host + ":" + port + @"<br/>
                    <strong>Gönderen:</strong> " + fromAddress + @"
                </div>
            </div>",
            host,
            port,
            username,
            password,
            fromAddress,
            fromName,
            sslMode);
    }

    private static async Task<(bool Success, string? Error)> SendInternalAsync(
        string toEmail,
        string subject,
        string htmlBody,
        string host,
        int port,
        string? username,
        string? password,
        string fromAddress,
        string? fromName,
        string? sslMode)
    {
        try
        {
            var message = new MimeMessage();
            var senderDisplayName = string.IsNullOrWhiteSpace(fromName) ? fromAddress : fromName;
            message.From.Add(new MailboxAddress(senderDisplayName, fromAddress.Trim()));

            if (MailboxAddress.TryParse(toEmail.Trim(), out var parsedTo))
            {
                message.To.Add(parsedTo);
            }
            else
            {
                message.To.Add(new MailboxAddress(string.Empty, toEmail.Trim()));
            }

            message.Subject = subject;
            message.Body = new TextPart("html") { Text = htmlBody };

            using var client = new SmtpClient();
            // Zaman aşımını 15 saniyeye indir (varsayılan 100 saniyelik askıda kalmayı engeller)
            client.Timeout = 15000;

            // Hostinger, self-signed veya alan adı uyuşmazlığı olan sertifikalarda SSL kopmasını engelle
            client.ServerCertificateValidationCallback = (sender, certificate, chain, sslPolicyErrors) => true;

            var socketOptions = ResolveSocketOptions(sslMode, port);
            await client.ConnectAsync(host.Trim(), port, socketOptions);

            // OAuth2 mekanizmasını temizle (standart şifre kimlik doğrulamasında hatalı SASL tetiklenmesini önler)
            client.AuthenticationMechanisms.Remove("XOAUTH2");

            if (!string.IsNullOrWhiteSpace(username) && !string.IsNullOrEmpty(password))
            {
                await client.AuthenticateAsync(username.Trim(), password);
            }

            await client.SendAsync(message);
            await client.DisconnectAsync(true);
            return (true, null);
        }
        catch (Exception ex)
        {
            var detail = ex.InnerException != null ? $"{ex.Message} ({ex.InnerException.Message})" : ex.Message;
            return (false, $"E-posta gönderilemedi: {detail}");
        }
    }

    private static SecureSocketOptions ResolveSocketOptions(string? sslMode, int port)
    {
        if (string.Equals(sslMode, "Ssl", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(sslMode, "SslOnConnect", StringComparison.OrdinalIgnoreCase))
        {
            return SecureSocketOptions.SslOnConnect;
        }

        if (string.Equals(sslMode, "StartTls", StringComparison.OrdinalIgnoreCase))
        {
            return SecureSocketOptions.StartTls;
        }

        if (string.Equals(sslMode, "StartTlsWhenAvailable", StringComparison.OrdinalIgnoreCase))
        {
            return SecureSocketOptions.StartTlsWhenAvailable;
        }

        if (string.Equals(sslMode, "None", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(sslMode, "PlainText", StringComparison.OrdinalIgnoreCase))
        {
            return SecureSocketOptions.None;
        }

        // Auto seçeneğinde port numarasına göre en güvenli ve uyumlu protokolü belirle
        return port switch
        {
            465 => SecureSocketOptions.SslOnConnect,
            587 => SecureSocketOptions.StartTlsWhenAvailable,
            25 => SecureSocketOptions.StartTlsWhenAvailable,
            _ => SecureSocketOptions.Auto
        };
    }
}
