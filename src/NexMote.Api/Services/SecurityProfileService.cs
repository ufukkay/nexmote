using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using NexMote.Api.Data;
using NexMote.Shared.Contracts;

namespace NexMote.Api.Services;

/// <summary>
/// Kurumsal ajan güvenlik profillerinin (branding, kısıtlı tray menüsü, Durum Paneli/Çıkış/Kaldırma
/// şifre korumaları) yönetimi, cihazlara atanması ve ajan tarafından sorgulanan doğrulama işlemleri.
/// Şifreler sunucuda hash'lenir; ajan hiçbir zaman şifre veya hash almaz, sadece doğrulama sonucunu (bool) alır.
/// </summary>
public sealed class SecurityProfileService
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly IPasswordHasher<SecurityProfileEntity> _passwordHasher;
    private readonly DeviceRegistry _devices;
    private readonly UserAuthService _activity;

    public SecurityProfileService(
        IDbContextFactory<AppDbContext> dbFactory,
        IPasswordHasher<SecurityProfileEntity> passwordHasher,
        DeviceRegistry devices,
        UserAuthService activity)
    {
        _dbFactory = dbFactory;
        _passwordHasher = passwordHasher;
        _devices = devices;
        _activity = activity;
    }

    // ----------------------------------------------------------------- Admin yönetimi

    public IReadOnlyList<SecurityProfileDetail> List()
    {
        using var db = _dbFactory.CreateDbContext();
        return db.SecurityProfiles.AsNoTracking().OrderBy(p => p.Name).Select(ToDetail).ToList();
    }

    public SecurityProfileDetail? Get(Guid id)
    {
        using var db = _dbFactory.CreateDbContext();
        var profile = db.SecurityProfiles.AsNoTracking().FirstOrDefault(p => p.Id == id);
        return profile is null ? null : ToDetail(profile);
    }

    public SecurityProfileDetail? Create(SecurityProfileRequest request, Guid actingUserId)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return null;
        }

        if (!PasswordsSatisfied(request, isCreate: true))
        {
            return null;
        }

        using var db = _dbFactory.CreateDbContext();
        var profile = new SecurityProfileEntity
        {
            Id = Guid.NewGuid(),
            CreatedAt = DateTimeOffset.UtcNow
        };
        ApplyRequest(profile, request);
        db.SecurityProfiles.Add(profile);
        db.SaveChanges();

        _activity.LogActivity(actingUserId, "security_profile.create", "SecurityProfile", profile.Id.ToString(), profile.Name, null, success: true);
        return ToDetail(profile);
    }

    public SecurityProfileDetail? Update(Guid id, SecurityProfileRequest request, Guid actingUserId)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return null;
        }

        if (!PasswordsSatisfied(request, isCreate: false))
        {
            return null;
        }

        using var db = _dbFactory.CreateDbContext();
        var profile = db.SecurityProfiles.FirstOrDefault(p => p.Id == id);
        if (profile is null)
        {
            return null;
        }

        ApplyRequest(profile, request);
        profile.UpdatedAt = DateTimeOffset.UtcNow;
        db.SaveChanges();

        _activity.LogActivity(actingUserId, "security_profile.update", "SecurityProfile", profile.Id.ToString(), profile.Name, null, success: true);
        return ToDetail(profile);
    }

    public bool Delete(Guid id, Guid actingUserId)
    {
        using var db = _dbFactory.CreateDbContext();
        var profile = db.SecurityProfiles.FirstOrDefault(p => p.Id == id);
        if (profile is null)
        {
            return false;
        }

        // Bu profili kullanan cihazları önce serbest bırak — kısıtlama olmadan çalışmaya devam etsinler.
        foreach (var device in db.Devices.Where(d => d.SecurityProfileId == id))
        {
            device.SecurityProfileId = null;
        }

        db.SecurityProfiles.Remove(profile);
        db.SaveChanges();

        _activity.LogActivity(actingUserId, "security_profile.delete", "SecurityProfile", id.ToString(), profile.Name, null, success: true);
        return true;
    }

    public bool AssignToDevice(Guid deviceId, Guid? securityProfileId, Guid actingUserId)
    {
        using var db = _dbFactory.CreateDbContext();
        var device = db.Devices.FirstOrDefault(d => d.Id == deviceId);
        if (device is null)
        {
            return false;
        }

        if (securityProfileId.HasValue && !db.SecurityProfiles.Any(p => p.Id == securityProfileId.Value))
        {
            return false;
        }

        device.SecurityProfileId = securityProfileId;
        db.SaveChanges();

        _activity.LogActivity(actingUserId, "security_profile.assign", "Device", deviceId.ToString(), securityProfileId?.ToString() ?? "none", null, success: true);
        return true;
    }

    // ----------------------------------------------------------------- Ajan tarafı (AgentToken ile, insan auth'u değil)

    public AgentSecurityProfileResponse? GetAgentProfile(Guid deviceId, string agentToken)
    {
        if (!_devices.ValidateAgent(deviceId, agentToken))
        {
            return null;
        }

        using var db = _dbFactory.CreateDbContext();
        var device = db.Devices.AsNoTracking().FirstOrDefault(d => d.Id == deviceId);
        if (device?.SecurityProfileId is null)
        {
            // Profil atanmamış — kısıtlama yok, varsayılan davranış.
            return new AgentSecurityProfileResponse(null, null, false, false, false, false);
        }

        var profile = db.SecurityProfiles.AsNoTracking().FirstOrDefault(p => p.Id == device.SecurityProfileId.Value);
        if (profile is null)
        {
            return new AgentSecurityProfileResponse(null, null, false, false, false, false);
        }

        return new AgentSecurityProfileResponse(
            profile.AgentDisplayName, profile.IconBase64, profile.RestrictTrayMenu,
            profile.RequireDashboardPassword, profile.RequireExitPassword, profile.RequireUninstallPassword);
    }

    public bool VerifyPassword(Guid deviceId, string agentToken, string action, string password)
    {
        if (!_devices.ValidateAgent(deviceId, agentToken))
        {
            return false;
        }

        using var db = _dbFactory.CreateDbContext();
        var device = db.Devices.AsNoTracking().FirstOrDefault(d => d.Id == deviceId);
        var profile = device?.SecurityProfileId is null
            ? null
            : db.SecurityProfiles.FirstOrDefault(p => p.Id == device.SecurityProfileId.Value);

        var hash = action switch
        {
            "dashboard" => profile?.DashboardPasswordHash,
            "exit" => profile?.ExitPasswordHash,
            "uninstall" => profile?.UninstallPasswordHash,
            _ => null
        };

        var ok = !string.IsNullOrEmpty(hash) &&
                  _passwordHasher.VerifyHashedPassword(profile!, hash, password) != PasswordVerificationResult.Failed;

        db.ActivityLogs.Add(new ActivityLogEntity
        {
            Id = Guid.NewGuid(),
            UserId = null,
            UserEmailSnapshot = null,
            Action = $"security.{action}_verify",
            TargetType = "Device",
            TargetId = deviceId.ToString(),
            Success = ok,
            CreatedAt = DateTimeOffset.UtcNow
        });
        db.SaveChanges();

        return ok;
    }

    // ----------------------------------------------------------------- Yardımcılar

    private bool PasswordsSatisfied(SecurityProfileRequest request, bool isCreate)
    {
        if (request.RequireDashboardPassword && isCreate && string.IsNullOrWhiteSpace(request.DashboardPassword)) return false;
        if (request.RequireExitPassword && isCreate && string.IsNullOrWhiteSpace(request.ExitPassword)) return false;
        if (request.RequireUninstallPassword && isCreate && string.IsNullOrWhiteSpace(request.UninstallPassword)) return false;
        return true;
    }

    private void ApplyRequest(SecurityProfileEntity profile, SecurityProfileRequest request)
    {
        profile.Name = request.Name.Trim();
        profile.AgentDisplayName = string.IsNullOrWhiteSpace(request.AgentDisplayName) ? null : request.AgentDisplayName.Trim();
        profile.IconBase64 = string.IsNullOrWhiteSpace(request.IconBase64) ? profile.IconBase64 : request.IconBase64;
        profile.RestrictTrayMenu = request.RestrictTrayMenu;

        profile.RequireDashboardPassword = request.RequireDashboardPassword;
        if (!request.RequireDashboardPassword)
        {
            profile.DashboardPasswordHash = null;
        }
        else if (!string.IsNullOrWhiteSpace(request.DashboardPassword))
        {
            profile.DashboardPasswordHash = _passwordHasher.HashPassword(profile, request.DashboardPassword);
        }

        profile.RequireExitPassword = request.RequireExitPassword;
        if (!request.RequireExitPassword)
        {
            profile.ExitPasswordHash = null;
        }
        else if (!string.IsNullOrWhiteSpace(request.ExitPassword))
        {
            profile.ExitPasswordHash = _passwordHasher.HashPassword(profile, request.ExitPassword);
        }

        profile.RequireUninstallPassword = request.RequireUninstallPassword;
        if (!request.RequireUninstallPassword)
        {
            profile.UninstallPasswordHash = null;
        }
        else if (!string.IsNullOrWhiteSpace(request.UninstallPassword))
        {
            profile.UninstallPasswordHash = _passwordHasher.HashPassword(profile, request.UninstallPassword);
        }
    }

    private static SecurityProfileDetail ToDetail(SecurityProfileEntity p) => new(
        p.Id, p.Name, p.AgentDisplayName, p.IconBase64, p.RestrictTrayMenu,
        p.RequireDashboardPassword, p.RequireExitPassword, p.RequireUninstallPassword,
        p.CreatedAt, p.UpdatedAt);
}
