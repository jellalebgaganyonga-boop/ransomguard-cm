#Requires -RunAsAdministrator
<#
.SYNOPSIS
    RansomGuard-CM Agent installer — downloads, installs, and enrolls automatically.

.DESCRIPTION
    Run this script on any Windows machine in the Tailscale network to install
    the RansomGuard agent. It downloads the agent package from the GRID server,
    extracts it, configures appsettings.json with the provided OTP, and installs
    the agent as a Windows service.

.PARAMETER GridServer
    The GRID server address (hostname or Tailscale IP). The dashboard one-liner
    fills this in automatically from the host you opened the console on; set
    RG_GRID_SERVER to override when running the script by hand.

.PARAMETER OTP
    The enrollment OTP from the dashboard (required). Generate one via
    Dashboard > Agents > Add Agent.

.PARAMETER InstallPath
    Installation directory (default: C:\Program Files\RansomGuard-CM).

.EXAMPLE
    .\install.ps1 -OTP "abc123def456" -GridServer "grid.example.ts.net"
#>
param(
    [string]$OTP,
    [string]$GridServer = $(if ($env:RG_GRID_SERVER) { $env:RG_GRID_SERVER } else { "" }),
    [string]$InstallPath = "C:\Program Files\RansomGuard-CM"
)

# Allow OTP from environment variable (for one-liner download+run)
if (-not $OTP) { $OTP = $env:RG_OTP }
if (-not $GridServer) {
    Write-Host "ERROR: -GridServer is required (hostname or Tailscale IP of the GRID server)." -ForegroundColor Red
    Write-Host "  Use the one-liner shown in: Dashboard > Agents > Add Agent" -ForegroundColor Yellow
    exit 1
}
if (-not $OTP) {
    Write-Host "ERROR: OTP is required. Use -OTP parameter or set RG_OTP env var." -ForegroundColor Red
    Write-Host "  Generate an OTP from: Dashboard > Agents > Add Agent" -ForegroundColor Yellow
    exit 1
}

$ErrorActionPreference = "Stop"
$AgentVersion = "0.3.0"
$ServiceName = "RansomGuardAgent"
$PackageUrl = "https://${GridServer}:8443/agent/RansomGuard-Agent-v${AgentVersion}-win-x64.zip"
$DataDir = "C:\ProgramData\RansomGuard-CM"

Write-Host ""
Write-Host "=====================================================" -ForegroundColor Cyan
Write-Host "  RansomGuard-CM Agent Installer v${AgentVersion}" -ForegroundColor Cyan
Write-Host "=====================================================" -ForegroundColor Cyan
Write-Host ""

# ── Step 1: Pre-flight checks ──────────────────────────────
Write-Host "[1/6] Pre-flight checks..." -ForegroundColor Yellow

if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Host "ERROR: This script must be run as Administrator." -ForegroundColor Red
    exit 1
}

if (Get-Service -Name $ServiceName -ErrorAction SilentlyContinue) {
    Write-Host "WARNING: Service '$ServiceName' already exists. Stopping it..." -ForegroundColor Yellow
    Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 2
    sc.exe delete $ServiceName | Out-Null
    Start-Sleep -Seconds 1
}

# ── Step 2: Download agent package ──────────────────────────
Write-Host "[2/6] Downloading agent package from $PackageUrl ..." -ForegroundColor Yellow

$tempZip = Join-Path $env:TEMP "RansomGuard-Agent.zip"

# Skip certificate validation for self-signed certs on internal network
[System.Net.ServicePointManager]::ServerCertificateValidationCallback = { $true }
try {
    # Use .NET WebClient for better progress and TLS control
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    $wc = New-Object System.Net.WebClient
    $wc.DownloadFile($PackageUrl, $tempZip)
    Write-Host "  Downloaded $('{0:N1}' -f ((Get-Item $tempZip).Length / 1MB)) MB" -ForegroundColor Green
}
catch {
    Write-Host "ERROR: Failed to download agent package." -ForegroundColor Red
    Write-Host "  $_" -ForegroundColor Red
    Write-Host "  Make sure the GRID server is reachable at $GridServer" -ForegroundColor Red
    exit 1
}
finally {
    [System.Net.ServicePointManager]::ServerCertificateValidationCallback = $null
}

