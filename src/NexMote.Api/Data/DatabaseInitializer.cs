using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace NexMote.Api.Data;

/// <summary>
/// NexMote SQLite veritabanı şema yaşam döngüsü ve başlatma yöneticisi.
/// WAL modu, Foreign Key (ilişkisel bütünlük), şema tabloları, indeksler ve kolon geçişlerini
/// (migrations) idempotent ve güvenli şekilde yürütür.
/// </summary>
public static class DatabaseInitializer
{
    public static void Initialize(AppDbContext db, ILogger logger)
    {
        try
        {
            // 1. SQLite Concurrency & Performance PRAGMA ayarları
            db.Database.ExecuteSqlRaw("PRAGMA journal_mode = WAL;");
            db.Database.ExecuteSqlRaw("PRAGMA synchronous = NORMAL;");
            db.Database.ExecuteSqlRaw("PRAGMA foreign_keys = ON;");
            db.Database.ExecuteSqlRaw("PRAGMA busy_timeout = 5000;");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "SQLite PRAGMA ayarları uygulanırken uyarı oluştu.");
        }

        // 2. Temel şemanın oluşturulması (Sıfır kurulumlar için)
        db.Database.EnsureCreated();

        // 3. Tabloların idempotent kontrolü ve oluşturulması (Mevcut eski DB'ler için)
        using var transaction = db.Database.BeginTransaction();
        EnsureTablesAndIndexes(db, logger);

