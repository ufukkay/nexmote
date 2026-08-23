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

    private static DeviceGroupDetail ToDetail(DeviceGroupEntity g) => new(g.Id, g.Name, g.ParentGroupId, g.DefaultSecurityProfileId, g.CreatedAt);
}
