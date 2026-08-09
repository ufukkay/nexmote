namespace NexMote.Api.Services;

public sealed class DownloadCatalog
{
    private readonly IReadOnlyList<DownloadPackage> _packages =
    [
        new(
            "agent-tr",
            "NexMote Agent",
            "Hedef Windows cihaza kurulur. Turkce kurulum.",
            "nexmote-agent-win-x64.msi",
            "Turkce",
            true),
        new(
            "technician-tr",
            "NexMote Technician App",
            "Teknisyen bilgisayarina kurulur. Turkce kurulum.",
            "nexmote-technician-win-x64.msi",
            "Turkce",
            true),
        new(
            "agent-en",
            "NexMote Agent",
            "Installs on the target Windows device. English installer.",
            "nexmote-agent-win-x64-en.msi",
            "English",
            true),
        new(
            "technician-en",
            "NexMote Technician App",
            "Installs on the technician computer. English installer.",
            "nexmote-technician-win-x64-en.msi",
            "English",
            true)
    ];

    public string DownloadsPath { get; } = Path.Combine(FindRepositoryRoot(), "downloads");

    public IReadOnlyCollection<DownloadPackageInfo> List()
    {
        Directory.CreateDirectory(DownloadsPath);

        return _packages
            .Select(package =>
            {
                var path = Path.Combine(DownloadsPath, package.FileName);
                var exists = File.Exists(path);
                var sizeBytes = exists ? new FileInfo(path).Length : 0;
                return new DownloadPackageInfo(
                    package.Id,
                    package.Name,
                    package.Description,
                    package.FileName,
                    $"/downloads/{package.FileName}",
                    package.Language,
                    package.RequiresAdmin,
                    exists,
                    sizeBytes);
            })
            .ToArray();
    }

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

public sealed record DownloadPackage(
    string Id,
    string Name,
    string Description,
    string FileName,
    string Language,
    bool RequiresAdmin);

public sealed record DownloadPackageInfo(
    string Id,
    string Name,
    string Description,
    string FileName,
    string Url,
    string Language,
    bool RequiresAdmin,
    bool Exists,
    long SizeBytes);

public sealed record DownloadFile(string Path, string FileName, string ContentType);
