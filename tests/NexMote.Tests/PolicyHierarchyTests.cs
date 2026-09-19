using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NexMote.Api.Auth;
using NexMote.Api.Data;
using NexMote.Api.Services;
using NexMote.Shared.Contracts;
using Xunit;

namespace NexMote.Tests;

public sealed class PolicyHierarchyTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly IPasswordHasher<ProfileEntity> _passwordHasher;
    private readonly ProfileService _profileService;

    public PolicyHierarchyTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        using (var initDb = new AppDbContext(options))
        {
            DatabaseInitializer.Initialize(initDb, NullLogger.Instance);
        }

        _dbFactory = new TestDbContextFactory(options);
        _passwordHasher = new PasswordHasher<ProfileEntity>();

        var userHasher = new PasswordHasher<UserEntity>();
        var dataProtection = new Microsoft.AspNetCore.DataProtection.EphemeralDataProtectionProvider();
        var userAuth = new UserAuthService(_dbFactory, userHasher, new TotpService(), dataProtection);
        _profileService = new ProfileService(_dbFactory, _passwordHasher, userAuth);
    }

    public void Dispose()
    {
        _connection.Dispose();
    }

    [Fact]
    public void CompanyPolicyInheritedByDefaultByDepartment()
    {
        var adminId = Guid.NewGuid();

        // 1. Kök Şirket Profili oluştur (USB: BlockAll, Branding: Talay Logistics)
        var companyPolicy = new PolicyDocument
        {
            Branding = new PolicyBranding(CompanyName: "Talay Logistics", AgentDisplayName: "Talay Agent"),
            Usb = new PolicyUsb(Mode: UsbPolicyModes.BlockAll)
        };
        var company = _profileService.Create(new ProfileUpsertRequest("Talay Logistics", null, ProfileTypes.Company, companyPolicy), adminId);
        Assert.NotNull(company);

        // 2. Alt Departman Profili oluştur (Muhasebe - USB belirtilmemiş, mirasa bırakılmış)
        var deptPolicy = new PolicyDocument
        {
            Branding = new PolicyBranding(AboutText: "Muhasebe Destek")
        };
        var dept = _profileService.Create(new ProfileUpsertRequest("Muhasebe", company.Id, ProfileTypes.Department, deptPolicy), adminId);
        Assert.NotNull(dept);

        // 3. Efektif politikayı sorgula: Muhasebe, şirketin USB = BlockAll ve CompanyName = Talay Logistics ayarlarını miras almalı
        var detail = _profileService.Get(dept.Id);
        Assert.NotNull(detail);
        Assert.Equal("Talay Logistics", detail.EffectivePolicy.Branding.CompanyName);
        Assert.Equal("Talay Agent", detail.EffectivePolicy.Branding.AgentDisplayName);
        Assert.Equal("Muhasebe Destek", detail.EffectivePolicy.Branding.AboutText);
        Assert.Equal(UsbPolicyModes.BlockAll, detail.EffectivePolicy.Usb.Mode);
    }

    [Fact]
    public void DepartmentCanOverrideCompanyPolicy()
    {
        var adminId = Guid.NewGuid();

        // 1. Şirket profili: USB = BlockAll
        var companyPolicy = new PolicyDocument
        {
            Branding = new PolicyBranding(CompanyName: "Talay Logistics"),
            Usb = new PolicyUsb(Mode: UsbPolicyModes.BlockAll)
        };
        var company = _profileService.Create(new ProfileUpsertRequest("Talay Logistics", null, ProfileTypes.Company, companyPolicy), adminId);
        Assert.NotNull(company);

        // 2. Bilgi Teknolojileri departmanı: USB = AllowAll (OVERRIDE!)
        var itPolicy = new PolicyDocument
        {
            Usb = new PolicyUsb(Mode: UsbPolicyModes.AllowAll)
        };
        var itDept = _profileService.Create(new ProfileUpsertRequest("Bilgi Teknolojileri", company.Id, ProfileTypes.Department, itPolicy), adminId);
        Assert.NotNull(itDept);

        // 3. Efektif politikada IT için USB serbest olmalı, fakat Şirket Adı miras alınmalı
        var itDetail = _profileService.Get(itDept.Id);
        Assert.NotNull(itDetail);
        Assert.Equal(UsbPolicyModes.AllowAll, itDetail.EffectivePolicy.Usb.Mode);
        Assert.Equal("Talay Logistics", itDetail.EffectivePolicy.Branding.CompanyName);
    }

    [Fact]
    public void DeviceSpecificOverrideWinsOverDepartmentPolicy()
    {
        var adminId = Guid.NewGuid();

        // 1. Şirket -> Departman
        var companyPolicy = new PolicyDocument { Usb = new PolicyUsb(Mode: UsbPolicyModes.BlockAll) };
        var company = _profileService.Create(new ProfileUpsertRequest("Talay Logistics", null, ProfileTypes.Company, companyPolicy), adminId);
        var dept = _profileService.Create(new ProfileUpsertRequest("Muhasebe", company!.Id, ProfileTypes.Department, new PolicyDocument()), adminId);

        // 2. Cihaz oluştur ve Muhasebe'ye bağla
        var deviceId = Guid.NewGuid();
        using (var db = _dbFactory.CreateDbContext())
        {
            db.Devices.Add(new DeviceEntity
            {
                Id = deviceId,
                DeviceName = "MUH-PC-015",
                DomainName = "TALAY",
                AgentToken = "test-token-123",
                ProfileId = dept!.Id
            });
            db.SaveChanges();
        }

        // Önce cihaz departman politikasını (BlockAll) almalı
        var initialPolicy = _profileService.GetEffectivePolicyForDevice(deviceId);
        Assert.Equal(UsbPolicyModes.BlockAll, initialPolicy.Usb.Mode);

        // 3. Cihaza özel override uygula: USB = WhitelistOnly
        var customPolicy = new PolicyDocument
        {
            Usb = new PolicyUsb(
                Mode: UsbPolicyModes.WhitelistOnly,
                Whitelist: new List<UsbDeviceItem>
                {
                    new UsbDeviceItem(Name: "IT Kingston", Vid: "0951", Pid: "1666")
                })
        };
        var overrideApplied = _profileService.SetDeviceOverride(deviceId, new DevicePolicyOverrideRequest(true, customPolicy), adminId);
        Assert.True(overrideApplied);

        // 4. Efektif politika sorgusu: Cihaz özel override'ı kazanmalı
        var effective = _profileService.GetEffectivePolicyForDevice(deviceId);
        Assert.Equal(UsbPolicyModes.WhitelistOnly, effective.Usb.Mode);
        Assert.Single(effective.Usb.Whitelist!);
        Assert.Equal("0951", effective.Usb.Whitelist![0].Vid);
    }

    [Fact]
    public void ProtectionPasswordInheritedAndVerifiedCorrectly()
    {
        var adminId = Guid.NewGuid();

        // 1. Şirket düzeyinde koruma şifresi tanımla
        var companyPolicy = new PolicyDocument
        {
            Protection = new PolicyProtection(AgentProtection: true, AllowAgentExit: false)
        };
        var company = _profileService.Create(new ProfileUpsertRequest("Talay Logistics", null, ProfileTypes.Company, companyPolicy, NewProtectionPassword: "SecretPassword123!"), adminId);
        Assert.NotNull(company);

        // 2. Cihazı bu şirkete bağla
        var deviceId = Guid.NewGuid();
        const string token = "agent-secure-token";
        using (var db = _dbFactory.CreateDbContext())
        {
            db.Devices.Add(new DeviceEntity
            {
                Id = deviceId,
                DeviceName = "TALAY-SRV-01",
                DomainName = "TALAY",
                AgentToken = token,
                ProfileId = company.Id
            });
            db.SaveChanges();
        }

        // 3. Yanlış şifre ile doğrula -> Başarısız olmalı
        var wrongResult = _profileService.VerifyProtectionPassword(deviceId, token, "exit", "WrongPassword!");
        Assert.False(wrongResult);

        // 4. Doğru şifre ile doğrula -> Başarılı olmalı
        var correctResult = _profileService.VerifyProtectionPassword(deviceId, token, "exit", "SecretPassword123!");
        Assert.True(correctResult);
    }

    private sealed class TestDbContextFactory : IDbContextFactory<AppDbContext>
    {
        private readonly DbContextOptions<AppDbContext> _options;
        public TestDbContextFactory(DbContextOptions<AppDbContext> options) => _options = options;
        public AppDbContext CreateDbContext() => new(_options);
    }
}
