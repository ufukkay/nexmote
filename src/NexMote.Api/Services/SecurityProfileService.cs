using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using NexMote.Api.Data;
using NexMote.Shared.Contracts;

namespace NexMote.Api.Services;

/// <summary>
/// Kurumsal ajan güvenlik profillerinin (branding, kısıtlı tray menüsü, tek şifreyle korunan Durum
/// Paneli/Çıkış/Kaldırma işlemleri) yönetimi, cihazlara/gruplara atanması ve ajan tarafından sorgulanan
/// doğrulama işlemleri. Şifre sunucuda hash'lenir; ajan hiçbir zaman şifre veya hash almaz, sadece
/// doğrulama sonucunu (bool) alır. Bir cihazın etkin profili, kendi atamasından ya da (kendi ataması
/// yoksa) grup hiyerarşisinden (<see cref="DeviceGroupService"/>) miras alınabilir.
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

        if (request.RequirePassword && string.IsNullOrWhiteSpace(request.Password))
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

        using var db = _dbFactory.CreateDbContext();
        var profile = db.SecurityProfiles.FirstOrDefault(p => p.Id == id);
        if (profile is null)
        {
            return null;
        }

        if (request.RequirePassword && string.IsNullOrWhiteSpace(request.Password) && string.IsNullOrEmpty(profile.PasswordHash))
        {
            // İlk kez açılıyorsa (daha önce hiç şifresi yoktu) bir şifre zorunludur.
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

        // Bu profili kullanan cihazları/grupları önce serbest bırak — kısıtlama olmadan çalışmaya devam etsinler.
        foreach (var device in db.Devices.Where(d => d.SecurityProfileId == id))
        {
            device.SecurityProfileId = null;
        }
        foreach (var group in db.DeviceGroups.Where(g => g.DefaultSecurityProfileId == id))
        {
            group.DefaultSecurityProfileId = null;
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
        var profile = ResolveEffectiveProfile(db, deviceId);
        if (profile is null)
        {
            // Profil yok (ne cihazda ne mirasında) — kısıtlama yok, varsayılan davranış.
            return new AgentSecurityProfileResponse(
                null, null, false, false,
                SecurityProfileConstants.ConsentUnattended, 30, SecurityProfileConstants.ActionDeny,
                false, true, true, true, true);
        }

        return new AgentSecurityProfileResponse(
            profile.AgentDisplayName,
            profile.IconBase64,
            profile.RestrictTrayMenu,
            profile.RequirePassword,
            profile.ConsentMode,
            profile.ConsentTimeoutSeconds,
            profile.ConsentDefaultAction,
            profile.ViewOnlyMode,
            profile.AllowRemoteTerminal,
            profile.AllowClipboard,
            profile.AllowFileTransfer,
            profile.ShowConnectionBanner);
    }

    public bool VerifyPassword(Guid deviceId, string agentToken, string action, string password)
    {
        if (!_devices.ValidateAgent(deviceId, agentToken))
        {
            return false;
        }

        using var db = _dbFactory.CreateDbContext();
        var profile = ResolveEffectiveProfile(db, deviceId);

        var ok = profile is not null && !string.IsNullOrEmpty(profile.PasswordHash) &&
                  _passwordHasher.VerifyHashedPassword(profile, profile.PasswordHash, password) != PasswordVerificationResult.Failed;

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

    /// <summary>
    /// Harici servislerin (örn. SignalingHub) cihaz için geçerli etkin güvenlik profilini sorgulaması için genel metod.
    /// </summary>
    public SecurityProfileEntity? GetEffectiveProfile(Guid deviceId)
    {
        using var db = _dbFactory.CreateDbContext();
        return ResolveEffectiveProfile(db, deviceId);
    }

    /// <summary>
    /// Bir cihazın etkin güvenlik profilini çözer: önce cihazın kendi ataması (varsa, her zaman kazanır),
    /// yoksa cihazın grubundan başlayıp <c>ParentGroupId</c> zincirinde yukarı doğru ilk
    /// <c>DefaultSecurityProfileId</c> dolu olan grup. Hiçbiri yoksa null (kısıtlama yok).
    /// </summary>
    public static SecurityProfileEntity? ResolveEffectiveProfile(AppDbContext db, Guid deviceId)
    {
        var device = db.Devices.AsNoTracking().FirstOrDefault(d => d.Id == deviceId);
        if (device is null)
        {
            return null;
        }

        if (device.SecurityProfileId is { } directId)
        {
            return db.SecurityProfiles.AsNoTracking().FirstOrDefault(p => p.Id == directId);
        }

        var groupId = device.GroupId;
        var visited = new HashSet<Guid>(); // çevrimlere karşı emniyet
        while (groupId is { } gid && visited.Add(gid))
        {
            var group = db.DeviceGroups.AsNoTracking().FirstOrDefault(g => g.Id == gid);
            if (group is null)
            {
                break;
            }

            if (group.DefaultSecurityProfileId is { } profileId)
            {
                var profile = db.SecurityProfiles.AsNoTracking().FirstOrDefault(p => p.Id == profileId);
                if (profile is not null)
                {
                    return profile;
                }
            }

            groupId = group.ParentGroupId;
        }

        return null;
    }

    // ----------------------------------------------------------------- Yardımcılar

    private void ApplyRequest(SecurityProfileEntity profile, SecurityProfileRequest request)
    {
        profile.Name = request.Name.Trim();
        profile.AgentDisplayName = string.IsNullOrWhiteSpace(request.AgentDisplayName) ? null : request.AgentDisplayName.Trim();
        profile.IconBase64 = string.IsNullOrWhiteSpace(request.IconBase64) ? profile.IconBase64 : request.IconBase64;
        profile.RestrictTrayMenu = request.RestrictTrayMenu;

        profile.RequirePassword = request.RequirePassword;
        if (!request.RequirePassword)
        {
            profile.PasswordHash = null;
        }
        else if (!string.IsNullOrWhiteSpace(request.Password))
        {
            profile.PasswordHash = _passwordHasher.HashPassword(profile, request.Password);
        }

        profile.ConsentMode = string.IsNullOrWhiteSpace(request.ConsentMode) ? SecurityProfileConstants.ConsentUnattended : request.ConsentMode.Trim();
        profile.ConsentTimeoutSeconds = request.ConsentTimeoutSeconds > 0 ? request.ConsentTimeoutSeconds : 30;
        profile.ConsentDefaultAction = string.Equals(request.ConsentDefaultAction, SecurityProfileConstants.ActionAllow, StringComparison.OrdinalIgnoreCase)
            ? SecurityProfileConstants.ActionAllow
            : SecurityProfileConstants.ActionDeny;

        profile.ViewOnlyMode = request.ViewOnlyMode;
        profile.AllowRemoteTerminal = request.AllowRemoteTerminal;
        profile.AllowClipboard = request.AllowClipboard;
        profile.AllowFileTransfer = request.AllowFileTransfer;
        profile.ShowConnectionBanner = request.ShowConnectionBanner;
    }

    private static SecurityProfileDetail ToDetail(SecurityProfileEntity p) => new(
        p.Id,
        p.Name,
        p.AgentDisplayName,
        p.IconBase64,
        p.RestrictTrayMenu,
        p.RequirePassword,
        p.ConsentMode,
        p.ConsentTimeoutSeconds,
        p.ConsentDefaultAction,
        p.ViewOnlyMode,
        p.AllowRemoteTerminal,
        p.AllowClipboard,
        p.AllowFileTransfer,
        p.ShowConnectionBanner,
        p.CreatedAt,
        p.UpdatedAt);
}
