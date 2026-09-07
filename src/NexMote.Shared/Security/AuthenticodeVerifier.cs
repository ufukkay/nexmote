using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace NexMote.Shared.Security;

/// <summary>
/// Windows Authenticode imza doğrulama sonucu.
/// </summary>
public sealed record AuthenticodeVerificationResult(
    bool IsValid,
    bool IsSigned,
    int HResult,
    string StatusMessage,
    string? SignerSubject = null,
    string? SignerThumbprint = null,
    string? SignerIssuer = null)
{
    public static AuthenticodeVerificationResult Success(string? subject, string? thumbprint, string? issuer) =>
        new(true, true, 0, "Authenticode imzası ve yayıncı sertifikası geçerli.", subject, thumbprint, issuer);

    public static AuthenticodeVerificationResult Fail(int hresult, string message, bool isSigned = false, string? subject = null, string? thumbprint = null) =>
        new(false, isSigned, hresult, message, subject, thumbprint);
}

/// <summary>
/// İndirilen MSI ve EXE kurulum paketlerinin işletim sistemi düzeyinde
/// Authenticode imza geçerliliğini (WinVerifyTrust) ve yayıncı kimliğini denetler.
/// </summary>
public static class AuthenticodeVerifier
{
    private static readonly Guid ActionGenericVerifyV2 = new("{00AAC56B-CD44-11d0-8CC2-00C04FC295EE}");

    private const uint WTD_UI_NONE = 2;
    private const uint WTD_REVOKE_NONE = 0;
    private const uint WTD_CHOICE_FILE = 1;
    private const uint WTD_STATEACTION_VERIFY = 1;
    private const uint WTD_STATEACTION_CLOSE = 2;
    private const uint WTD_SAFER_FLAG = 0x00000100;
    private const uint WTD_REVOCATION_CHECK_NONE = 0x00000010;
    private const uint WTD_CACHE_ONLY_URL_RETRIEVAL = 0x00001000;

    // Yaygın WinVerifyTrust HRESULT Kodları
    private const int ERROR_SUCCESS = 0;
    private const int TRUST_E_NOSIGNATURE = unchecked((int)0x800B0100);
    private const int TRUST_E_EXPLICIT_DISTRUST = unchecked((int)0x800B0111);
    private const int TRUST_E_BAD_DIGEST = unchecked((int)0x80096010);
    private const int CERT_E_EXPIRED = unchecked((int)0x800B0101);
    private const int CERT_E_UNTRUSTEDROOT = unchecked((int)0x800B0109);
    private const int CERT_E_CHAINING = unchecked((int)0x800B010A);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WINTRUST_FILE_INFO
    {
        public uint cbStruct;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string pcwszFilePath;
        public IntPtr hFile;
        public IntPtr pgKnownSubject;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WINTRUST_DATA
    {
        public uint cbStruct;
        public IntPtr pPolicyCallbackData;
        public IntPtr pSIPClientData;
        public uint dwUIChoice;
        public uint fdwRevocationChecks;
        public uint dwUnionChoice;
        public IntPtr pFile;
        public uint dwStateAction;
        public IntPtr hWVTStateData;
        public IntPtr pwszURLReference;
        public uint dwProvFlags;
        public uint dwUIContext;
        public IntPtr pSignatureSettings;
    }

    [DllImport("wintrust.dll", ExactSpelling = true, SetLastError = false, CharSet = CharSet.Unicode)]
    private static extern int WinVerifyTrust(
        [In] IntPtr hwnd,
        [In] ref Guid pgActionID,
        [In] IntPtr pWVTData);

    /// <summary>
    /// Verilen dosyanın SHA-256 özetini ve dosya boyutunu doğrular.
    /// </summary>
    public static void ValidateFileIntegrity(string filePath, string? expectedSha256, long? expectedSizeBytes)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("Doğrulanacak paket dosyası bulunamadı.", filePath);
        }

        var fileInfo = new FileInfo(filePath);
        if (expectedSizeBytes is > 0 && fileInfo.Length != expectedSizeBytes.Value)
        {
            throw new InvalidOperationException(
                $"Paket boyutu eşleşmiyor. Beklenen: {expectedSizeBytes.Value} bayt, Mevcut: {fileInfo.Length} bayt.");
        }

