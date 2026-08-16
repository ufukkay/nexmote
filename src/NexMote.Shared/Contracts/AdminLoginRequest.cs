namespace NexMote.Shared.Contracts;

/// <summary>
/// Web konsolu ve Teknisyen masaüstü uygulamasından sunucuya admin girişi yapmak için kullanılan istek kontratı.
/// </summary>
/// <param name="Email">Admin kullanıcı e-posta adresi.</param>
/// <param name="Password">Admin kullanıcı parolası.</param>
public sealed record AdminLoginRequest(string Email, string Password);
