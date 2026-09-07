Set-StrictMode -Version Latest

function Resolve-NexMoteSigningCertificate {
    param(
        [string]$CertificateThumbprint,
        [string]$CertificatePath,
        [string]$CertificatePassword
    )

    if (-not [string]::IsNullOrWhiteSpace($CertificatePath)) {
        if (-not (Test-Path -LiteralPath $CertificatePath)) {
            throw "Signing certificate file not found: $CertificatePath"
        }

        $password = $CertificatePassword
        if ([string]::IsNullOrWhiteSpace($password)) {
            $password = $env:NEXMOTE_SIGNING_CERT_PASSWORD
        }

        if ([string]::IsNullOrWhiteSpace($password)) {
            throw "Signing certificate password is required for PFX files. Set NEXMOTE_SIGNING_CERT_PASSWORD or pass -SigningCertificatePassword."
        }

        $securePassword = ConvertTo-SecureString -String $password -AsPlainText -Force
        $certificate = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2(
            $CertificatePath,
            $securePassword,
            [System.Security.Cryptography.X509Certificates.X509KeyStorageFlags]::MachineKeySet
        )

        if (-not $certificate.HasPrivateKey) {
            throw "Signing certificate does not include a private key: $CertificatePath"
        }

        return $certificate
    }

    $thumbprint = $CertificateThumbprint
    if ([string]::IsNullOrWhiteSpace($thumbprint)) {
        $thumbprint = $env:NEXMOTE_SIGNING_CERT_THUMBPRINT
    }

    if ([string]::IsNullOrWhiteSpace($thumbprint)) {
        throw "Signing certificate is required. Pass -SigningCertificateThumbprint, set NEXMOTE_SIGNING_CERT_THUMBPRINT, or pass -SigningCertificatePath."
    }

    $normalizedThumbprint = $thumbprint -replace '\s', ''
    $certificate = Get-ChildItem -Path Cert:\CurrentUser\My, Cert:\LocalMachine\My |
        Where-Object { ($_.Thumbprint -replace '\s', '') -ieq $normalizedThumbprint -and $_.HasPrivateKey } |
        Select-Object -First 1

    if ($null -eq $certificate) {
        throw "Signing certificate with private key was not found in CurrentUser/My or LocalMachine/My: $thumbprint"
    }

    return $certificate
}

function Invoke-NexMoteAuthenticodeSigning {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$Paths,
        [Parameter(Mandatory = $true)]
        [System.Security.Cryptography.X509Certificates.X509Certificate2]$Certificate,
        [string]$TimestampUrl = "http://timestamp.digicert.com"
    )

    foreach ($path in $Paths) {
        if (-not (Test-Path -LiteralPath $path)) {
            throw "File to sign was not found: $path"
        }

        Write-Host "Signing $path"
        $signature = Set-AuthenticodeSignature -LiteralPath $path -Certificate $Certificate -TimestampServer $TimestampUrl -HashAlgorithm SHA256
        if ($signature.Status -ne "Valid") {
            throw "Failed to sign $path. Authenticode status: $($signature.Status) $($signature.StatusMessage)"
        }
    }
}

function Assert-NexMoteAuthenticodeSignature {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$Paths,
        [string]$ExpectedThumbprint
    )

    $normalizedExpectedThumbprint = ""
    if (-not [string]::IsNullOrWhiteSpace($ExpectedThumbprint)) {
        $normalizedExpectedThumbprint = $ExpectedThumbprint -replace '\s', ''
    }

    foreach ($path in $Paths) {
        if (-not (Test-Path -LiteralPath $path)) {
            throw "Signed file was not found: $path"
        }

        $signature = Get-AuthenticodeSignature -LiteralPath $path
        if ($signature.Status -ne "Valid") {
            throw "Unsigned or invalid package blocked: $path ($($signature.Status) $($signature.StatusMessage))"
        }

        if (-not [string]::IsNullOrWhiteSpace($normalizedExpectedThumbprint)) {
            $actualThumbprint = $signature.SignerCertificate.Thumbprint -replace '\s', ''
            if ($actualThumbprint -ine $normalizedExpectedThumbprint) {
                throw "Signed file signer mismatch for $path. Expected $ExpectedThumbprint, got $($signature.SignerCertificate.Thumbprint)."
            }
        }
    }
}

function New-NexMoteDetachedManifestSignature {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ManifestPath,
        [Parameter(Mandatory = $true)]
        [string]$SignaturePath,
        [Parameter(Mandatory = $true)]
        [System.Security.Cryptography.X509Certificates.X509Certificate2]$Certificate,
        [string]$KeyId = ""
    )

    if (-not (Test-Path -LiteralPath $ManifestPath)) {
        throw "Manifest to sign was not found: $ManifestPath"
    }

    $rsa = [System.Security.Cryptography.X509Certificates.RSACertificateExtensions]::GetRSAPrivateKey($Certificate)
    if ($null -eq $rsa) {
        throw "Signing certificate does not expose an RSA private key."
    }

    $effectiveKeyId = $KeyId
    if ([string]::IsNullOrWhiteSpace($effectiveKeyId)) {
        $effectiveKeyId = $Certificate.Thumbprint
    }

    $manifestBytes = [System.IO.File]::ReadAllBytes($ManifestPath)
    $signatureBytes = $rsa.SignData(
        $manifestBytes,
        [System.Security.Cryptography.HashAlgorithmName]::SHA256,
        [System.Security.Cryptography.RSASignaturePadding]::Pkcs1
    )

    $payload = [ordered]@{
        keyId = $effectiveKeyId
        algorithm = "RSASSA-PKCS1-v1_5-SHA256"
        certificateThumbprint = $Certificate.Thumbprint
        certificate = [Convert]::ToBase64String($Certificate.RawData)
        signature = [Convert]::ToBase64String($signatureBytes)
        signedAtUtc = [DateTimeOffset]::UtcNow.ToString("O")
    }

    [System.IO.File]::WriteAllText($SignaturePath, ($payload | ConvertTo-Json -Depth 4), [System.Text.Encoding]::UTF8)
    Write-Host "Wrote detached manifest signature: $SignaturePath"
}
