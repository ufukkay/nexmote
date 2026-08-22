namespace NexMote.Agent.Windows;

/// <summary>
/// Windows Agent servisinin appsettings.json dosyasından okunan çalışma zamanı ayarları.
/// </summary>
public sealed class AgentOptions
{
    /// <summary>NexMote sunucu ana erişim adresi (Örn: https://nexmote.com).</summary>
    public string ServerUrl { get; set; } = "https://nexmote.com";

    /// <summary>Sunucuya ilk kayıt için kullanılan ortak anahtar.</summary>
    public string EnrollmentKey { get; set; } = "dev-enrollment-key";

    /// <summary>Cihazın ait olduğu lokasyon veya departman kodu.</summary>
    public string? LocationCode { get; set; }

    /// <summary>Sunucuya heartbeat gönderme sıklığı (Saniye).</summary>
    public int HeartbeatSeconds { get; set; } = 20;
}

// DeviceIdentity / DeviceIdentityStore artık NexMote.Shared.Identity içinde tanımlı: Windows Servisi VE
// Tray süreci aynı şifreli identity.dat dosyasını okuyup yazmalı, aksi halde aynı makine için sunucuda
// iki ayrı cihaz kaydı (split-brain) oluşur. Bkz. NexMote.Shared/Identity/DeviceIdentityStore.cs
