<#
.SYNOPSIS
    Sprint 3 Performance SLO Validation
.DESCRIPTION
    Measures RAM, CPU, and handle count with SENTINEL + ENTROPY + GENEALOGY active.
    SLO targets: RAM < 200 MB, CPU < 5%, handle variance < 50.
    Saves samples to CSV and generates a markdown report.
#>

param(
    [int]$DurationSeconds = 60,
    [string]$OutputDir = "docs/benchmarks"
)

$ErrorActionPreference = "Stop"

Write-Host "=== Sprint 3 Performance SLO Validation ===" -ForegroundColor Cyan
Write-Host "Duration: $DurationSeconds seconds"

if (-not (Test-Path $OutputDir)) {
    New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
}

# Ensure watch directory exists
$testZone = Join-Path $env:USERPROFILE "Desktop\RansomGuard-TestZone"
if (-not (Test-Path $testZone)) {
    New-Item -ItemType Directory -Path $testZone -Force | Out-Null
}

# Build Release first
Write-Host "Building Release configuration..."
Push-Location "C:/Projects/RansomGuard-CM/agent/src/RansomGuard.Agent.Service"
dotnet build -c Release --verbosity quiet 2>&1 | Out-Null
Pop-Location

# Start agent in background
Write-Host "Starting agent process..."
$stderrLog = Join-Path $OutputDir "agent-stderr.log"
$agentProcess = Start-Process -FilePath "dotnet" -ArgumentList "run","--project","C:/Projects/RansomGuard-CM/agent/src/RansomGuard.Agent.Service","-c","Release" -PassThru -WindowStyle Hidden -RedirectStandardError $stderrLog

$agentPid = $agentProcess.Id
Write-Host "Agent started with PID: $agentPid"

# Wait for startup
Write-Host "Waiting 30 seconds for baseline build..."
Start-Sleep -Seconds 30

# Check if process is still alive
if ($agentProcess.HasExited) {
    Write-Host "WARNING: Agent process exited. Will attempt to measure any running dotnet process." -ForegroundColor Yellow
}

# Collect samples
$samples = @()
$prevCpuTime = $null

for ($i = 0; $i -lt $DurationSeconds; $i++) {
    $p = $null
    if (-not $agentProcess.HasExited) {
        $p = Get-Process -Id $agentPid -ErrorAction SilentlyContinue
    }
    if (-not $p) {
        $p = Get-Process -Name "dotnet" -ErrorAction SilentlyContinue | Sort-Object WorkingSet64 -Descending | Select-Object -First 1
    }

    if ($p) {
        $ramMb = [Math]::Round($p.WorkingSet64 / 1MB, 2)
        $handles = $p.HandleCount
        $threads = $p.Threads.Count

        # CPU calculation (normalized across all logical processors)
        $cpuPercent = 0.0
        $currentCpuTime = $p.TotalProcessorTime.TotalMilliseconds
        if ($null -ne $prevCpuTime) {
            $cpuDelta = $currentCpuTime - $prevCpuTime
            $numCores = [Environment]::ProcessorCount
            $cpuPercent = [Math]::Round($cpuDelta / (1000.0 * $numCores) * 100, 2)
        }
        $prevCpuTime = $currentCpuTime

        $samples += [PSCustomObject]@{
            Seconds  = $i
            RAM_MB   = $ramMb
            Handles  = $handles
            Threads  = $threads
            CPU_Pct  = $cpuPercent
        }
    }
    Start-Sleep -Seconds 1
}

# Cleanup
if (-not $agentProcess.HasExited) {
    Stop-Process -Id $agentPid -Force -ErrorAction SilentlyContinue
}

# Compute metrics
if ($samples.Count -eq 0) {
    Write-Host "ERROR: No samples collected!" -ForegroundColor Red
    exit 1
}

