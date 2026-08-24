using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NexMote.Api.Data;
using NexMote.Shared.Contracts;

namespace NexMote.Api.Services;

/// <summary>
/// Cihazları iç içe (şirket &gt; departman &gt; ...) gruplar halinde organize etmek için kullanılan servis.
/// Bir gruba atanan varsayılan güvenlik profili, o gruptaki (kendi profili olmayan) cihazlar tarafından
/// miras alınır — çözümleme mantığı <see cref="SecurityProfileService"/> içindedir.
/// </summary>
public sealed class DeviceGroupService
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly UserAuthService _activity;

    public DeviceGroupService(IDbContextFactory<AppDbContext> dbFactory, UserAuthService activity)
    {
        _dbFactory = dbFactory;
        _activity = activity;
    }

    public IReadOnlyList<DeviceGroupDetail> List()
    {
        using var db = _dbFactory.CreateDbContext();
        return db.DeviceGroups.AsNoTracking().OrderBy(g => g.Name).Select(ToDetail).ToList();
    }

    public DeviceGroupDetail? Create(DeviceGroupRequest request, Guid actingUserId)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return null;
        }

        using var db = _dbFactory.CreateDbContext();
        if (request.ParentGroupId.HasValue && !db.DeviceGroups.Any(g => g.Id == request.ParentGroupId.Value))
        {
            return null;
        }
        if (request.DefaultSecurityProfileId.HasValue && !db.SecurityProfiles.Any(p => p.Id == request.DefaultSecurityProfileId.Value))
        {
            return null;
        }

        var group = new DeviceGroupEntity
        {
            Id = Guid.NewGuid(),
            Name = request.Name.Trim(),
            ParentGroupId = request.ParentGroupId,
            DefaultSecurityProfileId = request.DefaultSecurityProfileId,
            EnrollmentKey = GenerateUniqueKey(db),
            CreatedAt = DateTimeOffset.UtcNow
        };
        db.DeviceGroups.Add(group);
        db.SaveChanges();

        _activity.LogActivity(actingUserId, "device_group.create", "DeviceGroup", group.Id.ToString(), group.Name, null, success: true);
        return ToDetail(group);
    }

    public DeviceGroupDetail? Update(Guid id, DeviceGroupRequest request, Guid actingUserId)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return null;
        }

        using var db = _dbFactory.CreateDbContext();
        var group = db.DeviceGroups.FirstOrDefault(g => g.Id == id);
        if (group is null)
        {
            return null;
        }

        if (request.ParentGroupId.HasValue)
        {
            if (!db.DeviceGroups.Any(g => g.Id == request.ParentGroupId.Value))
            {
                return null;
            }
            if (CreatesCycle(db, id, request.ParentGroupId.Value))
            {
                return null;
            }
        }
        if (request.DefaultSecurityProfileId.HasValue && !db.SecurityProfiles.Any(p => p.Id == request.DefaultSecurityProfileId.Value))
        {
            return null;
        }

        group.Name = request.Name.Trim();
        group.ParentGroupId = request.ParentGroupId;
        group.DefaultSecurityProfileId = request.DefaultSecurityProfileId;
        db.SaveChanges();

        _activity.LogActivity(actingUserId, "device_group.update", "DeviceGroup", group.Id.ToString(), group.Name, null, success: true);
        return ToDetail(group);
    }

    public (bool Success, string? Error) Delete(Guid id, Guid actingUserId)
    {
        using var db = _dbFactory.CreateDbContext();
        var group = db.DeviceGroups.FirstOrDefault(g => g.Id == id);
        if (group is null)
        {
            return (false, "Grup bulunamadı.");
        }

        if (db.DeviceGroups.Any(g => g.ParentGroupId == id))
        {
            return (false, "Bu grubun alt grupları var — önce onları silin veya taşıyın.");
        }

        // Bu gruptaki cihazları gruptan çıkar (silinmez, sadece serbest kalır).
        foreach (var device in db.Devices.Where(d => d.GroupId == id))
        {
            device.GroupId = null;
        }

        db.DeviceGroups.Remove(group);
        db.SaveChanges();

        _activity.LogActivity(actingUserId, "device_group.delete", "DeviceGroup", id.ToString(), group.Name, null, success: true);
        return (true, null);
    }

    /// <summary>Sunulan kayıt anahtarıyla eşleşen grubu bulur (varsa) — <c>/api/agents/enroll</c> tarafından kullanılır.</summary>
    public DeviceGroupEntity? FindByEnrollmentKey(string? enrollmentKey)
    {
        if (string.IsNullOrEmpty(enrollmentKey))
        {
            return null;
        }

        using var db = _dbFactory.CreateDbContext();
        return db.DeviceGroups.AsNoTracking().FirstOrDefault(g => g.EnrollmentKey == enrollmentKey);
    }

    /// <summary>Bir grubun kurulum anahtarını yeniden üretir — eski anahtarla üretilmiş yeni kurulumlar artık bu gruba düşmez, zaten kayıtlı cihazlar etkilenmez.</summary>
    public DeviceGroupDetail? RegenerateEnrollmentKey(Guid id, Guid actingUserId)
    {
        using var db = _dbFactory.CreateDbContext();
        var group = db.DeviceGroups.FirstOrDefault(g => g.Id == id);
        if (group is null)
        {
            return null;
        }

        group.EnrollmentKey = GenerateUniqueKey(db);
        db.SaveChanges();

        _activity.LogActivity(actingUserId, "device_group.regenerate_key", "DeviceGroup", group.Id.ToString(), group.Name, null, success: true);
        return ToDetail(group);
    }

    /// <summary>
    /// Bu grubun kurulum anahtarını ve sunucu adresini, Tray'in okuduğu appsettings.json şemasıyla
    /// (bkz. NexMote.Agent.Tray/Program.cs AgentSettings.SaveConfigToPath) birebir aynı şekilde yazan,
    /// kurulumdan hemen sonra çalıştırılacak bir PowerShell provizyon script'i üretir.
    /// </summary>
    public (string Script, string GroupName)? BuildProvisionScript(Guid id, string serverUrl)
    {
        var body = ResolveProvisionBody(id, serverUrl);
        if (body is null)
        {
            return null;
        }

        var (group, escapedJson, escapedName) = body.Value;

        var script = $@"# NexMote Agent Provizyon Script'i — Grup: {group.Name}
# NexMote-Agent-Setup.msi kurulduktan HEMEN SONRA bu script'i çalıştırın.
# Bu script, ajanı '{escapedName}' grubuna (ve varsa o grubun güvenlik profiline) otomatik olarak bağlar.
$ErrorActionPreference = 'Stop'
{ProvisionBodyScript(escapedJson, escapedName)}";

        return (script, group.Name);
    }

    /// <summary>
    /// Seçilen gruba özel TEK bir kurulum script'i üretir: MSI'ı sunucudan indirir, sessizce (/qn) kurar,
    /// ardından ajanı doğrudan bu gruba (ve varsa grubun güvenlik profiline) bağlar. Web konsolunda "Hedef
    /// grup" seçilip indirme yapıldığında artık ayrı bir provizyon adımına gerek kalmaz — indirilen paket
    /// zaten seçilen profile uygun kurulur.
    /// </summary>
    public (string Script, string GroupName)? BuildInstallScript(Guid id, string serverUrl)
    {
        var body = ResolveProvisionBody(id, serverUrl);
        if (body is null)
        {
            return null;
        }

        var (group, escapedJson, escapedName) = body.Value;
        var msiUrl = $"{serverUrl.TrimEnd('/')}/downloads/NexMote-Agent-Setup.msi";

        var script = $@"# NexMote Agent Tek Adımda Kurulum Script'i — Grup: {group.Name}
# Bu script'i (Yönetici olarak) çalıştırın: MSI'ı sunucudan indirir, sessizce kurar ve ajanı otomatik olarak
# '{escapedName}' grubuna (ve varsa o grubun güvenlik profiline) bağlar — ayrı bir kurulum sonrası adım gerekmez.
$ErrorActionPreference = 'Stop'
$msiPath = Join-Path $env:TEMP 'NexMote-Agent-Setup.msi'
Invoke-WebRequest -Uri '{msiUrl}' -OutFile $msiPath -UseBasicParsing
Start-Process -FilePath 'msiexec.exe' -ArgumentList ""/i `""$msiPath`"" /qn /norestart"" -Wait
{ProvisionBodyScript(escapedJson, escapedName)}
Remove-Item -LiteralPath $msiPath -Force -ErrorAction SilentlyContinue
";

        return (script, group.Name);
    }

    private (DeviceGroupEntity Group, string EscapedJson, string EscapedName)? ResolveProvisionBody(Guid id, string serverUrl)
    {
        using var db = _dbFactory.CreateDbContext();
        var group = db.DeviceGroups.AsNoTracking().FirstOrDefault(g => g.Id == id);
        if (group is null || string.IsNullOrEmpty(group.EnrollmentKey))
        {
            return null;
        }

        var config = new
        {
            Agent = new
            {
                ServerUrl = serverUrl,
                EnrollmentKey = group.EnrollmentKey,
                AgentVersion = "0.1.0",
                LocationCode = "OFFICE",
                HeartbeatSeconds = 20
            },
            Logging = new
            {
                LogLevel = new { Default = "Information", MicrosoftHostingLifetime = "Information" }
            }
        };
        var configJson = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
        // Logging.LogLevel."Microsoft.Hosting.Lifetime" anahtarı C# tanımlayıcısı olarak yazılamadığından, üretilen JSON'da düzeltilir.
        configJson = configJson.Replace("\"MicrosoftHostingLifetime\"", "\"Microsoft.Hosting.Lifetime\"");
        var escapedJson = configJson.Replace("'", "''");
        var escapedName = group.Name.Replace("'", "''");

        return (group, escapedJson, escapedName);
    }

    /// <summary>
    /// Hem provizyon-only hem tek-adım kurulum script'lerinde ortak olan, appsettings.json'ı yazıp
    /// servisi/tepsi uygulamasını yeniden başlatan gövde.
    /// </summary>
    private static string ProvisionBodyScript(string escapedJson, string escapedName) => $@"$agentDir = Join-Path $env:ProgramData 'NexMote\Agent'
