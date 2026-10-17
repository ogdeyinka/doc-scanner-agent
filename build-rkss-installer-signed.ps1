param(
    [string]$CertPath = $env:SIGN_CERT_PATH,
    [string]$CertSubject = "RK Systems & Services",
    [string]$TimestampUrl = "http://timestamp.digicert.com",
    [string]$InstallerVersion = "2.1.3"
)

$ErrorActionPreference = "Stop"

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$installerScript = Join-Path $scriptRoot "installer\RKSSScannerAgent.iss"
$outputInstaller = Join-Path $scriptRoot "installer\output\RKSS-Scanner-Agent-Setup-$InstallerVersion.exe"
$innoCompiler = "C:\Program Files (x86)\Inno Setup 6\ISCC.exe"

if (-not (Test-Path $innoCompiler)) {
    throw "Inno Setup compiler was not found at $innoCompiler."
}

$signTool = Get-ChildItem "C:\Program Files (x86)\Windows Kits\10\bin" -Recurse -Filter signtool.exe -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -match "\\x64\\signtool\.exe$" -or $_.FullName -match "\\x86\\signtool\.exe$" } |
    Sort-Object FullName -Descending |
    Select-Object -First 1 -ExpandProperty FullName

if (-not $signTool) {
    throw "signtool.exe was not found. Install Windows SDK Signing Tools, then rerun this script."
}

if ($CertPath) {
    if (-not (Test-Path $CertPath)) {
        throw "SIGN_CERT_PATH / -CertPath was provided but the file was not found: $CertPath"
    }
    if (-not $env:SIGN_CERT_PASSWORD) {
        throw "SIGN_CERT_PASSWORD is required when signing with a PFX certificate."
    }

    $signCommand = '"{0}" sign /fd SHA256 /tr "{1}" /td SHA256 /f "{2}" /p "$qSIGN_CERT_PASSWORD$q" $f' -f $signTool, $TimestampUrl, $CertPath
} else {
    $cert = Get-ChildItem Cert:\CurrentUser\My, Cert:\LocalMachine\My -CodeSigningCert -ErrorAction SilentlyContinue |
        Where-Object { $_.Subject -like "*$CertSubject*" -and $_.HasPrivateKey } |
        Sort-Object NotAfter -Descending |
        Select-Object -First 1

    if (-not $cert) {
        throw "No RKSS code-signing certificate was found. Import the RKSS PFX and rerun, or set SIGN_CERT_PATH to the PFX file path."
    }

    $signCommand = '"{0}" sign /fd SHA256 /tr "{1}" /td SHA256 /sha1 {2} $f' -f $signTool, $TimestampUrl, $cert.Thumbprint
}

& $innoCompiler "/DSIGN_INSTALLER" "/Srkss=$signCommand" $installerScript
if ($LASTEXITCODE -ne 0) {
    throw "Inno Setup failed with exit code $LASTEXITCODE."
}

& $signTool verify /pa /v $outputInstaller
if ($LASTEXITCODE -ne 0) {
    throw "Signature verification failed with exit code $LASTEXITCODE."
}

Write-Host "Signed installer created at $outputInstaller"
