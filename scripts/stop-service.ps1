#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Stops the RansomGuard-CM Agent Windows Service.
#>

$ServiceName = "RansomGuard-CM"

$service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue

if (-not $service) {
    Write-Error "Service '$ServiceName' is not installed."
    exit 1
}

if ($service.Status -eq 'Stopped') {
    Write-Host "Service '$ServiceName' is already stopped." -ForegroundColor Yellow
    exit 0
}

Write-Host "Stopping service '$ServiceName'..."
Stop-Service -Name $ServiceName -Force

$service.Refresh()
if ($service.Status -eq 'Stopped') {
    Write-Host "Service '$ServiceName' stopped successfully." -ForegroundColor Green
} else {
    Write-Error "Service failed to stop."
    exit 1
}
