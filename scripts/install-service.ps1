#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Installs the RansomGuard-CM Agent as a Windows Service.

.DESCRIPTION
    Creates and configures the RansomGuard-CM Agent Windows Service with
    automatic restart on failure and delayed auto-start.

.PARAMETER ServicePath
    Path to the RansomGuard.Agent.Service.exe executable.
    Defaults to the publish output in the current directory.
#>
param(
    [string]$ServicePath = (Join-Path $PSScriptRoot "..\agent\src\RansomGuard.Agent.Service\bin\Release\net8.0\win-x64\publish\RansomGuard.Agent.Service.exe")
)

$ServiceName = "RansomGuard-CM"
$DisplayName = "RansomGuard-CM Agent"
$Description = "RansomGuard-CM Anti-Ransomware Agent - Real-time file system monitoring and ransomware detection for healthcare facilities."

# Validate executable exists
if (-not (Test-Path $ServicePath)) {
    Write-Error "Service executable not found at: $ServicePath"
    Write-Host "Please build and publish the agent first:"
    Write-Host "  dotnet publish RansomGuard.Agent.Service -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true"
    exit 1
}

$ServicePath = Resolve-Path $ServicePath

# Check if service already exists
$existingService = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($existingService) {
    Write-Host "Service '$ServiceName' already exists. Stopping and removing..."
    Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
    sc.exe delete $ServiceName | Out-Null
    Start-Sleep -Seconds 2
}

# Create the service
Write-Host "Installing service '$DisplayName'..."
sc.exe create $ServiceName binPath= "$ServicePath" start= delayed-auto DisplayName= "$DisplayName"

if ($LASTEXITCODE -ne 0) {
    Write-Error "Failed to create service."
    exit 1
}

# Set description
sc.exe description $ServiceName "$Description"

# Configure failure recovery
# First failure: restart after 5 seconds
# Second failure: restart after 10 seconds
# Subsequent failures: restart after 60 seconds
# Reset failure counter after 1 day (86400 seconds)
sc.exe failure $ServiceName reset= 86400 actions= restart/5000/restart/10000/restart/60000
sc.exe failureflag $ServiceName 1

# Security descriptor: prevent non-admin users from stopping the service
# SY=SYSTEM, BA=Administrators, IU=Interactive Users (read only), SU=Service Users (read only)
sc.exe sdset $ServiceName "D:(A;;CCLCSWRPWPDTLOCRRC;;;SY)(A;;CCDCLCSWRPWPDTLOCRSDRCWDWO;;;BA)(A;;CCLCSWLOCRRC;;;IU)(A;;CCLCSWLOCRRC;;;SU)"

# Ensure ProgramData directories exist
$dataDir = Join-Path $env:ProgramData "RansomGuard-CM\data"
$logDir = Join-Path $env:ProgramData "RansomGuard-CM\logs"

if (-not (Test-Path $dataDir)) { New-Item -Path $dataDir -ItemType Directory -Force | Out-Null }
if (-not (Test-Path $logDir)) { New-Item -Path $logDir -ItemType Directory -Force | Out-Null }

Write-Host ""
Write-Host "Service '$DisplayName' installed successfully." -ForegroundColor Green
Write-Host "  Service Name : $ServiceName"
Write-Host "  Executable   : $ServicePath"
Write-Host "  Startup Type : Automatic (Delayed Start)"
Write-Host "  Data Dir     : $dataDir"
Write-Host "  Log Dir      : $logDir"
Write-Host ""
Write-Host "To start the service: .\start-service.ps1"
