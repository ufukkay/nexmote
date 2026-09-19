using System.Text.Json;

namespace NexMote.Shared.Contracts;

public static class RemoteAccessModes
{
    public const string Unattended = "unattended";
    public const string Prompt = "prompt";
    public const string AutoAcceptIfIdle = "auto_accept_idle";
}

public static class UsbPolicyModes
{
    public const string AllowAll = "allow_all";
    public const string BlockAll = "block_all";
    public const string BlockStorage = "block_storage";
    public const string ReadOnlyStorage = "read_only_storage";
    public const string WhitelistOnly = "whitelist_only";
}

public static class ProfileTypes
{
    public const string Company = "Company";
    public const string Department = "Department";
    public const string Location = "Location";
    public const string Custom = "Custom";
}

/// <summary>
/// İzin verilen tekil bir USB cihazının tanımlayıcı bilgileri.
/// </summary>
public sealed record UsbDeviceItem(
    string? Name = null,
    string? Vid = null,
    string? Pid = null,
    string? SerialNumber = null);

/// <summary>
/// Ajanın görsel kimlik (branding) ayarları.
/// </summary>
public sealed record PolicyBranding(
    string? CompanyName = null,
    string? AgentDisplayName = null,
    string? LogoBase64 = null,
    string? TrayIconBase64 = null,
    string? AboutText = null,
    string? SupportContact = null);

/// <summary>
/// Ajanın şifreli koruma kalkanı ayarları. Null alanlar üst profilden miras alınır.
/// </summary>
public sealed record PolicyProtection(
    bool? AgentProtection = null,
    string? ProtectionPasswordHash = null,
    bool? AllowAgentExit = null,
    bool? AllowServiceStop = null,
    bool? AllowAgentUninstall = null);

/// <summary>
/// Uzaktan erişim ve kullanıcı onay politikası. Null alanlar üst profilden miras alınır.
/// </summary>
public sealed record PolicyRemoteAccess(
    string? Mode = null,
    int? PromptTimeoutSeconds = null,
    string? DefaultAction = null,
    int? IdleTimeoutMinutes = null,
    bool? ShowConnectionBanner = null,
    bool? ViewOnlyMode = null,
    bool? AllowRemoteTerminal = null,
    bool? AllowClipboard = null,
    bool? AllowFileTransfer = null);

/// <summary>
/// USB donanım ve depolama erişim politikası. Null alanlar üst profilden miras alınır.
/// </summary>
public sealed record PolicyUsb(
    string? Mode = null,
    List<UsbDeviceItem>? Whitelist = null);

/// <summary>
/// Uçtan uca tip-güvenli, modüler ve versiyonlanmış politika belgesi.
/// </summary>
public sealed record PolicyDocument
{
    public int Version { get; set; } = 1;
    public Guid? ProfileId { get; set; }
    public string? ProfileName { get; set; }
    public Guid? ParentProfileId { get; set; }
    public PolicyBranding Branding { get; set; } = new();
    public PolicyProtection Protection { get; set; } = new();
    public PolicyRemoteAccess RemoteAccess { get; set; } = new();
    public PolicyUsb Usb { get; set; } = new();
    public Dictionary<string, string> CustomModules { get; set; } = new();

    public static PolicyDocument CreateDefaultRoot() => new()
    {
        Branding = new PolicyBranding(CompanyName: "NexMote", AgentDisplayName: "NexMote Agent"),
        Protection = new PolicyProtection(AgentProtection: false, AllowAgentExit: true, AllowServiceStop: true, AllowAgentUninstall: true),
        RemoteAccess = new PolicyRemoteAccess(
            Mode: RemoteAccessModes.Unattended,
            PromptTimeoutSeconds: 30,
            DefaultAction: "deny",
            IdleTimeoutMinutes: 10,
            ShowConnectionBanner: true,
            ViewOnlyMode: false,
            AllowRemoteTerminal: true,
            AllowClipboard: true,
            AllowFileTransfer: true),
        Usb = new PolicyUsb(Mode: UsbPolicyModes.AllowAll, Whitelist: new List<UsbDeviceItem>())
    };