# ── Step 3: Extract ─────────────────────────────────────────
Write-Host "[3/6] Extracting to $InstallPath ..." -ForegroundColor Yellow

if (Test-Path $InstallPath) {
    Remove-Item -Path $InstallPath -Recurse -Force
}
New-Item -ItemType Directory -Path $InstallPath -Force | Out-Null
Expand-Archive -Path $tempZip -DestinationPath $InstallPath -Force
Remove-Item $tempZip -Force

Write-Host "  Extracted $(Get-ChildItem $InstallPath -Recurse -File | Measure-Object | Select-Object -ExpandProperty Count) files" -ForegroundColor Green

# ── Step 4: Configure appsettings.json ──────────────────────
Write-Host "[4/6] Configuring agent with OTP..." -ForegroundColor Yellow

$settingsPath = Join-Path $InstallPath "appsettings.json"
if (-not (Test-Path $settingsPath)) {
    Write-Host "ERROR: appsettings.json not found in package." -ForegroundColor Red
    exit 1
}

$settings = Get-Content $settingsPath -Raw | ConvertFrom-Json

# Set OTP for enrollment
$settings.Agent.Server.EnrollmentOtp = $OTP
$settings.Agent.Server.BaseUrl = "https://${GridServer}"

# Set hostname automatically
$settings.Agent.Identity.Hostname = $env:COMPUTERNAME

# Ensure data/log directories
$settings.Agent.Database.ConnectionString = "Data Source=$DataDir\data\agent.db"
$settings.Agent.Logging.LogFilePath = "$DataDir\logs\agent-.log"

# Write updated settings
$settings | ConvertTo-Json -Depth 10 | Set-Content $settingsPath -Encoding UTF8

# Create data directories
New-Item -ItemType Directory -Path "$DataDir\data" -Force | Out-Null
New-Item -ItemType Directory -Path "$DataDir\logs" -Force | Out-Null
New-Item -ItemType Directory -Path "$DataDir\keys" -Force | Out-Null

Write-Host "  OTP configured, hostname set to $env:COMPUTERNAME" -ForegroundColor Green

# ── Step 5: Install as Windows service ──────────────────────
Write-Host "[5/6] Installing Windows service '$ServiceName'..." -ForegroundColor Yellow

$exePath = Join-Path $InstallPath "RansomGuard.Agent.Service.exe"
if (-not (Test-Path $exePath)) {
    Write-Host "ERROR: Agent executable not found at $exePath" -ForegroundColor Red
    exit 1
}

sc.exe create $ServiceName `
    binPath= "`"$exePath`"" `
    start= auto `
    DisplayName= "RansomGuard-CM Agent" `
    obj= "LocalSystem" | Out-Null

sc.exe description $ServiceName "RansomGuard-CM endpoint detection and response agent" | Out-Null
sc.exe failure $ServiceName reset= 86400 actions= restart/60000/restart/120000/restart/300000 | Out-Null

Write-Host "  Service installed with auto-start and failure recovery" -ForegroundColor Green

# ── Step 6: Start service ───────────────────────────────────
Write-Host "[6/6] Starting agent service..." -ForegroundColor Yellow

Start-Service -Name $ServiceName
Start-Sleep -Seconds 3

$svc = Get-Service -Name $ServiceName
if ($svc.Status -eq "Running") {
    Write-Host ""
    Write-Host "=====================================================" -ForegroundColor Green
    Write-Host "  RansomGuard-CM Agent installed successfully!" -ForegroundColor Green
    Write-Host "=====================================================" -ForegroundColor Green
    Write-Host ""
    Write-Host "  Service:    $ServiceName (Running)" -ForegroundColor White
    Write-Host "  Install:    $InstallPath" -ForegroundColor White
    Write-Host "  Data:       $DataDir" -ForegroundColor White
    Write-Host "  Logs:       $DataDir\logs\" -ForegroundColor White
    Write-Host ""
    Write-Host "  The agent will enroll with the GRID server using the" -ForegroundColor White
    Write-Host "  provided OTP. Check the dashboard in ~60 seconds." -ForegroundColor White
    Write-Host ""
}
else {
    Write-Host "WARNING: Service installed but not running (status: $($svc.Status))." -ForegroundColor Yellow
    Write-Host "Check logs at: $DataDir\logs\" -ForegroundColor Yellow
}