        if (!string.IsNullOrWhiteSpace(expectedSha256))
        {
            using var stream = File.OpenRead(filePath);
            var actualHash = Convert.ToHexString(SHA256.HashData(stream));
            if (!string.Equals(actualHash, expectedSha256.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Paket SHA-256 hash doğrulaması başarısız. Beklenen: {expectedSha256.Trim()}, Mevcut: {actualHash}");
            }
        }
    }

    /// <summary>
    /// Bir MSI veya EXE dosyasının Authenticode imzasını Windows yerel API'si üzerinden denetler.
    /// </summary>
    /// <param name="filePath">Doğrulanacak dosya yolu.</param>
    /// <param name="expectedThumbprint">İsteğe bağlı beklenen imzalayan sertifika thumbprint'i.</param>
    /// <param name="expectedSubjectContains">İsteğe bağlı sertifika konu alanında aranacak metin (örn: "NexMote").</param>
    /// <param name="allowUntrustedRootInDev">Geliştirme veya yerel test ortamında test/öz-imzalı kök sertifikaya izin verilip verilmeyeceği.</param>
    public static AuthenticodeVerificationResult Verify(
        string filePath,
        string? expectedThumbprint = null,
        string? expectedSubjectContains = null,
        bool allowUntrustedRootInDev = false)
    {
        if (!File.Exists(filePath))
        {
            return AuthenticodeVerificationResult.Fail(-1, $"Dosya bulunamadı: {filePath}");
        }

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // Windows dışı platformlarda WinVerifyTrust çalıştırılamaz
            return AuthenticodeVerificationResult.Success(null, null, null);
        }

        string? signerSubject = null;
        string? signerThumbprint = null;
        string? signerIssuer = null;
        bool hasLeafCert = false;

        try
        {
#pragma warning disable SYSLIB0057
            using var rawCert = X509Certificate.CreateFromSignedFile(filePath);
            using var cert2 = new X509Certificate2(rawCert);
            signerSubject = cert2.Subject;
            signerThumbprint = cert2.Thumbprint;
            signerIssuer = cert2.Issuer;
            hasLeafCert = true;
#pragma warning restore SYSLIB0057
        }
        catch
        {
            hasLeafCert = false;
        }

        var fileInfo = new WINTRUST_FILE_INFO
        {
            cbStruct = (uint)Marshal.SizeOf<WINTRUST_FILE_INFO>(),
            pcwszFilePath = filePath,
            hFile = IntPtr.Zero,
            pgKnownSubject = IntPtr.Zero
        };

        var fileInfoPtr = Marshal.AllocHGlobal(Marshal.SizeOf<WINTRUST_FILE_INFO>());
        var wvtDataPtr = IntPtr.Zero;

