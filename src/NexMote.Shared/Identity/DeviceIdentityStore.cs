using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace NexMote.Shared.Identity;

/// <summary>
/// Cihazın sunucu kaydı sonrasında yerel diskte saklanan kimlik ve güvenlik token'ı.
/// </summary>
/// <param name="DeviceId">Veritabanındaki benzersiz cihaz kimliği.</param>
/// <param name="AgentToken">Heartbeat ve yetkilendirmelerde kullanılan 32-byte güvenlik token'ı.</param>
public sealed record DeviceIdentity(Guid DeviceId, string AgentToken);

/// <summary>
/// Cihaza ait kimlik bilgilerini (DeviceId ve AgentToken) %ProgramData%\NexMote\Agent\ dizininde
/// Windows DPAPI (DataProtectionScope.LocalMachine) ile şifrelenmiş olarak saklayan, okuyan ve sıfırlayan depo sınıfı.
///
/// Hem SYSTEM yetkili Windows Servisi (NexMote.Agent.Windows) hem de kullanıcı oturumundaki Tray süreci
/// (NexMote.Agent.Tray) tarafından ORTAK kullanılır: LocalMachine kapsamlı DPAPI şifrelemesi, aynı fiziksel
/// makinede çalışan her iki sürecin de aynı blob'u çözebilmesini garanti eder. İki süreç kendi ayrı
/// dosya formatını kullanırsa (biri şifreli .dat, diğeri düz .json) her biri farklı bir DeviceId/AgentToken
/// ile kayıt olur ve sunucuda aynı makine için iki ayrı cihaz kaydı (split-brain) oluşur.
///
/// Neden DPAPI?
/// Plaintext JSON dosyası; yerel fiziksel erişimi olan ya da ayrıcalıklı süreç enjeksiyonu yapan bir saldırganın
/// AgentToken'ı çalıp farklı bir makineden sunucuya heartbeat göndermesine imkân tanır.
/// LocalMachine kapsamında DPAPI şifrelemesi bu riski ortadan kaldırır:
/// şifreli blob yalnızca aynı fiziksel Windows kurulumu tarafından çözülebilir.
/// </summary>
public sealed class DeviceIdentityStore
{
    // Şifreli binary blob uzantısı (.dat); plaintext JSON ile karışmasın
    private readonly string _identityPath;
    private readonly string _legacyPath;

    // DPAPI entropy: aynı makine üzerindeki farklı uygulamaların birbirinin blobunu çözmesini engeller
    private static readonly byte[] DpapiEntropy =
        Encoding.UTF8.GetBytes("NexMote.Agent.Identity.v1");

    public DeviceIdentityStore()
    {
        var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        var directory = Path.Combine(programData, "NexMote", "Agent");
        Directory.CreateDirectory(directory);
        _identityPath = Path.Combine(directory, "identity.dat");
        _legacyPath = Path.Combine(directory, "identity.json");
    }

    /// <summary>
    /// Diskten şifreli kimlik blobunu okur, DPAPI ile çözer ve DeviceIdentity nesnesine dönüştürür.
    /// Dosya yoksa veya çözme başarısız olursa null döner (re-enrollment tetiklenir).
    /// </summary>
    public DeviceIdentity? Load()
    {
        if (File.Exists(_identityPath))
        {
            return LoadEncrypted();
        }

        // Eski plaintext JSON varsa geç: şifreli formata dönüştür ve plaintext'i sil
        if (File.Exists(_legacyPath))
        {
            return MigrateLegacy();
        }

        return null;
    }

    /// <summary>
    /// DeviceIdentity nesnesini JSON olarak serileştirir ve DPAPI (LocalMachine) ile şifreleyerek diske yazar.
    /// </summary>
    public void Save(DeviceIdentity identity)
    {
        var json = JsonSerializer.Serialize(identity);
        var plainBytes = Encoding.UTF8.GetBytes(json);

        // DPAPI şifrelemesi yalnızca Windows'ta mevcuttur; test ortamı için güvenli fallback
        byte[] cipherBytes;
        try
        {
            cipherBytes = ProtectedData.Protect(plainBytes, DpapiEntropy, DataProtectionScope.LocalMachine);
        }
        catch (PlatformNotSupportedException)
        {
            // Unit test veya non-Windows ortamı: düz yaz (production'da bu path'e girilmez)
            cipherBytes = plainBytes;
        }

        File.WriteAllBytes(_identityPath, cipherBytes);
    }

    /// <summary>
    /// Kimlik dosyasını siler (Sunucu adresi değiştiğinde veya yeniden kayıt gerektiğinde çağrılır).
    /// </summary>
    public void Delete()
    {
        if (File.Exists(_identityPath))
        {
            File.Delete(_identityPath);
        }

        // Eski plaintext varsa onu da temizle
        if (File.Exists(_legacyPath))
        {
            File.Delete(_legacyPath);
        }
    }

    // ── Özel yardımcılar ──────────────────────────────────────────────────────

    private DeviceIdentity? LoadEncrypted()
    {
        try
        {
            var cipherBytes = File.ReadAllBytes(_identityPath);
            byte[] plainBytes;

            try
            {
                plainBytes = ProtectedData.Unprotect(cipherBytes, DpapiEntropy, DataProtectionScope.LocalMachine);
            }
            catch (CryptographicException)
            {
                // Blob bozuk ya da farklı makineden kopyalanmış — sil, yeniden kayıt yapılacak
                File.Delete(_identityPath);
                return null;
            }
            catch (PlatformNotSupportedException)
            {
                // Test ortamı — düz JSON olarak oku
                plainBytes = cipherBytes;
            }

            var json = Encoding.UTF8.GetString(plainBytes);
            return JsonSerializer.Deserialize<DeviceIdentity>(json);
        }
        catch
        {
            return null;
        }
    }

    private DeviceIdentity? MigrateLegacy()
    {
        try
        {
            var json = File.ReadAllText(_legacyPath);
            var identity = JsonSerializer.Deserialize<DeviceIdentity>(json);

            if (identity is not null)
            {
                // Şifreli formata dönüştür
                Save(identity);
                // Plaintext eski dosyayı güvenli sil
                File.Delete(_legacyPath);
            }

            return identity;
        }
        catch
        {
            return null;
        }
    }
}
