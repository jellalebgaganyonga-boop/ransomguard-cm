#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Starts the RansomGuard-CM Agent Windows Service.
#>

$ServiceName = "RansomGuard-CM"

$service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue

if (-not $service) {
    Write-Error "Service '$ServiceName' is not installed. Run install-service.ps1 first."
    exit 1
}

if ($service.Status -eq 'Running') {
    Write-Host "Service '$ServiceName' is already running." -ForegroundColor Yellow
    exit 0
}

Write-Host "Starting service '$ServiceName'..."
Start-Service -Name $ServiceName

$service.Refresh()
if ($service.Status -eq 'Running') {
    Write-Host "Service '$ServiceName' started successfully." -ForegroundColor Green
} else {
    Write-Error "Service failed to start. Check Windows Event Log for details."
    exit 1
}