        // 4. Kolon güncellemeleri (Incremental Schema Evolution)
        EnsureColumns(db, logger);
        db.Database.ExecuteSqlRaw("CREATE INDEX IF NOT EXISTS IX_ActivityLogs_CorrelationId ON ActivityLogs (CorrelationId);");
        db.Database.ExecuteSqlRaw("CREATE INDEX IF NOT EXISTS IX_RemoteSessions_OwnerUserId ON RemoteSessions (OwnerUserId);");
        transaction.Commit();
    }

    private static void EnsureTablesAndIndexes(AppDbContext db, ILogger logger)
    {
        const string sql = @"
            -- 1. Silinen Cihazlar
            CREATE TABLE IF NOT EXISTS ""DeletedDevices"" (
                ""Id"" TEXT NOT NULL CONSTRAINT ""PK_DeletedDevices"" PRIMARY KEY,
                ""DeviceName"" TEXT NOT NULL,
                ""DomainName"" TEXT NOT NULL,
                ""DeletedAt"" TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS ""IX_DeletedDevices_DeviceName_DomainName"" ON ""DeletedDevices"" (""DeviceName"", ""DomainName"");

            -- 2. Çoklu Kullanıcı (Admin/Teknisyen)
            CREATE TABLE IF NOT EXISTS ""Users"" (
                ""Id"" TEXT NOT NULL CONSTRAINT ""PK_Users"" PRIMARY KEY,
                ""Email"" TEXT NOT NULL,
                ""DisplayName"" TEXT NOT NULL,
                ""PasswordHash"" TEXT NOT NULL,
                ""Role"" TEXT NOT NULL,
                ""IsActive"" INTEGER NOT NULL,
                ""MfaEnabled"" INTEGER NOT NULL,
                ""MfaSecretEncrypted"" TEXT NULL,
                ""MfaRecoveryCodesHashJson"" TEXT NULL,
                ""CreatedAt"" TEXT NOT NULL,
                ""LastLoginAt"" TEXT NULL
            );
            CREATE UNIQUE INDEX IF NOT EXISTS ""IX_Users_Email"" ON ""Users"" (""Email"");

            -- 3. Kullanıcı Oturumları
            CREATE TABLE IF NOT EXISTS ""UserSessions"" (
                ""Id"" TEXT NOT NULL CONSTRAINT ""PK_UserSessions"" PRIMARY KEY,
                ""UserId"" TEXT NOT NULL,
                ""TokenHash"" TEXT NOT NULL,
                ""IsMfaPending"" INTEGER NOT NULL,
                ""CreatedAt"" TEXT NOT NULL,
                ""ExpiresAt"" TEXT NOT NULL,
                ""RevokedAt"" TEXT NULL,
                ""IpAddress"" TEXT NULL,
                ""UserAgent"" TEXT NULL
            );
            CREATE UNIQUE INDEX IF NOT EXISTS ""IX_UserSessions_TokenHash"" ON ""UserSessions"" (""TokenHash"");
            CREATE INDEX IF NOT EXISTS ""IX_UserSessions_UserId"" ON ""UserSessions"" (""UserId"");

            -- 4. Aktivite & Denetim Logları
            CREATE TABLE IF NOT EXISTS ""ActivityLogs"" (
                ""Id"" TEXT NOT NULL CONSTRAINT ""PK_ActivityLogs"" PRIMARY KEY,
                ""UserId"" TEXT NULL,
                ""UserEmailSnapshot"" TEXT NULL,
                ""Action"" TEXT NOT NULL,
                ""TargetType"" TEXT NULL,
                ""TargetId"" TEXT NULL,
                ""DetailsJson"" TEXT NULL,
                ""IpAddress"" TEXT NULL,
                ""Success"" INTEGER NOT NULL,
                ""CreatedAt"" TEXT NOT NULL,
                ""CorrelationId"" TEXT NULL
            );
            CREATE INDEX IF NOT EXISTS ""IX_ActivityLogs_UserId"" ON ""ActivityLogs"" (""UserId"");
            CREATE INDEX IF NOT EXISTS ""IX_ActivityLogs_CreatedAt"" ON ""ActivityLogs"" (""CreatedAt"");

            -- 5. Kullanıcı Davetleri
            CREATE TABLE IF NOT EXISTS ""UserInvites"" (
                ""Id"" TEXT NOT NULL CONSTRAINT ""PK_UserInvites"" PRIMARY KEY,
                ""UserId"" TEXT NOT NULL,
                ""TokenHash"" TEXT NOT NULL,
                ""CreatedAt"" TEXT NOT NULL,
                ""ExpiresAt"" TEXT NOT NULL,
                ""AcceptedAt"" TEXT NULL,
                ""InvitedByUserId"" TEXT NOT NULL
            );
            CREATE UNIQUE INDEX IF NOT EXISTS ""IX_UserInvites_TokenHash"" ON ""UserInvites"" (""TokenHash"");
            CREATE INDEX IF NOT EXISTS ""IX_UserInvites_UserId"" ON ""UserInvites"" (""UserId"");

            -- 6. Güvenlik Profilleri
            CREATE TABLE IF NOT EXISTS ""SecurityProfiles"" (
                ""Id"" TEXT NOT NULL CONSTRAINT ""PK_SecurityProfiles"" PRIMARY KEY,
                ""Name"" TEXT NOT NULL,
                ""AgentDisplayName"" TEXT NULL,
                ""IconBase64"" TEXT NULL,
                ""RestrictTrayMenu"" INTEGER NOT NULL,
                ""RequirePassword"" INTEGER NOT NULL,
                ""PasswordHash"" TEXT NULL,
                ""ConsentMode"" TEXT NOT NULL DEFAULT 'unattended',
                ""ConsentTimeoutSeconds"" INTEGER NOT NULL DEFAULT 30,
                ""ConsentDefaultAction"" TEXT NOT NULL DEFAULT 'deny',
                ""ViewOnlyMode"" INTEGER NOT NULL DEFAULT 0,
                ""AllowRemoteTerminal"" INTEGER NOT NULL DEFAULT 1,
                ""AllowClipboard"" INTEGER NOT NULL DEFAULT 1,
                ""AllowFileTransfer"" INTEGER NOT NULL DEFAULT 1,
                ""ShowConnectionBanner"" INTEGER NOT NULL DEFAULT 1,
                ""CreatedAt"" TEXT NOT NULL,
                ""UpdatedAt"" TEXT NOT NULL
            );

            -- 7. Cihaz Grupları
            CREATE TABLE IF NOT EXISTS ""DeviceGroups"" (
                ""Id"" TEXT NOT NULL CONSTRAINT ""PK_DeviceGroups"" PRIMARY KEY,
                ""Name"" TEXT NOT NULL,
                ""ParentGroupId"" TEXT NULL,
                ""DefaultSecurityProfileId"" TEXT NULL,
                ""EnrollmentKey"" TEXT NULL,
                ""CreatedAt"" TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS ""IX_DeviceGroups_ParentGroupId"" ON ""DeviceGroups"" (""ParentGroupId"");

            -- 7b. Hiyerarşik Kurumsal Profiller ve Modüler Politikalar
            CREATE TABLE IF NOT EXISTS ""Profiles"" (
                ""Id"" TEXT NOT NULL CONSTRAINT ""PK_Profiles"" PRIMARY KEY,
                ""Name"" TEXT NOT NULL,
                ""ParentProfileId"" TEXT NULL,
                ""Type"" TEXT NOT NULL DEFAULT 'Company',
                ""PolicyVersion"" INTEGER NOT NULL DEFAULT 1,
                ""PolicyConfigJson"" TEXT NOT NULL DEFAULT '',
                ""EnrollmentKey"" TEXT NULL,
                ""CreatedAt"" TEXT NOT NULL,
                ""UpdatedAt"" TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS ""IX_Profiles_ParentProfileId"" ON ""Profiles"" (""ParentProfileId"");

            -- 8. Cihaz Uyarıları
            CREATE TABLE IF NOT EXISTS ""DeviceAlerts"" (
                ""Id"" TEXT NOT NULL CONSTRAINT ""PK_DeviceAlerts"" PRIMARY KEY,
                ""DeviceId"" TEXT NOT NULL,
                ""AlertType"" TEXT NOT NULL,
                ""TriggeredAt"" TEXT NOT NULL,
                ""LastNotifiedAt"" TEXT NOT NULL,
                ""ResolvedAt"" TEXT NULL
            );
            CREATE INDEX IF NOT EXISTS ""IX_DeviceAlerts_DeviceId_ResolvedAt"" ON ""DeviceAlerts"" (""DeviceId"", ""ResolvedAt"");

            -- 9. Kalıcı Cihaz Komut Kuyruğu
            CREATE TABLE IF NOT EXISTS ""DeviceCommands"" (
                ""Id"" TEXT NOT NULL CONSTRAINT ""PK_DeviceCommands"" PRIMARY KEY,
                ""RequestId"" TEXT NOT NULL,
                ""DeviceId"" TEXT NOT NULL,
                ""Kind"" TEXT NOT NULL,
                ""Shell"" TEXT NOT NULL,
                ""Command"" TEXT NOT NULL,
                ""Status"" TEXT NOT NULL,
                ""TimeoutSeconds"" INTEGER NOT NULL,
                ""CreatedAt"" TEXT NOT NULL,
                ""DeliveredAt"" TEXT NULL,
                ""CompletedAt"" TEXT NULL,
                ""ExitCode"" INTEGER NULL,
                ""StdOutPreview"" TEXT NULL,
                ""StdErrPreview"" TEXT NULL,
                ""DurationMs"" INTEGER NULL,
                ""TimedOut"" INTEGER NOT NULL DEFAULT 0,
                ""ElevationDenied"" INTEGER NOT NULL DEFAULT 0,
                ""InitiatorUserId"" TEXT NULL,
                ""InitiatorEmail"" TEXT NULL,
                ""CorrelationId"" TEXT NULL
            );
            CREATE UNIQUE INDEX IF NOT EXISTS ""IX_DeviceCommands_RequestId"" ON ""DeviceCommands"" (""RequestId"");
            CREATE INDEX IF NOT EXISTS ""IX_DeviceCommands_DeviceId_Status_CreatedAt"" ON ""DeviceCommands"" (""DeviceId"", ""Status"", ""CreatedAt"");

        ";

        try
        {
            db.Database.ExecuteSqlRaw(sql);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Tablo ve indeks oluşturma sırasında hata.");
            throw;
        }
    }

    private static void EnsureColumns(AppDbContext db, ILogger logger)
    {
        // Güvenli kolon ekleme listesi — zaten varsa SQLite 'duplicate column name' döner, yutulur
        var alterStatements = new[]
        {
            @"ALTER TABLE ""Devices"" ADD COLUMN ""WindowsUpdatesJson"" TEXT;",
            @"ALTER TABLE ""Devices"" ADD COLUMN ""HardwareDetailsJson"" TEXT;",
            @"ALTER TABLE ""Devices"" ADD COLUMN ""SecurityProfileId"" TEXT;",
            @"ALTER TABLE ""Devices"" ADD COLUMN ""GroupId"" TEXT;",
            @"ALTER TABLE ""DeviceGroups"" ADD COLUMN ""EnrollmentKey"" TEXT;",
            @"ALTER TABLE ""SecurityProfiles"" ADD COLUMN ""RequirePassword"" INTEGER NOT NULL DEFAULT 0;",
            @"ALTER TABLE ""SecurityProfiles"" ADD COLUMN ""PasswordHash"" TEXT;",
            @"ALTER TABLE ""SecurityProfiles"" ADD COLUMN ""ConsentMode"" TEXT NOT NULL DEFAULT 'unattended';",
            @"ALTER TABLE ""SecurityProfiles"" ADD COLUMN ""ConsentTimeoutSeconds"" INTEGER NOT NULL DEFAULT 30;",
            @"ALTER TABLE ""SecurityProfiles"" ADD COLUMN ""ConsentDefaultAction"" TEXT NOT NULL DEFAULT 'deny';",
            @"ALTER TABLE ""SecurityProfiles"" ADD COLUMN ""ViewOnlyMode"" INTEGER NOT NULL DEFAULT 0;",
            @"ALTER TABLE ""SecurityProfiles"" ADD COLUMN ""AllowRemoteTerminal"" INTEGER NOT NULL DEFAULT 1;",
            @"ALTER TABLE ""SecurityProfiles"" ADD COLUMN ""AllowClipboard"" INTEGER NOT NULL DEFAULT 1;",
            @"ALTER TABLE ""SecurityProfiles"" ADD COLUMN ""AllowFileTransfer"" INTEGER NOT NULL DEFAULT 1;",
            @"ALTER TABLE ""SecurityProfiles"" ADD COLUMN ""ShowConnectionBanner"" INTEGER NOT NULL DEFAULT 1;",
            @"ALTER TABLE ""ServerSettings"" ADD COLUMN ""SmtpHost"" TEXT;",
            @"ALTER TABLE ""ServerSettings"" ADD COLUMN ""SmtpPort"" INTEGER NOT NULL DEFAULT 465;",
            @"ALTER TABLE ""ServerSettings"" ADD COLUMN ""SmtpUsername"" TEXT;",
            @"ALTER TABLE ""ServerSettings"" ADD COLUMN ""SmtpPasswordEncrypted"" TEXT;",
            @"ALTER TABLE ""ServerSettings"" ADD COLUMN ""SmtpFromAddress"" TEXT;",
            @"ALTER TABLE ""ServerSettings"" ADD COLUMN ""SmtpFromName"" TEXT;",
            @"ALTER TABLE ""ServerSettings"" ADD COLUMN ""SmtpSslMode"" TEXT NOT NULL DEFAULT 'Auto';",
            @"ALTER TABLE ""ActivityLogs"" ADD COLUMN ""CorrelationId"" TEXT;",
            @"ALTER TABLE ""CommandAudits"" ADD COLUMN ""InitiatorUserId"" TEXT;",
            @"ALTER TABLE ""CommandAudits"" ADD COLUMN ""InitiatorEmail"" TEXT;",
            @"ALTER TABLE ""CommandAudits"" ADD COLUMN ""CorrelationId"" TEXT;",
            @"ALTER TABLE ""DeviceCommands"" ADD COLUMN ""InitiatorUserId"" TEXT;",
            @"ALTER TABLE ""DeviceCommands"" ADD COLUMN ""InitiatorEmail"" TEXT;",
            @"ALTER TABLE ""DeviceCommands"" ADD COLUMN ""CorrelationId"" TEXT;",
            @"ALTER TABLE ""RemoteSessions"" ADD COLUMN ""OwnerUserId"" TEXT;",
            @"ALTER TABLE ""RemoteSessions"" ADD COLUMN ""ActiveTokenHash"" TEXT;",
            @"ALTER TABLE ""RemoteSessions"" ADD COLUMN ""ActivatedAt"" TEXT;",
            @"ALTER TABLE ""Users"" ADD COLUMN ""MfaFailedAttempts"" INTEGER NOT NULL DEFAULT 0;",
            @"ALTER TABLE ""Users"" ADD COLUMN ""MfaLockedUntil"" TEXT;",
            @"ALTER TABLE ""Devices"" ADD COLUMN ""ProfileId"" TEXT;",
            @"ALTER TABLE ""Devices"" ADD COLUMN ""HasCustomOverride"" INTEGER NOT NULL DEFAULT 0;",
            @"ALTER TABLE ""Devices"" ADD COLUMN ""CustomOverrideJson"" TEXT;",
            @"ALTER TABLE ""Devices"" ADD COLUMN ""AppliedPolicyVersion"" INTEGER NOT NULL DEFAULT 0;",
            @"ALTER TABLE ""Devices"" ADD COLUMN ""LastPolicySyncedAt"" TEXT;",
            @"ALTER TABLE ""ActivityLogs"" ADD COLUMN ""ProfileId"" TEXT;",
            @"ALTER TABLE ""ActivityLogs"" ADD COLUMN ""DeviceId"" TEXT;",
            @"ALTER TABLE ""ActivityLogs"" ADD COLUMN ""OldValueJson"" TEXT;",
            @"ALTER TABLE ""ActivityLogs"" ADD COLUMN ""NewValueJson"" TEXT;"
        };

        foreach (var statement in alterStatements)
        {
            try
            {
                db.Database.ExecuteSqlRaw(statement);
            }
            catch (SqliteException ex) when (ex.SqliteErrorCode == 1 &&
                ex.Message.Contains("duplicate column name:", StringComparison.OrdinalIgnoreCase))
            {
                // Kolon zaten mevcutsa sessizce devam edilir
            }
        }
    }
}
