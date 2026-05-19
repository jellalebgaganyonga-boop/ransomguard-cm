param([int]$DurationSeconds = 60, [string]$OutputDir = "docs/benchmarks")
$ErrorActionPreference = "Stop"
Write-Host "=== Sprint 3 Performance Under Load ===" -ForegroundColor Cyan
if (-not (Test-Path $OutputDir)) { New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null }
$testZone = Join-Path $env:USERPROFILE "Desktop\RansomGuard-TestZone"
if (-not (Test-Path $testZone)) { New-Item -ItemType Directory -Path $testZone -Force | Out-Null }
Write-Host "Creating 100 test files..."
for ($i = 0; $i -lt 100; $i++) {
    Set-Content (Join-Path $testZone "file_$i.txt") -Value ("Patient record $i " + ("medical data " * 100))
}
Push-Location "C:/Projects/RansomGuard-CM/agent/src/RansomGuard.Agent.Service"
dotnet build -c Release --verbosity quiet 2>&1 | Out-Null
Pop-Location
$agentProcess = Start-Process -FilePath "dotnet" -ArgumentList "run","--project","C:/Projects/RansomGuard-CM/agent/src/RansomGuard.Agent.Service","-c","Release" -PassThru -WindowStyle Hidden
Write-Host "Agent PID: $($agentProcess.Id). Waiting 30s for baseline..."
Start-Sleep -Seconds 30
Write-Host "Sampling with concurrent file encryption..."
$samples = @()
$prevCpuTime = $null
$rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
for ($i = 0; $i -lt $DurationSeconds; $i++) {
    # Encrypt one file every 2 seconds
    if ($i % 2 -eq 0) {
        $idx = [Math]::Floor($i / 2)
        $target = Join-Path $testZone "file_$idx.txt"
        if (Test-Path $target) {
            $bytes = New-Object byte[] 5000
            $rng.GetBytes($bytes)
            [System.IO.File]::WriteAllBytes($target, $bytes)
        }
    }
    $p = $null
    if (-not $agentProcess.HasExited) { $p = Get-Process -Id $agentProcess.Id -ErrorAction SilentlyContinue }
    if ($p) {
        $cpuPercent = 0.0
        $currentCpuTime = $p.TotalProcessorTime.TotalMilliseconds
        if ($null -ne $prevCpuTime) {
            $cpuPercent = [Math]::Round(($currentCpuTime - $prevCpuTime) / (1000.0 * [Environment]::ProcessorCount) * 100, 2)
        }
        $prevCpuTime = $currentCpuTime
        $samples += [PSCustomObject]@{ Second=$i; RAM_MB=[Math]::Round($p.WorkingSet64/1MB,2); Handles=$p.HandleCount; CPU_Pct=$cpuPercent }
    }
    Start-Sleep -Seconds 1
}
if (-not $agentProcess.HasExited) { Stop-Process -Id $agentProcess.Id -Force -ErrorAction SilentlyContinue }
if ($samples.Count -eq 0) { Write-Host "No samples!" -ForegroundColor Red; exit 1 }
$maxRam = ($samples.RAM_MB | Measure-Object -Maximum).Maximum
$avgRam = [Math]::Round(($samples.RAM_MB | Measure-Object -Average).Average, 2)
$hVar = ($samples.Handles | Measure-Object -Maximum).Maximum - ($samples.Handles | Measure-Object -Minimum).Minimum
$cpuVals = $samples | Where-Object { $_.CPU_Pct -gt 0 } | Select-Object -ExpandProperty CPU_Pct
$avgCpu = if ($cpuVals) { [Math]::Round(($cpuVals | Measure-Object -Average).Average, 2) } else { 0 }
$rP = $maxRam -lt 200; $cP = $avgCpu -lt 15; $hP = $hVar -lt 100
Write-Host "`nMax RAM: $maxRam MB - $(if($rP){'PASS'}else{'FAIL'})" -ForegroundColor $(if($rP){'Green'}else{'Red'})
Write-Host "Avg CPU: ${avgCpu}% - $(if($cP){'PASS'}else{'FAIL'})" -ForegroundColor $(if($cP){'Green'}else{'Red'})
Write-Host "Handle var: $hVar - $(if($hP){'PASS'}else{'FAIL'})" -ForegroundColor $(if($hP){'Green'}else{'Red'})
$samples | Export-Csv (Join-Path $OutputDir "sprint-3-perf-under-load.csv") -NoTypeInformation
$d = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
$all = $rP -and $cP -and $hP
"# Sprint 3 Performance Under Load`n`n**Date:** $d`n**Duration:** ${DurationSeconds}s`n**Verdict:** $(if($all){'PASS'}else{'REVIEW'})`n`n| Metric | SLO | Measured | Status |`n|--------|-----|----------|--------|`n| Max RAM | 200 MB | $maxRam MB | $(if($rP){'PASS'}else{'FAIL'}) |`n| Avg RAM | - | $avgRam MB | - |`n| Avg CPU | 15% | ${avgCpu}% | $(if($cP){'PASS'}else{'FAIL'}) |`n| Handle var | 100 | $hVar | $(if($hP){'PASS'}else{'FAIL'}) |`n" | Out-File -Encoding utf8 (Join-Path $OutputDir "sprint-3-perf-under-load.md")
Write-Host "Report saved."
if ($all) { exit 0 } else { exit 1 }
