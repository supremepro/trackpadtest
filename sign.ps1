#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Creates a self-signed code-signing certificate, trusts it on this machine,
    and signs TrackpadWindowControl.exe so Windows Defender / SmartScreen
    accepts it without warnings.

.NOTES
    Run ONCE after building:
        powershell -ExecutionPolicy Bypass -File sign.ps1
#>

param(
    [string]$ExePath = ".\publish\TrackpadWindowControl.exe"
)

$ErrorActionPreference = "Stop"
$CertSubject = "CN=TrackpadWindowControl Local"
$CertStore   = "Cert:\CurrentUser\My"

# ── 1. Reuse or create the self-signed code-signing cert ────────────────────
$cert = Get-ChildItem $CertStore |
        Where-Object { $_.Subject -eq $CertSubject -and $_.NotAfter -gt (Get-Date) } |
        Select-Object -First 1

if (-not $cert) {
    Write-Host "Creating self-signed code-signing certificate..." -ForegroundColor Cyan
    $cert = New-SelfSignedCertificate `
        -Type          CodeSigningCert `
        -Subject       $CertSubject `
        -HashAlgorithm SHA256 `
        -NotAfter      (Get-Date).AddYears(10) `
        -CertStoreLocation $CertStore
    Write-Host "  Created: $($cert.Thumbprint)" -ForegroundColor Green
} else {
    Write-Host "Reusing existing certificate: $($cert.Thumbprint)" -ForegroundColor Green
}

# ── 2. Trust the cert on this machine (Root + TrustedPublisher) ─────────────
foreach ($store in @("Cert:\LocalMachine\Root", "Cert:\LocalMachine\TrustedPublisher")) {
    $existing = Get-ChildItem $store -ErrorAction SilentlyContinue |
                Where-Object { $_.Thumbprint -eq $cert.Thumbprint }
    if (-not $existing) {
        Write-Host "Trusting certificate in $store ..." -ForegroundColor Cyan
        $certObj = [System.Security.Cryptography.X509Certificates.X509Certificate2]$cert
        $storeObj = New-Object System.Security.Cryptography.X509Certificates.X509Store(
            ($store -replace "Cert:\\LocalMachine\\",""),
            [System.Security.Cryptography.X509Certificates.StoreLocation]::LocalMachine)
        $storeObj.Open([System.Security.Cryptography.X509Certificates.OpenFlags]::ReadWrite)
        $storeObj.Add($certObj)
        $storeObj.Close()
        Write-Host "  Done." -ForegroundColor Green
    }
}

# ── 3. Sign the executable ───────────────────────────────────────────────────
if (-not (Test-Path $ExePath)) {
    Write-Error "Executable not found: $ExePath`nRun build.bat first."
    exit 1
}

Write-Host "Signing $ExePath ..." -ForegroundColor Cyan
$result = Set-AuthenticodeSignature `
    -FilePath        $ExePath `
    -Certificate     $cert `
    -HashAlgorithm   SHA256 `
    -TimestampServer "http://timestamp.digicert.com"

if ($result.Status -ne "Valid") {
    Write-Error "Signing failed: $($result.StatusMessage)"
    exit 1
}

Write-Host "  Signed successfully." -ForegroundColor Green

# ── 4. Remove Mark-of-the-Web (zone identifier) if present ──────────────────
Unblock-File -Path $ExePath -ErrorAction SilentlyContinue
Write-Host "  Mark-of-the-Web cleared." -ForegroundColor Green

Write-Host ""
Write-Host "============================================================" -ForegroundColor Green
Write-Host " Done! TrackpadWindowControl.exe is now trusted on this PC." -ForegroundColor Green
Write-Host " Windows Defender and SmartScreen will no longer block it." -ForegroundColor Green
Write-Host "============================================================" -ForegroundColor Green
