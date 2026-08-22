namespace NexMote.Api.Services;

/// <summary>
/// Sunucudaki kurulum paketlerini (Agent ve Teknisyen MSI paketleri) ve sürüm bildirimlerini yöneten katalog servisi.
/// </summary>
public sealed class DownloadCatalog
{
    private readonly IReadOnlyList<DownloadPackage> _packages =
    [
        new(
            "agent",
            "NexMote Agent Setup",
            "Hedef Windows bilgisayarına kurulur. Arka planda servis ve kullanıcı oturumunda tepsi simgesi olarak çalışır.",
            "NexMote-Agent-Setup.msi",
            "Çok Dilli (Multi-Language)",
            true),
        new(
            "technician",
            "NexMote Technician Console",
            "Teknisyen bilgisayarına kurulur. nexmote:// protokolü ile canlı oturumları ve cihaz listesini açar.",
            "NexMote-Technician-Setup.msi",
            "Çok Dilli (Multi-Language)",
            true),
        new(
            "cleanup",
            "NexMote Tam Kaldırıcı & Derin Temizleyici (MSI)",
            "Cihazda bulunan tüm NexMote Ajanı, Teknisyen Konsolu, Windows Servisi, Kayıt Defteri anahtarları ve artık dosyalarını derinlemesine tamamen kaldırır ve temizler.",
            "NexMote-Cleanup-Setup.msi",
            "Çok Dilli (Multi-Language)",
            true)
    ];

    /// <summary>
    /// Kurulum dosyalarının aranacağı dizin yolu.
    /// </summary>
    public string DownloadsPath { get; } = GetDownloadsDirectory();

    /// <summary>
    /// Hassas/ozel kurulum paketlerinin (orn. gomulu kimlik bilgili sessiz kurulum araclari) bulundugu,
    /// wwwroot DISINDA kalan ve statik dosya sunumuyla asla servis edilmeyen dizin. Sadece admin token'i
    /// dogrulanmis /api/silent-installer endpoint'i uzerinden erisilebilir.
    /// </summary>
    public string PrivatePath { get; } = GetPrivateDirectory();

    private static string GetPrivateDirectory()
    {
        var appBasePrivate = Path.Combine(AppContext.BaseDirectory, "private");
        if (Directory.Exists(appBasePrivate))
        {
            return appBasePrivate;
        }

        return Path.Combine(FindRepositoryRoot(), "artifacts", "private-installers");
    }

    /// <summary>
    /// PrivatePath icindeki dosyayi dondurur (yol geleneksellemesine karsi sadece dosya adi kullanilir).
    /// </summary>
    public DownloadFile? GetPrivateFile(string fileName)
    {
        var safeFileName = Path.GetFileName(fileName);
        var path = Path.Combine(PrivatePath, safeFileName);
        return File.Exists(path)
            ? new DownloadFile(path, safeFileName, GetContentType(safeFileName))
            : null;
    }

    /// <summary>
    /// Ortama göre uygun downloads dizinini tespit eder (appBase, wwwroot/downloads veya repo downloads).
    /// </summary>
    private static string GetDownloadsDirectory()
    {
        var appBase = Path.Combine(AppContext.BaseDirectory, "downloads");
        if (Directory.Exists(appBase) && Directory.GetFiles(appBase, "*.msi").Length > 0)
        {
            return appBase;
        }

        var wwwrootDownloads = Path.Combine(AppContext.BaseDirectory, "wwwroot", "downloads");
        if (Directory.Exists(wwwrootDownloads) && Directory.GetFiles(wwwrootDownloads, "*.msi").Length > 0)
        {
            return wwwrootDownloads;
        }

        return Path.Combine(FindRepositoryRoot(), "downloads");
    }

