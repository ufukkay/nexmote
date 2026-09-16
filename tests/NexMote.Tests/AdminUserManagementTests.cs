using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NexMote.Api.Auth;
using NexMote.Api.Data;
using NexMote.Api.Services;
using NexMote.Shared.Contracts;
using NexMote.Shared.Security;
using Xunit;

namespace NexMote.Tests;

public sealed class AdminUserManagementTests
{
    private sealed class TestDbContextFactory : IDbContextFactory<AppDbContext>
    {
        private readonly DbContextOptions<AppDbContext> _options;
        public TestDbContextFactory(DbContextOptions<AppDbContext> options) => _options = options;
        public AppDbContext CreateDbContext() => new(_options);
    }

    private static (UserAuthService Auth, SqliteConnection Connection, TestDbContextFactory Factory) CreateTestEnvironment()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        using (var initDb = new AppDbContext(options))
        {
            initDb.Database.EnsureCreated();
            DatabaseInitializer.Initialize(initDb, NullLogger.Instance);
        }

        var factory = new TestDbContextFactory(options);
        var hasher = new PasswordHasher<UserEntity>();
        var dataProtection = new EphemeralDataProtectionProvider();
        var auth = new UserAuthService(factory, hasher, new TotpService(), dataProtection);
        return (auth, connection, factory);
    }

    [Fact]
    public void AdminCanChangeTechnicianPassword()
    {
        var (auth, connection, factory) = CreateTestEnvironment();
        using (connection)
        {
            using var db = factory.CreateDbContext();
            var adminId = Guid.NewGuid();
            var techId = Guid.NewGuid();

            var admin = new UserEntity
            {
                Id = adminId,
                Email = "admin@nexmote.com",
                Role = UserRoles.Admin,
                IsActive = true,
                PasswordHash = "hash1"
            };
            var tech = new UserEntity
            {
                Id = techId,
                Email = "tech@nexmote.com",
                Role = UserRoles.Technician,
                IsActive = true,
                PasswordHash = "hash2"
            };
            db.Users.AddRange(admin, tech);

            // Add an active session for technician
            db.UserSessions.Add(new UserSessionEntity
            {
                Id = Guid.NewGuid(),
                UserId = techId,
                TokenHash = "session-hash-1",
                ExpiresAt = DateTimeOffset.UtcNow.AddDays(1)
            });
            db.SaveChanges();

            // Attempt short password (less than 8 chars)
            var (shortSuccess, shortError) = auth.AdminChangePassword(techId, "Ab1!", adminId);
            Assert.False(shortSuccess);
            Assert.NotNull(shortError);

            // Valid password change
            var (success, error) = auth.AdminChangePassword(techId, "StrongPassword123!", adminId);
            Assert.True(success);
            Assert.Null(error);

            // Verify password hash changed and old session revoked
            using var verifyDb = factory.CreateDbContext();
            var updatedTech = verifyDb.Users.First(u => u.Id == techId);
            Assert.NotEqual("hash2", updatedTech.PasswordHash);

            var session = verifyDb.UserSessions.First(s => s.UserId == techId);
            Assert.NotNull(session.RevokedAt);
        }
    }

    [Fact]
    public void AdminCanDeleteTechnicianAndCleansUpSessions()
    {
        var (auth, connection, factory) = CreateTestEnvironment();
        using (connection)
        {
            using var db = factory.CreateDbContext();
            var adminId = Guid.NewGuid();
            var techId = Guid.NewGuid();

            var admin = new UserEntity { Id = adminId, Email = "admin@nexmote.com", Role = UserRoles.Admin, IsActive = true, PasswordHash = "h1" };
            var tech = new UserEntity { Id = techId, Email = "tech@nexmote.com", Role = UserRoles.Technician, IsActive = true, PasswordHash = "h2" };
            db.Users.AddRange(admin, tech);

            db.UserSessions.Add(new UserSessionEntity
            {
                Id = Guid.NewGuid(),
                UserId = techId,
                TokenHash = "token-1",
                ExpiresAt = DateTimeOffset.UtcNow.AddDays(1)
            });
            db.UserInvites.Add(new UserInviteEntity
            {
                Id = Guid.NewGuid(),
                UserId = techId,
                TokenHash = "invite-1",
                ExpiresAt = DateTimeOffset.UtcNow.AddDays(1),
                InvitedByUserId = adminId
            });
            db.SaveChanges();

            // Admin cannot delete own account
            var (selfDelSuccess, selfDelError) = auth.DeleteUser(adminId, adminId);
            Assert.False(selfDelSuccess);
            Assert.Equal("Kendi hesabınızı silemezsiniz.", selfDelError);

            // Admin can delete technician
            var (success, error) = auth.DeleteUser(techId, adminId);
            Assert.True(success);
            Assert.Null(error);

            using var verifyDb = factory.CreateDbContext();
            Assert.Null(verifyDb.Users.FirstOrDefault(u => u.Id == techId));
            Assert.Empty(verifyDb.UserSessions.Where(s => s.UserId == techId));
            Assert.Empty(verifyDb.UserInvites.Where(i => i.UserId == techId));
        }
    }

    [Fact]
    public void AdminCannotDeleteLastRemainingAdmin()
    {
        var (auth, connection, factory) = CreateTestEnvironment();
        using (connection)
        {
            using var db = factory.CreateDbContext();
            var admin1Id = Guid.NewGuid();
            var admin2Id = Guid.NewGuid();

            db.Users.AddRange(
                new UserEntity { Id = admin1Id, Email = "admin1@nexmote.com", Role = UserRoles.Admin, IsActive = true, PasswordHash = "h1" },
                new UserEntity { Id = admin2Id, Email = "admin2@nexmote.com", Role = UserRoles.Admin, IsActive = true, PasswordHash = "h2" }
            );
            db.SaveChanges();

            // Delete admin2 by admin1 -> Allowed because 2 admins exist
            var (firstDelSuccess, _) = auth.DeleteUser(admin2Id, admin1Id);
            Assert.True(firstDelSuccess);

            // Try to delete admin1 (if attempted by another user ID) -> Blocked because only 1 admin remains
            var dummyActorId = Guid.NewGuid();
            var (lastDelSuccess, lastDelError) = auth.DeleteUser(admin1Id, dummyActorId);
            Assert.False(lastDelSuccess);
            Assert.Equal("Sistemdeki son aktif yönetici silinemez.", lastDelError);
        }
    }

    [Fact]
    public void AdminCanResetMfaAndUnlocksAccount()
    {
        var (auth, connection, factory) = CreateTestEnvironment();
        using (connection)
        {
            using var db = factory.CreateDbContext();
            var adminId = Guid.NewGuid();
            var techId = Guid.NewGuid();

            var tech = new UserEntity
            {
                Id = techId,
                Email = "tech@nexmote.com",
                Role = UserRoles.Technician,
                IsActive = true,
                PasswordHash = "h1",
                MfaEnabled = true,
                MfaSecretEncrypted = "encrypted-secret",
                MfaRecoveryCodesHashJson = "[\"hash\"]",
                MfaFailedAttempts = 5,
                MfaLockedUntil = DateTimeOffset.UtcNow.AddMinutes(10)
            };
            db.Users.AddRange(
                new UserEntity { Id = adminId, Email = "admin@nexmote.com", Role = UserRoles.Admin, IsActive = true, PasswordHash = "h2" },
                tech
            );
            db.UserSessions.Add(new UserSessionEntity
            {
                Id = Guid.NewGuid(),
                UserId = techId,
                TokenHash = "pending-challenge",
                IsMfaPending = true,
                ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(5)
            });
            db.SaveChanges();

            var result = auth.AdminResetMfa(techId, adminId);
            Assert.True(result);

            using var verifyDb = factory.CreateDbContext();
            var updatedTech = verifyDb.Users.First(u => u.Id == techId);
            Assert.False(updatedTech.MfaEnabled);
            Assert.Null(updatedTech.MfaSecretEncrypted);
            Assert.Null(updatedTech.MfaRecoveryCodesHashJson);
            Assert.Equal(0, updatedTech.MfaFailedAttempts);
            Assert.Null(updatedTech.MfaLockedUntil);

            var pendingSession = verifyDb.UserSessions.First(s => s.UserId == techId && s.IsMfaPending);
            Assert.NotNull(pendingSession.RevokedAt);
        }
    }

    [Fact]
    public void DatabaseInitializerCreatesSmtpSslModeColumn()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        using var db = new AppDbContext(options);
        db.Database.EnsureCreated();
        DatabaseInitializer.Initialize(db, NullLogger.Instance);

        db.ServerSettings.Add(new ServerSettingEntity
        {
            ServerUrl = "https://nexmote.com",
            EnrollmentKey = "key1",
            HeartbeatSeconds = 20,
            DefaultLocationCode = "DEF"
        });
        db.SaveChanges();

        var setting = db.ServerSettings.First();
        Assert.Equal("Auto", setting.SmtpSslMode);
    }
}
