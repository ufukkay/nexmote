namespace NexMote.Shared.Contracts;

/// <summary>
/// Admin tarafında bir güvenlik profilinin oluşturulması/güncellenmesi için kullanılan istek kontratı.
/// Şifre alanları boş/null bırakılırsa (güncellemede) mevcut şifre korunur — yeni bir profil oluştururken
/// ilgili "Require*Password" bayrağı true ise şifre zorunludur.
/// </summary>
public sealed record SecurityProfileRequest(
    string Name,
    string? AgentDisplayName,
    string? IconBase64,
    bool RestrictTrayMenu,
    bool RequireDashboardPassword,
    string? DashboardPassword,
    bool RequireExitPassword,
    string? ExitPassword,
    bool RequireUninstallPassword,
    string? UninstallPassword);

/// <summary>Admin panelinde listelenen/gösterilen güvenlik profili bilgisi — şifre hash'leri asla içermez.</summary>
public sealed record SecurityProfileDetail(
    Guid Id,
    string Name,
    string? AgentDisplayName,
    string? IconBase64,
    bool RestrictTrayMenu,
    bool RequireDashboardPassword,
    bool RequireExitPassword,
    bool RequireUninstallPassword,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>Bir cihaza güvenlik profili atama isteği — null ise profil kaldırılır (kısıtlama olmaz).</summary>
public sealed record AssignSecurityProfileRequest(Guid? SecurityProfileId);

/// <summary>
/// Ajanın (Tray/Cleaner) kendi güvenlik profilini öğrenmek için sorguladığı, agent-token korumalı yanıt.
/// Şifre hash'leri kesinlikle içermez — sadece branding ve "bu işlem şifre ister mi" bayrakları.
/// </summary>
public sealed record AgentSecurityProfileResponse(
    string? AgentDisplayName,
    string? IconBase64,
    bool RestrictTrayMenu,
    bool RequireDashboardPassword,
    bool RequireExitPassword,
    bool RequireUninstallPassword);

/// <summary>
/// Ajanın, kullanıcının girdiği şifreyi sunucuda doğrulatmak için gönderdiği istek.
/// Action değerleri: "dashboard" | "exit" | "uninstall".
/// </summary>
public sealed record SecurityVerifyRequest(string AgentToken, string Action, string Password);

/// <summary>Şifre doğrulama sonucu.</summary>
public sealed record SecurityVerifyResponse(bool Ok);