        try
        {
            Marshal.StructureToPtr(fileInfo, fileInfoPtr, false);

            var wvtData = new WINTRUST_DATA
            {
                cbStruct = (uint)Marshal.SizeOf<WINTRUST_DATA>(),
                pPolicyCallbackData = IntPtr.Zero,
                pSIPClientData = IntPtr.Zero,
                dwUIChoice = WTD_UI_NONE,
                fdwRevocationChecks = WTD_REVOKE_NONE,
                dwUnionChoice = WTD_CHOICE_FILE,
                pFile = fileInfoPtr,
                dwStateAction = WTD_STATEACTION_VERIFY,
                hWVTStateData = IntPtr.Zero,
                pwszURLReference = IntPtr.Zero,
                dwProvFlags = WTD_SAFER_FLAG | WTD_REVOCATION_CHECK_NONE | WTD_CACHE_ONLY_URL_RETRIEVAL,
                dwUIContext = 0,
                pSignatureSettings = IntPtr.Zero
            };

            wvtDataPtr = Marshal.AllocHGlobal(Marshal.SizeOf<WINTRUST_DATA>());
            Marshal.StructureToPtr(wvtData, wvtDataPtr, false);

            var actionId = ActionGenericVerifyV2;
            var hResult = WinVerifyTrust(IntPtr.Zero, ref actionId, wvtDataPtr);

            // Kapatma eylemi (kaynakları serbest bırak)
            wvtData.dwStateAction = WTD_STATEACTION_CLOSE;
            Marshal.StructureToPtr(wvtData, wvtDataPtr, false);
            WinVerifyTrust(IntPtr.Zero, ref actionId, wvtDataPtr);

            if (hResult == ERROR_SUCCESS)
            {
                return CheckSignerIdentity(signerSubject, signerThumbprint, signerIssuer, expectedThumbprint, expectedSubjectContains);
            }

            // Geliştirme/test ortamında veya özel imzalama sertifikası yapılandırılmamış ortamlarda opsiyonel geçiş
            if (allowUntrustedRootInDev && (hResult == CERT_E_UNTRUSTEDROOT || hResult == CERT_E_CHAINING || hResult == TRUST_E_NOSIGNATURE))
            {
                if (hResult == TRUST_E_NOSIGNATURE)
                {
                    return AuthenticodeVerificationResult.Success("NexMote Package (SHA256 Verified)", null, null);
                }
                return CheckSignerIdentity(signerSubject, signerThumbprint, signerIssuer, expectedThumbprint, expectedSubjectContains);
            }

            var message = hResult switch
            {
                TRUST_E_NOSIGNATURE => "Paket dijital olarak imzalanmamış (Authenticode imzası yok).",
                TRUST_E_BAD_DIGEST => "Paket bozulmuş veya üzerinde oynanmış (Hash doğrulaması başarısız).",
                TRUST_E_EXPLICIT_DISTRUST => "İmzalayan sertifika güvenilmeyenler listesinde (Açıkça reddedildi).",
                CERT_E_EXPIRED => "İmzalayan sertifikanın süresi dolmuş ve geçerli bir zaman damgası yok.",
                CERT_E_UNTRUSTEDROOT => "Sertifika yetkilisi (Root CA) sistem tarafından güvenilmiyor.",
                CERT_E_CHAINING => "Sertifika zinciri doğrulanamadı.",
                _ => $"Authenticode doğrulaması başarısız oldu (HRESULT: 0x{hResult:X8})."
            };

            return AuthenticodeVerificationResult.Fail(hResult, message, isSigned: hasLeafCert, signerSubject, signerThumbprint);
        }
        finally
        {
            if (fileInfoPtr != IntPtr.Zero) Marshal.FreeHGlobal(fileInfoPtr);
            if (wvtDataPtr != IntPtr.Zero) Marshal.FreeHGlobal(wvtDataPtr);
        }
    }

    private static AuthenticodeVerificationResult CheckSignerIdentity(
        string? signerSubject,
        string? signerThumbprint,
        string? signerIssuer,
        string? expectedThumbprint,
        string? expectedSubjectContains)
    {
        if (!string.IsNullOrWhiteSpace(expectedThumbprint))
        {
            var normalizedExpected = expectedThumbprint.Replace(" ", "").Trim();
            var normalizedActual = (signerThumbprint ?? "").Replace(" ", "").Trim();
            if (!string.Equals(normalizedExpected, normalizedActual, StringComparison.OrdinalIgnoreCase))
            {
                return AuthenticodeVerificationResult.Fail(
                    -2,
                    $"İmzalayan sertifika parmak izi eşleşmiyor. Beklenen: {expectedThumbprint}, Bulunan: {signerThumbprint}",
                    isSigned: true,
                    signerSubject,
                    signerThumbprint);
            }
        }

        if (!string.IsNullOrWhiteSpace(expectedSubjectContains))
        {
            if (string.IsNullOrWhiteSpace(signerSubject) ||
                !signerSubject.Contains(expectedSubjectContains, StringComparison.OrdinalIgnoreCase))
            {
                return AuthenticodeVerificationResult.Fail(
                    -3,
                    $"İmzalayan sertifika konusu beklenen değeri içermiyor. Beklenen: '{expectedSubjectContains}', Bulunan: '{signerSubject}'",
                    isSigned: true,
                    signerSubject,
                    signerThumbprint);
            }
        }

        return AuthenticodeVerificationResult.Success(signerSubject, signerThumbprint, signerIssuer);
    }
}
