namespace NexMote.Shared.Contracts;

/// <summary>
/// Bağlantı onay modları ve varsayılan eylem sabitleri.
/// </summary>
public static class SecurityProfileConstants
{
    public const string ConsentUnattended = "unattended";
    public const string ConsentAlwaysPrompt = "always_prompt";
    public const string ConsentPromptIfActive = "prompt_if_active";

    public const string ActionDeny = "deny";
    public const string ActionAllow = "allow";
}

/// <summary>
/// Admin tarafında bir güvenlik profilinin oluşturulması/güncellenmesi için kullanılan istek kontratı.
/// <paramref name="Password"/> boş/null bırakılırsa (güncellemede) mevcut şifre korunur — yeni bir profil
/// oluştururken <paramref name="RequirePassword"/> true ise şifre zorunludur.
/// </summary>
public sealed record SecurityProfileRequest(
    string Name,
    string? AgentDisplayName,
    string? IconBase64,
    bool RestrictTrayMenu,
    bool RequirePassword,
    string? Password,
    string? ConsentMode = SecurityProfileConstants.ConsentUnattended,
    int ConsentTimeoutSeconds = 30,
    string? ConsentDefaultAction = SecurityProfileConstants.ActionDeny,
    bool ViewOnlyMode = false,
    bool AllowRemoteTerminal = true,
    bool AllowClipboard = true,
    bool AllowFileTransfer = true,
    bool ShowConnectionBanner = true);

/// <summary>Admin panelinde listelenen/gösterilen güvenlik profili bilgisi — şifre hash'i asla içermez.</summary>
public sealed record SecurityProfileDetail(
    Guid Id,
    string Name,
    string? AgentDisplayName,
    string? IconBase64,
    bool RestrictTrayMenu,
    bool RequirePassword,
    string ConsentMode,
    int ConsentTimeoutSeconds,
    string ConsentDefaultAction,
    bool ViewOnlyMode,
    bool AllowRemoteTerminal,
    bool AllowClipboard,
    bool AllowFileTransfer,
    bool ShowConnectionBanner,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>Bir cihaza güvenlik profili atama isteği — null ise profil kaldırılır (kısıtlama olmaz).</summary>
public sealed record AssignSecurityProfileRequest(Guid? SecurityProfileId);

/// <summary>
/// Ajanın (Tray/Cleaner) kendi güvenlik profilini (kendi profili veya grubundan miras alınan) öğrenmek için
/// sorguladığı, agent-token korumalı yanıt. Şifre hash'i kesinlikle içermez — branding, erişim/onay politikası
/// ve izin bayrakları içerir.
/// </summary>
public sealed record AgentSecurityProfileResponse(
    string? AgentDisplayName,
    string? IconBase64,
    bool RestrictTrayMenu,
    bool RequirePassword,
    string ConsentMode = SecurityProfileConstants.ConsentUnattended,
    int ConsentTimeoutSeconds = 30,
    string ConsentDefaultAction = SecurityProfileConstants.ActionDeny,
    bool ViewOnlyMode = false,
    bool AllowRemoteTerminal = true,
    bool AllowClipboard = true,
    bool AllowFileTransfer = true,
    bool ShowConnectionBanner = true);

/// <summary>
/// Ajanın, kullanıcının girdiği şifreyi sunucuda doğrulatmak için gönderdiği istek. <paramref name="Action"/>
/// ("dashboard" | "exit" | "uninstall") sadece denetim logunda hangi işlemin denendiğini kaydetmek için
/// kullanılır — doğrulama her zaman profilin tek şifresine karşı yapılır.
/// </summary>
public sealed record SecurityVerifyRequest(string AgentToken, string Action, string Password);

/// <summary>Şifre doğrulama sonucu.</summary>
public sealed record SecurityVerifyResponse(bool Ok);

/// <summary>Cihaz gruplarını (şirket/departman, iç içe) yönetmek için oluşturma/güncelleme isteği.</summary>
public sealed record DeviceGroupRequest(string Name, Guid? ParentGroupId, Guid? DefaultSecurityProfileId);

/// <summary>Admin panelinde listelenen cihaz grubu bilgisi.</summary>
public sealed record DeviceGroupDetail(
    Guid Id,
    string Name,
    Guid? ParentGroupId,
    Guid? DefaultSecurityProfileId,
    string? EnrollmentKey,
    DateTimeOffset CreatedAt);

/// <summary>Bir cihazı bir gruba atama isteği — null ise cihaz gruptan çıkarılır.</summary>
public sealed record AssignDeviceGroupRequest(Guid? GroupId);

/// <summary>Teknisyen bağlandığında ajana gönderilen bağlantı onay isteği sinyali.</summary>
public sealed record ConnectionConsentRequest(
    Guid SessionId,
    string TechnicianName,
    int TimeoutSeconds,
    string DefaultAction);

/// <summary>Hedef bilgisayardaki kullanıcının bağlantı onayına verdiği yanıt sinyali.</summary>
public sealed record ConnectionConsentResponse(
    Guid SessionId,
    bool Accepted,
    string? Reason);