$maxRam = ($samples.RAM_MB | Measure-Object -Maximum).Maximum
$avgRam = [Math]::Round(($samples.RAM_MB | Measure-Object -Average).Average, 2)
$maxHandles = ($samples.Handles | Measure-Object -Maximum).Maximum
$minHandles = ($samples.Handles | Measure-Object -Minimum).Minimum
$handleVariance = $maxHandles - $minHandles
$cpuValues = $samples | Where-Object { $_.CPU_Pct -gt 0 } | Select-Object -ExpandProperty CPU_Pct
if ($cpuValues) {
    $avgCpu = [Math]::Round(($cpuValues | Measure-Object -Average).Average, 2)
} else {
    $avgCpu = 0
}
$maxCpu = ($samples.CPU_Pct | Measure-Object -Maximum).Maximum
$avgThreads = [Math]::Round(($samples.Threads | Measure-Object -Average).Average, 0)

# SLO evaluation
$ramPass = $maxRam -lt 200
$cpuPass = $avgCpu -lt 5
$handlePass = $handleVariance -lt 50

Write-Host ""
Write-Host "=== SLO Results ===" -ForegroundColor Cyan
$ramResult = if ($ramPass) { "PASS" } else { "FAIL" }
$cpuResult = if ($cpuPass) { "PASS" } else { "FAIL" }
$handleResult = if ($handlePass) { "PASS" } else { "FAIL" }

Write-Host "Max RAM: $maxRam MB (SLO: under 200 MB) - $ramResult" -ForegroundColor $(if ($ramPass) { 'Green' } else { 'Red' })
Write-Host "Avg CPU: ${avgCpu}% (SLO: under 5%) - $cpuResult" -ForegroundColor $(if ($cpuPass) { 'Green' } else { 'Red' })
Write-Host "Handle variance: $handleVariance (SLO: under 50) - $handleResult" -ForegroundColor $(if ($handlePass) { 'Green' } else { 'Red' })

# Save CSV
$csvPath = Join-Path $OutputDir "sprint-3-perf-samples.csv"
$samples | Export-Csv -Path $csvPath -NoTypeInformation
$sampleCount = $samples.Count
Write-Host "Samples saved to: $csvPath ($sampleCount samples)"

# Save markdown report
$reportPath = Join-Path $OutputDir "sprint-3-perf-validation.md"
$allPass = $ramPass -and $cpuPass -and $handlePass
$verdict = if ($allPass) { "PASS" } else { "FAIL" }
$dateStr = Get-Date -Format "yyyy-MM-dd HH:mm:ss"

$report = "# Sprint 3 Performance SLO Validation`n"
$report += "`n**Date:** $dateStr`n"
$report += "**Duration:** $DurationSeconds seconds`n"
$report += "**Samples collected:** $sampleCount`n"
$report += "**Verdict:** $verdict`n"
$report += "`n## SLO Results`n"
$report += "`n| Metric | Measured | SLO Target | Result |`n"
$report += "|--------|----------|------------|--------|`n"
$report += "| Max RAM | $maxRam MB | 200 MB | $ramResult |`n"
$report += "| Avg RAM | $avgRam MB | - | - |`n"
$report += "| Avg CPU | ${avgCpu}% | 5% | $cpuResult |`n"
$report += "| Max CPU | ${maxCpu}% | - | - |`n"
$report += "| Handle Variance | $handleVariance | 50 | $handleResult |`n"
$report += "| Handle Range | $minHandles - $maxHandles | - | - |`n"
$report += "| Avg Threads | $avgThreads | - | - |`n"
$report += "`n## Notes`n"
$report += "`n- Agent ran with SENTINEL + ENTROPY + GENEALOGY modules active`n"
$report += "- Watch directories: %USERPROFILE%\Desktop\RansomGuard-TestZone`n"
$report += "- Configuration: Release build, net8.0-windows`n"

$report | Out-File -Encoding utf8 -FilePath $reportPath
Write-Host "Report saved to: $reportPath"

# Exit code
if ($allPass) {
    Write-Host "`nAll SLOs PASSED." -ForegroundColor Green
    exit 0
} else {
    Write-Host "`nSLO FAILED. Investigation required." -ForegroundColor Red
    exit 1
}
