namespace NexMote.Shared.Contracts;

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
    string? Password);

/// <summary>Admin panelinde listelenen/gösterilen güvenlik profili bilgisi — şifre hash'i asla içermez.</summary>
public sealed record SecurityProfileDetail(
    Guid Id,
    string Name,
    string? AgentDisplayName,
    string? IconBase64,
    bool RestrictTrayMenu,
    bool RequirePassword,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>Bir cihaza güvenlik profili atama isteği — null ise profil kaldırılır (kısıtlama olmaz).</summary>
public sealed record AssignSecurityProfileRequest(Guid? SecurityProfileId);

/// <summary>
/// Ajanın (Tray/Cleaner) kendi güvenlik profilini (kendi profili veya grubundan miras alınan) öğrenmek için
/// sorguladığı, agent-token korumalı yanıt. Şifre hash'i kesinlikle içermez — sadece branding ve
/// "Durum Paneli/Çıkış/Kaldırma işlemleri bu tek şifreyi ister mi" bayrağı.
/// </summary>
public sealed record AgentSecurityProfileResponse(
    string? AgentDisplayName,
    string? IconBase64,
    bool RestrictTrayMenu,
    bool RequirePassword);

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
