param(
    [ValidateSet("Install", "Repair", "Uninstall", "Start", "Stop", "Status")]
    [string]$Action = "Install",

    [string]$SourcePath = "",
    [string]$InstallPath = "C:\RKSS\ScannerAgent\app",
    [string]$ServiceName = "RKSSScannerAgent",
    [string]$DisplayName = "RKSS Scanner Agent",
    [string]$AllowedOrigins = "",
    [string]$BindUrl = "http://127.0.0.1:8890",
    [string]$DesktopBindUrl = "http://127.0.0.1:8891",
    [string]$StoragePath = "C:\RKSS\ScannerAgent\Scans"
)

$ErrorActionPreference = "Stop"

function Write-Step($message) {
    Write-Host "[RKSS Scanner Agent] $message"
}

function Assert-Administrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw "Run this script from an elevated PowerShell window: right-click PowerShell and choose 'Run as administrator'."
    }
}

function Resolve-SourcePath {
    if ($SourcePath -and (Test-Path $SourcePath)) {
        return (Resolve-Path $SourcePath).Path
    }

    $scriptDir = Split-Path -Parent $MyInvocation.ScriptName
    $candidate = Join-Path $scriptDir "publish\rkss-win-x64"
    if (Test-Path (Join-Path $candidate "RKSS.ScannerAgent.exe")) {
        return (Resolve-Path $candidate).Path
    }

    $candidate = Join-Path $scriptDir "publish\win-x64"
    if (Test-Path (Join-Path $candidate "RKSS.ScannerAgent.exe")) {
        return (Resolve-Path $candidate).Path
    }

    if (Test-Path (Join-Path $scriptDir "RKSS.ScannerAgent.exe")) {
        return (Resolve-Path $scriptDir).Path
    }

    throw "Could not find RKSS.ScannerAgent.exe. Pass -SourcePath or run this script from the folder containing the published agent."
}

function Get-ServiceOrNull($name) {
    try {
        return Get-Service -Name $name -ErrorAction Stop
    } catch {
        return $null
    }
}

function Stop-AgentService {
    $svc = Get-ServiceOrNull $ServiceName
    if ($svc -and $svc.Status -ne "Stopped") {
        Write-Step "Stopping service $ServiceName..."
        & sc.exe stop $ServiceName | Out-Null
        Start-Sleep -Seconds 2
    }
}

function Remove-AgentService {
    $svc = Get-ServiceOrNull $ServiceName
    if ($svc) {
        Stop-AgentService
        Write-Step "Removing service $ServiceName..."
        & sc.exe delete $ServiceName | Out-Null
        Start-Sleep -Seconds 2
    }
}

function Copy-AgentFiles($resolvedSource) {
    if (-not (Test-Path $InstallPath)) {
        New-Item -Path $InstallPath -ItemType Directory -Force | Out-Null
    }
    if (-not (Test-Path $StoragePath)) {
        New-Item -Path $StoragePath -ItemType Directory -Force | Out-Null
    }

    Write-Step "Copying agent files from $resolvedSource to $InstallPath..."
    Copy-Item -Path (Join-Path $resolvedSource "*") -Destination $InstallPath -Recurse -Force
}

function Update-AppSettings {
    $settingsPath = Join-Path $InstallPath "appsettings.json"
    if (-not (Test-Path $settingsPath)) {
        throw "appsettings.json was not found in $InstallPath."
    }

    $json = Get-Content $settingsPath -Raw | ConvertFrom-Json
    if (-not $json.ScannerAgent) {
        $json | Add-Member -MemberType NoteProperty -Name ScannerAgent -Value ([pscustomobject]@{})
    }

    if ($json.ScannerAgent.PSObject.Properties.Name -contains "AgentToken") {
        $json.ScannerAgent.PSObject.Properties.Remove("AgentToken")
    }

    if ($AllowedOrigins) {
        $origins = @()
        $AllowedOrigins.Split(",") | ForEach-Object {
            $value = $_.Trim()
            if ($value) { $origins += $value }
        }
        if ($origins.Count -gt 0) {
            $json.ScannerAgent.AllowedOrigins = $origins
        }
    }

    $json.ScannerAgent.BindUrl = $BindUrl
    $json.ScannerAgent.DesktopBindUrl = $DesktopBindUrl
    $json.ScannerAgent.StoragePath = $StoragePath
    $json | ConvertTo-Json -Depth 10 | Set-Content -Path $settingsPath -Encoding UTF8
}

function Install-AgentService {
    $exePath = Join-Path $InstallPath "RKSS.ScannerAgent.exe"
    if (-not (Test-Path $exePath)) {
        throw "RKSS.ScannerAgent.exe was not found at $exePath."
    }

    Remove-AgentService

    Write-Step "Creating Windows service $ServiceName..."
    & sc.exe create $ServiceName binPath= "`"$exePath`"" start= auto DisplayName= "`"$DisplayName`"" | Out-Null
    & sc.exe description $ServiceName "Local RKSS DIAS scanner bridge for WIA/TWAIN-compatible desktop scanners." | Out-Null

    Write-Step "Starting service $ServiceName..."
    & sc.exe start $ServiceName | Out-Null
}

function Show-Status {
    $svc = Get-ServiceOrNull $ServiceName
    if (-not $svc) {
        Write-Host "Service $ServiceName is not installed."
        return
    }

    $svc | Format-List Name,DisplayName,Status,StartType
    Write-Host ""
    Write-Host "Health check:"
    try {
        Invoke-WebRequest -UseBasicParsing -Uri "http://127.0.0.1:8890/health" -TimeoutSec 5 | Select-Object -ExpandProperty Content
    } catch {
        Write-Host "Agent did not respond on http://127.0.0.1:8890/health"
    }
}

Assert-Administrator

switch ($Action) {
    "Install" {
        $source = Resolve-SourcePath
        Stop-AgentService
        Copy-AgentFiles $source
        Update-AppSettings
        Install-AgentService
        Show-Status
    }
    "Repair" {
        $source = Resolve-SourcePath
        Stop-AgentService
        Copy-AgentFiles $source
        Update-AppSettings
        Install-AgentService
        Show-Status
    }
    "Uninstall" {
        Remove-AgentService
        Write-Step "Service removed. Installed files were left at $InstallPath."
    }
    "Start" {
        & sc.exe start $ServiceName | Out-Null
        Show-Status
    }
    "Stop" {
        Stop-AgentService
        Show-Status
    }
    "Status" {
        Show-Status
    }
}
