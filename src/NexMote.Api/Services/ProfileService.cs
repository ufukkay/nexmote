using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using NexMote.Api.Data;
using NexMote.Shared.Contracts;

namespace NexMote.Api.Services;

/// <summary>
/// Hiyerarşik kurumsal profil ağacı (Şirket > Departman > Lokasyon), politika miras alma ve ezme
/// (inheritance & override), cihaz atama ve koruma şifresi doğrulama servisi.
/// </summary>
public sealed class ProfileService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly IPasswordHasher<ProfileEntity> _passwordHasher;
    private readonly UserAuthService _activity;

    public ProfileService(
        IDbContextFactory<AppDbContext> dbFactory,
        IPasswordHasher<ProfileEntity> passwordHasher,
        UserAuthService activity)
    {
        _dbFactory = dbFactory;
        _passwordHasher = passwordHasher;
        _activity = activity;
    }

    // ----------------------------------------------------------------- Profil Yönetimi

    public IReadOnlyList<ProfileTreeNode> GetTree()
    {
        using var db = _dbFactory.CreateDbContext();
        var profiles = db.Profiles.AsNoTracking().OrderBy(p => p.Name).ToList();
        var devices = db.Devices.AsNoTracking().Select(d => new { d.Id, d.ProfileId, d.GroupId }).ToList();

        var nodes = new List<ProfileTreeNode>();
        var companyNameLookup = profiles
            .Where(p => p.Type == ProfileTypes.Company || p.ParentProfileId == null)
            .ToDictionary(p => p.Id, p => p.Name);

        foreach (var p in profiles)
        {
            var deviceCount = devices.Count(d => d.ProfileId == p.Id);
            var subProfileCount = profiles.Count(sub => sub.ParentProfileId == p.Id);

            string? companyName = null;
            var current = p;
            var visited = new HashSet<Guid>();
            while (current != null && visited.Add(current.Id))
            {
                if (current.Type == ProfileTypes.Company || current.ParentProfileId == null)
                {
                    companyName = current.Name;
                    break;
                }
                current = current.ParentProfileId.HasValue
                    ? profiles.FirstOrDefault(parent => parent.Id == current.ParentProfileId.Value)
                    : null;
            }

            nodes.Add(new ProfileTreeNode(
                p.Id,
                p.Name,
                p.ParentProfileId,
                p.Type,
                p.PolicyVersion,
                deviceCount,
                subProfileCount,
                companyName,
                p.UpdatedAt));
        }

        return nodes;
    }

    public ProfileDetailResponse? Get(Guid id)
    {
        using var db = _dbFactory.CreateDbContext();
        var profile = db.Profiles.AsNoTracking().FirstOrDefault(p => p.Id == id);
        if (profile == null)
        {
            return null;
        }

        var ownPolicy = DeserializePolicy(profile.PolicyConfigJson, profile.Id, profile.Name, profile.ParentProfileId, profile.PolicyVersion);
        var effectivePolicy = ResolveEffectivePolicy(db, profile);
        var deviceCount = db.Devices.AsNoTracking().Count(d => d.ProfileId == id);

        return new ProfileDetailResponse(
            profile.Id,
            profile.Name,
            profile.ParentProfileId,
            profile.Type,
            profile.PolicyVersion,
            profile.EnrollmentKey,
            ownPolicy,
            effectivePolicy,
            deviceCount,
            profile.CreatedAt,
            profile.UpdatedAt);
    }

    public ProfileDetailResponse? Create(ProfileUpsertRequest request, Guid actingUserId)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return null;
        }

        using var db = _dbFactory.CreateDbContext();

        if (request.ParentProfileId.HasValue && !db.Profiles.Any(p => p.Id == request.ParentProfileId.Value))
        {
            return null;
        }

        var profileId = Guid.NewGuid();
        var policy = request.Policy ?? new PolicyDocument();
        policy.ProfileId = profileId;
        policy.ProfileName = request.Name.Trim();
        policy.ParentProfileId = request.ParentProfileId;
        policy.Version = 1;

        if (!string.IsNullOrWhiteSpace(request.NewProtectionPassword))
        {
            var dummyEntity = new ProfileEntity { Id = profileId };
            policy.Protection = policy.Protection with
            {
                ProtectionPasswordHash = _passwordHasher.HashPassword(dummyEntity, request.NewProtectionPassword)
            };
        }

        var entity = new ProfileEntity
        {
            Id = profileId,
            Name = request.Name.Trim(),
            ParentProfileId = request.ParentProfileId,
            Type = string.IsNullOrWhiteSpace(request.Type) ? ProfileTypes.Company : request.Type.Trim(),
            PolicyVersion = 1,
            PolicyConfigJson = JsonSerializer.Serialize(policy, JsonOptions),
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        db.Profiles.Add(entity);
        db.SaveChanges();

        _activity.LogActivity(actingUserId, "profile.create", "Profile", entity.Id.ToString(), entity.Name, null, success: true);

        return Get(entity.Id);
    }

    public ProfileDetailResponse? Update(Guid id, ProfileUpsertRequest request, Guid actingUserId)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return null;
        }

        using var db = _dbFactory.CreateDbContext();
        var entity = db.Profiles.FirstOrDefault(p => p.Id == id);
        if (entity == null)
        {
            return null;
        }

        if (request.ParentProfileId.HasValue && request.ParentProfileId.Value == id)
        {
            return null; // Kendini üst profil yapamaz
        }

        var oldJson = entity.PolicyConfigJson;
        var policy = request.Policy ?? new PolicyDocument();
        entity.PolicyVersion++;
        policy.Version = entity.PolicyVersion;
        policy.ProfileId = id;
        policy.ProfileName = request.Name.Trim();
        policy.ParentProfileId = request.ParentProfileId;

        if (!string.IsNullOrWhiteSpace(request.NewProtectionPassword))
        {
            policy.Protection = policy.Protection with
            {
                ProtectionPasswordHash = _passwordHasher.HashPassword(entity, request.NewProtectionPassword)
            };
        }
        else
        {
            // Eski şifre hash'ini koru
            var existingPolicy = DeserializePolicy(oldJson, id, entity.Name, entity.ParentProfileId, entity.PolicyVersion);
            if (!string.IsNullOrEmpty(existingPolicy.Protection.ProtectionPasswordHash) && string.IsNullOrEmpty(policy.Protection.ProtectionPasswordHash))
            {
                policy.Protection = policy.Protection with
                {
                    ProtectionPasswordHash = existingPolicy.Protection.ProtectionPasswordHash
                };
            }
        }

        entity.Name = request.Name.Trim();
        entity.ParentProfileId = request.ParentProfileId;
        entity.Type = string.IsNullOrWhiteSpace(request.Type) ? entity.Type : request.Type.Trim();
        entity.PolicyConfigJson = JsonSerializer.Serialize(policy, JsonOptions);
        entity.UpdatedAt = DateTimeOffset.UtcNow;

        db.SaveChanges();

        _activity.LogActivity(actingUserId, "profile.update", "Profile", entity.Id.ToString(), entity.Name, null, success: true);

        return Get(entity.Id);
    }

    public bool Delete(Guid id, Guid actingUserId, out string? errorMessage)
    {
        errorMessage = null;
        using var db = _dbFactory.CreateDbContext();
        var profile = db.Profiles.FirstOrDefault(p => p.Id == id);
        if (profile == null)
        {
            errorMessage = "Profil bulunamadı.";
            return false;
        }

        var hasChildren = db.Profiles.Any(p => p.ParentProfileId == id);
        if (hasChildren)
        {
            errorMessage = "Bu profile bağlı alt profiller var. Önce alt profilleri silmeli veya başka bir profile taşımalısınız.";
            return false;
        }

        var hasDevices = db.Devices.Any(d => d.ProfileId == id);
        if (hasDevices)
        {
            errorMessage = "Bu profile bağlı kayıtlı cihazlar var. Önce cihazları başka bir profile taşımalısınız.";
            return false;
        }

        db.Profiles.Remove(profile);
        db.SaveChanges();

        _activity.LogActivity(actingUserId, "profile.delete", "Profile", profile.Id.ToString(), profile.Name, null, success: true);
        return true;
    }

    public ProfileDetailResponse? Clone(Guid id, string newName, Guid actingUserId)
    {
        var source = Get(id);
        if (source == null) return null;

        var request = new ProfileUpsertRequest(
            string.IsNullOrWhiteSpace(newName) ? $"{source.Name} (Kopya)" : newName.Trim(),
            source.ParentProfileId,
            source.Type,
            source.OwnPolicy);

        return Create(request, actingUserId);
    }

    // ----------------------------------------------------------------- Cihaz ve Politika Atama

    public bool AssignDevice(Guid deviceId, Guid? profileId, Guid actingUserId)
    {
        using var db = _dbFactory.CreateDbContext();
        var device = db.Devices.FirstOrDefault(d => d.Id == deviceId);
        if (device == null) return false;

        if (profileId.HasValue && !db.Profiles.Any(p => p.Id == profileId.Value))
        {
            return false;
        }

        var oldProfileId = device.ProfileId;
        device.ProfileId = profileId;
        db.SaveChanges();

        _activity.LogActivity(actingUserId, "device.assign_profile", "Device", device.Id.ToString(),
            $"Cihaz profili değiştirildi: {oldProfileId} -> {profileId}", null, success: true);

        return true;
    }

    public int BulkAssignDevices(List<Guid> deviceIds, Guid? targetProfileId, Guid actingUserId)
    {
        if (deviceIds == null || deviceIds.Count == 0) return 0;

        using var db = _dbFactory.CreateDbContext();
        if (targetProfileId.HasValue && !db.Profiles.Any(p => p.Id == targetProfileId.Value))
        {
            return 0;
        }

        var devices = db.Devices.Where(d => deviceIds.Contains(d.Id)).ToList();
        foreach (var d in devices)
        {
            d.ProfileId = targetProfileId;
        }

        var count = db.SaveChanges();
        _activity.LogActivity(actingUserId, "device.bulk_assign_profile", "Device", targetProfileId?.ToString(),
            $"{devices.Count} adet cihaz toplu olarak profile atandı.", null, success: true);

        return devices.Count;
    }

    public bool SetDeviceOverride(Guid deviceId, DevicePolicyOverrideRequest request, Guid actingUserId)
    {
        using var db = _dbFactory.CreateDbContext();
        var device = db.Devices.FirstOrDefault(d => d.Id == deviceId);
        if (device == null) return false;

        device.HasCustomOverride = request.HasCustomOverride;
        device.CustomOverrideJson = request.HasCustomOverride && request.CustomPolicy != null
            ? JsonSerializer.Serialize(request.CustomPolicy, JsonOptions)
            : null;

        db.SaveChanges();

        _activity.LogActivity(actingUserId, "device.policy_override", "Device", device.Id.ToString(),
            $"Cihaz özel politika override durumu: {request.HasCustomOverride}", null, success: true);

        return true;
    }

    // ----------------------------------------------------------------- Efektif Politika Çözümleme (Inheritance Engine)

    public PolicyDocument GetEffectivePolicyForDevice(Guid deviceId)
    {
        using var db = _dbFactory.CreateDbContext();
        var device = db.Devices.AsNoTracking().FirstOrDefault(d => d.Id == deviceId);
        if (device == null)
        {
            return new PolicyDocument();
        }

        PolicyDocument baseEffectivePolicy;

        if (device.ProfileId.HasValue)
        {
            var profile = db.Profiles.AsNoTracking().FirstOrDefault(p => p.Id == device.ProfileId.Value);
            baseEffectivePolicy = profile != null ? ResolveEffectivePolicy(db, profile) : new PolicyDocument();
        }
        else if (device.GroupId.HasValue)
        {
            // Geriye dönük uyumluluk: eski DeviceGroups tablosu ile eşleşme
            baseEffectivePolicy = new PolicyDocument();
        }
        else
        {
            baseEffectivePolicy = new PolicyDocument();
        }

        if (device.HasCustomOverride && !string.IsNullOrWhiteSpace(device.CustomOverrideJson))
        {
            try
            {
                var customPolicy = JsonSerializer.Deserialize<PolicyDocument>(device.CustomOverrideJson, JsonOptions);
                if (customPolicy != null)
                {
                    return baseEffectivePolicy.MergeWithChild(customPolicy, device.Id, device.DeviceName, baseEffectivePolicy.Version + 1);
                }
            }
            catch
            {
            }
        }

        return baseEffectivePolicy;
    }

    public static PolicyDocument ResolveEffectivePolicy(AppDbContext db, ProfileEntity targetProfile)
    {
        var hierarchy = new List<ProfileEntity>();
        var current = targetProfile;
        var visited = new HashSet<Guid>();

        while (current != null && visited.Add(current.Id))
        {
            hierarchy.Add(current);
            if (!current.ParentProfileId.HasValue) break;

            current = db.Profiles.AsNoTracking().FirstOrDefault(p => p.Id == current.ParentProfileId.Value);
        }

        // Kökten hedefe doğru (Root -> Child1 -> Target) sırayla uygula
        hierarchy.Reverse();

        PolicyDocument accumulated = PolicyDocument.CreateDefaultRoot();

        foreach (var p in hierarchy)
        {
            var policyDoc = DeserializePolicy(p.PolicyConfigJson, p.Id, p.Name, p.ParentProfileId, p.PolicyVersion);
            accumulated = accumulated.MergeWithChild(policyDoc, p.Id, p.Name, p.PolicyVersion);
        }

        return accumulated;
    }

    // ----------------------------------------------------------------- Koruma Şifresi Doğrulama

    public bool VerifyProtectionPassword(Guid deviceId, string agentToken, string action, string password)
    {
        if (string.IsNullOrWhiteSpace(password))
        {
            return false;
        }

        using var db = _dbFactory.CreateDbContext();
        var device = db.Devices.AsNoTracking().FirstOrDefault(d => d.Id == deviceId);
        if (device == null || !string.Equals(device.AgentToken, agentToken, StringComparison.Ordinal))
        {
            return false;
        }

        var effectivePolicy = GetEffectivePolicyForDevice(deviceId);
        if (effectivePolicy.Protection.AgentProtection != true || string.IsNullOrEmpty(effectivePolicy.Protection.ProtectionPasswordHash))
        {
            return true; // Şifre koruması aktif değilse serbest geçiş
        }

        var dummyEntity = new ProfileEntity { Id = deviceId };
        var result = _passwordHasher.VerifyHashedPassword(dummyEntity, effectivePolicy.Protection.ProtectionPasswordHash, password);
        var ok = result != PasswordVerificationResult.Failed;

        db.ActivityLogs.Add(new ActivityLogEntity
        {
            Id = Guid.NewGuid(),
            Action = ok ? "agent.protection_unlocked" : "agent.protection_failed",
            TargetType = "Device",
            TargetId = deviceId.ToString(),
            DetailsJson = JsonSerializer.Serialize(new { Action = action, Success = ok }),
            Success = ok,
            CreatedAt = DateTimeOffset.UtcNow
        });
        db.SaveChanges();

        return ok;
    }

    // ----------------------------------------------------------------- Ajan Teyit (ACK)

    public bool AcknowledgePolicy(Guid deviceId, string agentToken, int appliedVersion)
    {
        using var db = _dbFactory.CreateDbContext();
        var device = db.Devices.FirstOrDefault(d => d.Id == deviceId);
        if (device == null || !string.Equals(device.AgentToken, agentToken, StringComparison.Ordinal))
        {
            return false;
        }

        device.AppliedPolicyVersion = appliedVersion;
        device.LastPolicySyncedAt = DateTimeOffset.UtcNow;
        db.SaveChanges();

        return true;
    }

    private static PolicyDocument DeserializePolicy(string? json, Guid id, string name, Guid? parentId, int version)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new PolicyDocument
            {
                ProfileId = id,
                ProfileName = name,
                ParentProfileId = parentId,
                Version = version
            };
        }

        try
        {
            var doc = JsonSerializer.Deserialize<PolicyDocument>(json, JsonOptions);
            if (doc != null)
            {
                doc.ProfileId = id;
                doc.ProfileName = name;
                doc.ParentProfileId = parentId;
                doc.Version = version;
                return doc;
            }
        }
        catch
        {
        }

        return new PolicyDocument
        {
            ProfileId = id,
            ProfileName = name,
            ParentProfileId = parentId,
            Version = version
        };
    }
}
