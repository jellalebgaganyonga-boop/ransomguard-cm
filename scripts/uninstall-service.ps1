#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Uninstalls the RansomGuard-CM Agent Windows Service.

.DESCRIPTION
    Stops and removes the RansomGuard-CM Agent Windows Service.
    Does NOT delete data or log files.
#>

$ServiceName = "RansomGuard-CM"

$existingService = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue

if (-not $existingService) {
    Write-Host "Service '$ServiceName' is not installed." -ForegroundColor Yellow
    exit 0
}

Write-Host "Stopping service '$ServiceName'..."
Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 2

Write-Host "Removing service '$ServiceName'..."
sc.exe delete $ServiceName

if ($LASTEXITCODE -eq 0) {
    Write-Host "Service '$ServiceName' uninstalled successfully." -ForegroundColor Green
    Write-Host ""
    Write-Host "Note: Data and log files were NOT deleted."
    Write-Host "  Data: $env:ProgramData\RansomGuard-CM\data"
    Write-Host "  Logs: $env:ProgramData\RansomGuard-CM\logs"
} else {
    Write-Error "Failed to remove service."
    exit 1
}
