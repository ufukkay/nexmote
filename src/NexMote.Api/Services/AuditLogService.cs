using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NexMote.Api.Auth;
using NexMote.Api.Data;

namespace NexMote.Api.Services;

/// <summary>
/// Yüksek riskli işlemler (uzak oturum, komut yürütme, ayar değişikliği, cihaz silme)
/// ve güvenlik eylemleri için merkezi aktör ve denetim log servisi.
/// </summary>
public sealed class AuditLogService
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly ILogger<AuditLogService> _logger;

    public AuditLogService(IDbContextFactory<AppDbContext> dbFactory, ILogger<AuditLogService> logger)
    {
        _dbFactory = dbFactory;
        _logger = logger;
    }

    /// <summary>
    /// HttpContext üzerinden kullanıcı ve istek bağlamını (UserId, Email, IP, CorrelationId)
    /// otomatik çıkararak bir denetim kaydı oluşturur.
    /// </summary>
    public void Log(
        HttpContext? http,
        string action,
        string? targetType = null,
        string? targetId = null,
        object? details = null,
        bool success = true)
    {
        Guid? userId = null;
        string? userEmail = null;
        string? ip = null;
        string? correlationId = null;

        if (http != null)
        {
            var user = http.User;
            var sub = user.FindFirstValue(ClaimTypes.NameIdentifier);
            if (Guid.TryParse(sub, out var parsedGuid))
            {
                userId = parsedGuid;
            }

            userEmail = user.FindFirstValue(ClaimTypes.Email) ?? user.FindFirstValue("email");
            ip = http.Connection.RemoteIpAddress?.ToString();
            correlationId = http.GetCorrelationId();
        }

        Log(userId, userEmail, action, targetType, targetId, details, ip, correlationId, success);
    }

    /// <summary>
    /// Açıkça belirtilen aktör parametreleriyle veritabanına denetim kaydı ekler.
    /// </summary>
    public void Log(
        Guid? userId,
        string? userEmail,
        string action,
        string? targetType = null,
        string? targetId = null,
        object? details = null,
        string? ip = null,
        string? correlationId = null,
        bool success = true)
    {
        try
        {
            using var db = _dbFactory.CreateDbContext();

            string? detailsJson = null;
            if (details is not null)
            {
                detailsJson = details is string s ? s : JsonSerializer.Serialize(details);
            }

            var entity = new ActivityLogEntity
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                UserEmailSnapshot = userEmail?.Length > 256 ? userEmail[..256] : userEmail,
                Action = action?.Length > 64 ? action[..64] : (action ?? string.Empty),
                TargetType = targetType?.Length > 32 ? targetType[..32] : targetType,
                TargetId = targetId?.Length > 128 ? targetId[..128] : targetId,
                DetailsJson = detailsJson,
                IpAddress = ip?.Length > 64 ? ip[..64] : ip,
                CorrelationId = correlationId?.Length > 64 ? correlationId[..64] : correlationId,
                Success = success,
                CreatedAt = DateTimeOffset.UtcNow
            };

            db.ActivityLogs.Add(entity);
            db.SaveChanges();

            _logger.LogInformation(
                "Audit: [{Action}] Actor: {UserEmail} Target: {TargetType}/{TargetId} Success: {Success} CorrId: {CorrelationId}",
                action, userEmail ?? "anonymous", targetType ?? "-", targetId ?? "-", success, correlationId ?? "-");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to record audit activity log: {Action}", action);
        }
    }
}
