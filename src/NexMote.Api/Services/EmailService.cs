using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using MimeKit;
using NexMote.Api.Data;

namespace NexMote.Api.Services;

/// <summary>
/// SMTP üzerinden e-posta gönderimi (test e-postası, kullanıcı davetleri). Sunucu ayarları
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

    /// <summary>SMTP yapılandırılmışsa ve verilen adrese gönderim başarılıysa true döner.</summary>
    public async Task<(bool Success, string? Error)> SendAsync(string toEmail, string subject, string htmlBody)
    {
        using var db = _dbFactory.CreateDbContext();
        var settings = db.ServerSettings.AsNoTracking().First();

        if (string.IsNullOrWhiteSpace(settings.SmtpHost) ||
            string.IsNullOrWhiteSpace(settings.SmtpUsername) ||
            string.IsNullOrWhiteSpace(settings.SmtpPasswordEncrypted) ||
            string.IsNullOrWhiteSpace(settings.SmtpFromAddress))
        {
            return (false, "SMTP yapılandırılmamış. Önce Ayarlar > E-posta (SMTP) bölümünden sunucu bilgilerini kaydedin.");
        }

        string password;
        try
        {
            password = _protector.Unprotect(settings.SmtpPasswordEncrypted);
        }
        catch
        {
            return (false, "SMTP şifresi çözülemedi, lütfen ayarlardan yeniden girin.");
        }

        try
        {
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(
                string.IsNullOrWhiteSpace(settings.SmtpFromName) ? settings.SmtpFromAddress : settings.SmtpFromName,
                settings.SmtpFromAddress));
            message.To.Add(new MailboxAddress(string.Empty, toEmail));
            message.Subject = subject;
            message.Body = new TextPart("html") { Text = htmlBody };

            using var client = new SmtpClient();
            await client.ConnectAsync(settings.SmtpHost, settings.SmtpPort, SecureSocketOptions.Auto);
            await client.AuthenticateAsync(settings.SmtpUsername, password);
            await client.SendAsync(message);
            await client.DisconnectAsync(true);
            return (true, null);
        }
        catch (Exception ex)
        {
            return (false, $"E-posta gönderilemedi: {ex.Message}");
        }
    }
}