New-Item -ItemType Directory -Force -Path $agentDir | Out-Null
$configJson = '{escapedJson}'
Set-Content -LiteralPath (Join-Path $agentDir 'appsettings.json') -Value $configJson -Encoding UTF8
Restart-Service 'NexMote Agent' -ErrorAction SilentlyContinue
Get-Process -Name 'NexMote.Agent.Tray' -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
$trayExe = Join-Path ${{env:ProgramFiles}} 'NexMote\Agent\NexMote.Agent.Tray.exe'
if (Test-Path $trayExe) {{ Start-Process -FilePath $trayExe -ArgumentList '--tray' }}
Write-Host 'NexMote Agent ''{escapedName}'' grubuna bağlandı.'";

    private static string GenerateUniqueKey(AppDbContext db)
    {
        string key;
        do
        {
            key = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        } while (db.DeviceGroups.Any(g => g.EnrollmentKey == key));
        return key;
    }

    public bool AssignDeviceToGroup(Guid deviceId, Guid? groupId, Guid actingUserId)
    {
        using var db = _dbFactory.CreateDbContext();
        var device = db.Devices.FirstOrDefault(d => d.Id == deviceId);
        if (device is null)
        {
            return false;
        }

        if (groupId.HasValue && !db.DeviceGroups.Any(g => g.Id == groupId.Value))
        {
            return false;
        }

        device.GroupId = groupId;
        db.SaveChanges();

        _activity.LogActivity(actingUserId, "device_group.assign", "Device", deviceId.ToString(), groupId?.ToString() ?? "none", null, success: true);
        return true;
    }

    /// <summary>Bir grubun üst grubunu <paramref name="proposedParentId"/> yapmanın bir çevrim oluşturup oluşturmayacağını kontrol eder.</summary>
    private static bool CreatesCycle(AppDbContext db, Guid groupId, Guid proposedParentId)
    {
        var current = (Guid?)proposedParentId;
        var visited = new HashSet<Guid>();
        while (current is { } cid && visited.Add(cid))
        {
            if (cid == groupId)
            {
                return true;
            }
            current = db.DeviceGroups.AsNoTracking().Where(g => g.Id == cid).Select(g => g.ParentGroupId).FirstOrDefault();
        }
        return false;
    }

    private static DeviceGroupDetail ToDetail(DeviceGroupEntity g) => new(g.Id, g.Name, g.ParentGroupId, g.DefaultSecurityProfileId, g.EnrollmentKey, g.CreatedAt);
}
