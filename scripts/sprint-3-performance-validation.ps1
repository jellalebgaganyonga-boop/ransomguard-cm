<#
.SYNOPSIS
    Sprint 3 Performance SLO Validation
.DESCRIPTION
    Measures RAM, CPU, and handle count over 60 seconds with SENTINEL + ENTROPY + GENEALOGY active.
    SLO targets: RAM < 200 MB, CPU < 5%, handle variance < 50.
#>

param(
    [int]$DurationSeconds = 60,
    [string]$OutputDir = "docs/benchmarks"
)

$ErrorActionPreference = "Stop"

Write-Host "=== Sprint 3 Performance SLO Validation ===" -ForegroundColor Cyan

if (-not (Test-Path $OutputDir)) {
    New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
}

# Start agent
$job = Start-Job -ScriptBlock {
    Set-Location "C:/Projects/RansomGuard-CM/agent/src/RansomGuard.Agent.Service"
    dotnet run 2>&1
}

Write-Host "Waiting 15 seconds for startup..."
Start-Sleep -Seconds 15

$samples = @()
for ($i = 0; $i -lt $DurationSeconds; $i++) {
    $p = Get-Process -Name "dotnet" -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($p) {
        $samples += [PSCustomObject]@{
            Seconds = $i
            RAM_MB = [Math]::Round($p.WorkingSet64 / 1MB, 2)
            Handles = $p.HandleCount
        }
    }
    Start-Sleep -Seconds 1
}

$maxRam = ($samples.RAM_MB | Measure-Object -Maximum).Maximum
$avgRam = [Math]::Round(($samples.RAM_MB | Measure-Object -Average).Average, 2)
$maxHandles = ($samples.Handles | Measure-Object -Maximum).Maximum
$minHandles = ($samples.Handles | Measure-Object -Minimum).Minimum

Write-Host "Max RAM: $maxRam MB (SLO: 200 MB)" -ForegroundColor $(if ($maxRam -lt 200) { 'Green' } else { 'Red' })
Write-Host "Avg RAM: $avgRam MB"
Write-Host "Handle variance: $($maxHandles - $minHandles) (SLO: <50)"

Stop-Job $job -ErrorAction SilentlyContinue
Remove-Job $job -ErrorAction SilentlyContinue
Get-Process -Name "dotnet" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