    /// <summary>
    /// Üst politikayı bu alt politika (override) ile birleştirip efektif politikayı üretir.
    /// Alt politikada açıkça belirtilen (non-null) değerler üst politikanın değerlerini ezer (override).
    /// </summary>
    public PolicyDocument MergeWithChild(PolicyDocument? child, Guid childProfileId, string childProfileName, int childVersion)
    {
        if (child is null)
        {
            return this with
            {
                ProfileId = childProfileId,
                ProfileName = childProfileName,
                Version = childVersion
            };
        }

        var branding = new PolicyBranding(
            CompanyName: child.Branding.CompanyName ?? Branding.CompanyName,
            AgentDisplayName: child.Branding.AgentDisplayName ?? Branding.AgentDisplayName,
            LogoBase64: child.Branding.LogoBase64 ?? Branding.LogoBase64,
            TrayIconBase64: child.Branding.TrayIconBase64 ?? Branding.TrayIconBase64,
            AboutText: child.Branding.AboutText ?? Branding.AboutText,
            SupportContact: child.Branding.SupportContact ?? Branding.SupportContact);

        var protection = new PolicyProtection(
            AgentProtection: child.Protection.AgentProtection ?? Protection.AgentProtection,
            ProtectionPasswordHash: !string.IsNullOrEmpty(child.Protection.ProtectionPasswordHash)
                ? child.Protection.ProtectionPasswordHash
                : Protection.ProtectionPasswordHash,
            AllowAgentExit: child.Protection.AllowAgentExit ?? Protection.AllowAgentExit,
            AllowServiceStop: child.Protection.AllowServiceStop ?? Protection.AllowServiceStop,
            AllowAgentUninstall: child.Protection.AllowAgentUninstall ?? Protection.AllowAgentUninstall);

        var remoteAccess = new PolicyRemoteAccess(
            Mode: !string.IsNullOrEmpty(child.RemoteAccess.Mode) ? child.RemoteAccess.Mode : RemoteAccess.Mode,
            PromptTimeoutSeconds: child.RemoteAccess.PromptTimeoutSeconds ?? RemoteAccess.PromptTimeoutSeconds,
            DefaultAction: !string.IsNullOrEmpty(child.RemoteAccess.DefaultAction) ? child.RemoteAccess.DefaultAction : RemoteAccess.DefaultAction,
            IdleTimeoutMinutes: child.RemoteAccess.IdleTimeoutMinutes ?? RemoteAccess.IdleTimeoutMinutes,
            ShowConnectionBanner: child.RemoteAccess.ShowConnectionBanner ?? RemoteAccess.ShowConnectionBanner,
            ViewOnlyMode: child.RemoteAccess.ViewOnlyMode ?? RemoteAccess.ViewOnlyMode,
            AllowRemoteTerminal: child.RemoteAccess.AllowRemoteTerminal ?? RemoteAccess.AllowRemoteTerminal,
            AllowClipboard: child.RemoteAccess.AllowClipboard ?? RemoteAccess.AllowClipboard,
            AllowFileTransfer: child.RemoteAccess.AllowFileTransfer ?? RemoteAccess.AllowFileTransfer);

        var usb = new PolicyUsb(
            Mode: !string.IsNullOrEmpty(child.Usb.Mode) ? child.Usb.Mode : Usb.Mode,
            Whitelist: child.Usb.Whitelist != null && child.Usb.Whitelist.Count > 0
                ? child.Usb.Whitelist
                : Usb.Whitelist ?? new List<UsbDeviceItem>());

        var customModules = new Dictionary<string, string>(CustomModules);
        if (child.CustomModules != null)
        {
            foreach (var (k, v) in child.CustomModules)
            {
                customModules[k] = v;
            }
        }

        return new PolicyDocument
        {
            Version = childVersion,
            ProfileId = childProfileId,
            ProfileName = childProfileName,
            ParentProfileId = ProfileId,
            Branding = branding,
            Protection = protection,
            RemoteAccess = remoteAccess,
            Usb = usb,
            CustomModules = customModules
        };
    }
}

/// <summary>
/// Web konsolu profil ağacında bir düğümü temsil eder.
/// </summary>
public sealed record ProfileTreeNode(
    Guid Id,
    string Name,
    Guid? ParentProfileId,
    string Type,
    int PolicyVersion,
    int DeviceCount,
    int SubProfileCount,
    string? CompanyName,
    DateTimeOffset UpdatedAt);

/// <summary>
/// Profil detayını ve efektif politikasını döner.
/// </summary>
public sealed record ProfileDetailResponse(
    Guid Id,
    string Name,
    Guid? ParentProfileId,
    string Type,
    int PolicyVersion,
    string? EnrollmentKey,
    PolicyDocument OwnPolicy,
    PolicyDocument EffectivePolicy,
    int DeviceCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>
/// Profil oluşturma veya güncelleme isteği.
/// </summary>
public sealed record ProfileUpsertRequest(
    string Name,
    Guid? ParentProfileId,
    string Type,
    PolicyDocument Policy,
    string? NewProtectionPassword = null);

/// <summary>
/// Cihaz bazlı override politikası isteği.
/// </summary>
public sealed record DevicePolicyOverrideRequest(
    bool HasCustomOverride,
    PolicyDocument? CustomPolicy);

/// <summary>
/// Çoklu cihazı bir profile toplu atama isteği.
/// </summary>
public sealed record BulkAssignProfileRequest(
    List<Guid> DeviceIds,
    Guid? TargetProfileId);

/// <summary>
/// Ajan şifre doğrulama isteği (Çıkış, durdurma, kaldırma).
/// </summary>
public sealed record VerifyProtectionRequest(
    string AgentToken,
    string Action,
    string Password);

/// <summary>
/// Ajan şifre doğrulama yanıtı.
/// </summary>
public sealed record VerifyProtectionResponse(
    bool Ok,
    string? Reason = null);