    /// <summary>
    /// İndirilebilir kurulum paketlerinin listesini, dosya varlığını ve boyutlarını döner.
    /// </summary>
    public IReadOnlyCollection<DownloadPackageInfo> List()
    {
        Directory.CreateDirectory(DownloadsPath);
        var versions = GetVersionInfo();

        return _packages
            .Select(package =>
            {
                var path = Path.Combine(DownloadsPath, package.FileName);
                var exists = File.Exists(path);
                var sizeBytes = exists ? new FileInfo(path).Length : 0;
                var version = package.Id switch
                {
                    "technician" => versions.Technician.Version,
                    _ => versions.Agent.Version
                };
                return new DownloadPackageInfo(
                    package.Id,
                    package.Name,
                    package.Description,
                    package.FileName,
                    $"/downloads/{package.FileName}",
                    package.Language,
                    package.RequiresAdmin,
                    exists,
                    sizeBytes,
                    version);
            })
            .ToArray();
    }

    /// <summary>
    /// versions.json dosyasından en son Agent ve Teknisyen sürüm ve sürüm notlarını okur.
    /// </summary>
    public VersionManifest GetVersionInfo()
    {
        var path = Path.Combine(DownloadsPath, "versions.json");
        if (File.Exists(path))
        {
            try
            {
                var manifest = System.Text.Json.JsonSerializer.Deserialize<VersionManifest>(
                    File.ReadAllText(path),
                    new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (manifest is not null)
                {
                    return manifest;
                }
            }
            catch (System.Text.Json.JsonException)
            {
                // JSON okunamadıysa alttaki varsayılan fallback'e düş
            }
        }

        var fallback = new PackageVersionInfo("0.0.0", "Sürüm bilgisi bulunamadı.");
        return new VersionManifest(fallback, fallback);
    }

    /// <summary>
    /// Belirtilen dosya adına ait indirilebilir dosya nesnesini döner.
    /// </summary>
    public DownloadFile? GetFile(string fileName)
    {
        var package = _packages.FirstOrDefault(item =>
            string.Equals(item.FileName, fileName, StringComparison.OrdinalIgnoreCase));
        if (package is null)
        {
            return null;
        }

        var path = Path.Combine(DownloadsPath, package.FileName);
        return File.Exists(path)
            ? new DownloadFile(path, package.FileName, GetContentType(package.FileName))
            : null;
    }

    /// <summary>
    /// Dosya uzantısına göre MIME Content-Type değerini belirler.
    /// </summary>
    private static string GetContentType(string fileName)
    {
        return Path.GetExtension(fileName).ToLowerInvariant() switch
        {
            ".msi" => "application/x-msi",
            ".zip" => "application/zip",
            ".exe" => "application/vnd.microsoft.portable-executable",
            _ => "application/octet-stream"
        };
    }

    /// <summary>
    /// Geliştirme ortamında proje kök dizinini bulur.
    /// </summary>
    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "NexMote.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        current = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "NexMote.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        return Directory.GetCurrentDirectory();
    }
}

/// <summary>İndirme paketi tanımı.</summary>
public sealed record DownloadPackage(
    string Id,
    string Name,
    string Description,
    string FileName,
    string Language,
    bool RequiresAdmin);

/// <summary>İstemciye sunulan paket durum bilgisi.</summary>
public sealed record DownloadPackageInfo(
    string Id,
    string Name,
    string Description,
    string FileName,
    string Url,
    string Language,
    bool RequiresAdmin,
    bool Exists,
    long SizeBytes,
    string Version);

/// <summary>İndirilen dosya disk yolu ve MIME tipi.</summary>
public sealed record DownloadFile(string Path, string FileName, string ContentType);

/// <summary>Tekil paket sürüm bilgisi ve sürüm notları.</summary>
public sealed record PackageVersionInfo(string Version, string ReleaseNotes);

/// <summary>Agent ve Teknisyen sürümlerini içeren manifest.</summary>
public sealed record VersionManifest(PackageVersionInfo Agent, PackageVersionInfo Technician);
