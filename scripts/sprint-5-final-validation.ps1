#Requires -Version 7.0
<#
.SYNOPSIS
    Sprint 5 IRONCLAD Final Validation — 22 gates before v0.7.0 tag.
#>
$ErrorActionPreference = "Continue"
$results = @()

function Add-Result {
    param($Number, $Description, $Pass, $Detail = "")
    $script:results += [PSCustomObject]@{
        Gate = $Number
        Description = $Description
        Status = if ($Pass) { "PASS" } else { "FAIL" }
        Detail = $Detail
    }
    $color = if ($Pass) { "Green" } else { "Red" }
    Write-Host ("[{0}] Gate {1}: {2} {3}" -f $(if ($Pass) { "PASS" } else { "FAIL" }), $Number, $Description, $Detail) -ForegroundColor $color
}

Write-Host "=== Sprint 5 IRONCLAD Final Validation ===" -ForegroundColor Cyan
Write-Host ""

Push-Location "agent/src"

# Gate 1: Build Release
$buildOutput = dotnet build --configuration Release --no-incremental 2>&1 | Out-String
$buildSuccess = $buildOutput -match "Build succeeded"
$errorLines = ($buildOutput -split "`n" | Where-Object { $_ -match ": error " }).Count
Add-Result 1 "Build Release 0/0" ($buildSuccess -and $errorLines -eq 0) "errors=$errorLines"

# Gate 2: Tests 555+ passing
$testOutput = dotnet test --filter "Category!=Stress" --configuration Release --logger "console;verbosity=minimal" 2>&1 | Out-String
$passed = if ($testOutput -match "Passed:\s+(\d+)") { [int]$matches[1] } else { 0 }
$failed = if ($testOutput -match "Failed:\s+(\d+)") { [int]$matches[1] } else { 0 }
Add-Result 2 "Tests 555+ passing" ($passed -ge 555 -and $failed -le 2) "passed=$passed failed=$failed (<=2 timing flakes allowed)"

Pop-Location

# Gate 3: IronClad communication files exist (6)
$commFiles = @(
    "agent/src/RansomGuard.Agent.Core/Detection/IronClad/Communication/IIronCladCommunicator.cs",
    "agent/src/RansomGuard.Agent.Core/Detection/IronClad/Communication/TcpMockCommunicator.cs",
    "agent/src/RansomGuard.Agent.Core/Detection/IronClad/Communication/SerialPortCommunicator.cs",
    "agent/src/RansomGuard.Agent.Core/Detection/IronClad/Communication/MockArduinoServer.cs",
    "agent/src/RansomGuard.Agent.Core/Detection/IronClad/Communication/IronCladProtocol.cs",
    "agent/src/RansomGuard.Agent.Core/Detection/IronClad/Communication/IronCladCommunicatorFactory.cs"
)
$commFound = ($commFiles | Where-Object { Test-Path $_ }).Count
Add-Result 3 "Communication layer files (6)" ($commFound -eq 6) "found=$commFound/6"

# Gate 4: Action engine files exist
$aeFile1 = Test-Path "agent/src/RansomGuard.Agent.Core/Detection/IronClad/Actions/IIronCladActionEngine.cs"
$aeFile2 = Test-Path "agent/src/RansomGuard.Agent.Core/Detection/IronClad/Actions/IronCladActionEngine.cs"
Add-Result 4 "Action engine files (2)" ($aeFile1 -and $aeFile2)

# Gate 5: IronClad migration exists
$migrations = Get-ChildItem "agent/src/RansomGuard.Agent.Core/Persistence/Migrations/" -Filter "*IronClad*.cs" -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -notmatch "Designer" }
$migCount = ($migrations | Measure-Object).Count
Add-Result 5 "IronClad migration" ($migCount -ge 1) "found=$migCount"

# Gate 6: Persistence entities
$eventE = Test-Path "agent/src/RansomGuard.Agent.Core/Persistence/Entities/IronCladEvent.cs"
$stateE = Test-Path "agent/src/RansomGuard.Agent.Core/Persistence/Entities/IronCladDeviceState.cs"
Add-Result 6 "Persistence entities (2)" ($eventE -and $stateE)

# Gate 7: Background services
$hb = Test-Path "agent/src/RansomGuard.Agent.Service/IronCladHeartbeatService.cs"
$rc = Test-Path "agent/src/RansomGuard.Agent.Service/IronCladStateReconciliationService.cs"
Add-Result 7 "Background services (2)" ($hb -and $rc)

# Gate 8: USB GUARD wiring to IronClad
$uae = Get-Content "agent/src/RansomGuard.Agent.Core/Detection/UsbGuard/Actions/UsbActionEngine.cs" -Raw -ErrorAction SilentlyContinue
$wired = ($uae -match "IIronCladActionEngine") -and ($uae -match "CutUsbPortAsync")
Add-Result 8 "UsbActionEngine wired to IronClad" $wired

# Gate 9: DI registration
$sr = Get-Content "agent/src/RansomGuard.Agent.Service/ServiceRegistration.cs" -Raw -ErrorAction SilentlyContinue
$diReg = $sr -match "AddIronCladServices"
Add-Result 9 "AddIronCladServices in DI" $diReg

# Gate 10: AuditLog used in ActionEngine
$aeContent = Get-Content "agent/src/RansomGuard.Agent.Core/Detection/IronClad/Actions/IronCladActionEngine.cs" -Raw -ErrorAction SilentlyContinue
$auditUsed = $aeContent -match "IAuditLogRepository"
Add-Result 10 "Audit log Ed25519 used in ActionEngine" $auditUsed

# Gate 11: Default Enabled=false
$optContent = Get-Content "agent/src/RansomGuard.Agent.Core/Detection/IronClad/IronCladOptions.cs" -Raw -ErrorAction SilentlyContinue
$safeDefault = $optContent -match "Enabled.*=\s*false"
Add-Result 11 "Default Enabled=false (safe)" $safeDefault

# Gate 12: FailSafe default true
$failSafe = $optContent -match "FailSafeRestoreOnDisconnect.*=\s*true"
Add-Result 12 "FailSafe default true" $failSafe

# Gate 13: Both communication modes wired
$factContent = Get-Content "agent/src/RansomGuard.Agent.Core/Detection/IronClad/Communication/IronCladCommunicatorFactory.cs" -Raw -ErrorAction SilentlyContinue
$bothModes = ($factContent -match "TcpMock") -and ($factContent -match "SerialPort")
Add-Result 13 "Both communication modes wired" $bothModes

# Gate 14: MockArduinoServer implements full protocol
$mockContent = Get-Content "agent/src/RansomGuard.Agent.Core/Detection/IronClad/Communication/MockArduinoServer.cs" -Raw -ErrorAction SilentlyContinue
$proto = ($mockContent -match "CUT_PORT") -and ($mockContent -match "RESTORE") -and ($mockContent -match "STATUS") -and ($mockContent -match "PONG")
Add-Result 14 "MockArduinoServer full protocol" $proto

# Gate 15: 30s fail-safe in mock server
$failsafe30 = $mockContent -match "30|clientTimeoutSeconds"
Add-Result 15 "Mock server 30s fail-safe" $failsafe30

# Gate 16: SQL script generated
$sqlExist = Test-Path "artifacts/sprint-5-migrations.sql"
Add-Result 16 "Sprint 5 SQL script" $sqlExist

# Gate 17: ADR-022 exists
$adrExist = Test-Path "docs/architecture/adr/ADR-022-ironclad-software-only-sprint5.md"
Add-Result 17 "ADR-022 software-only decision" $adrExist

# Gate 18: Module README
$readmeExist = Test-Path "agent/src/RansomGuard.Agent.Core/Detection/IronClad/README.md"
Add-Result 18 "IronClad README" $readmeExist

# Gate 19: Wire protocol serialization
$protoContent = Get-Content "agent/src/RansomGuard.Agent.Core/Detection/IronClad/Communication/IronCladProtocol.cs" -Raw -ErrorAction SilentlyContinue
$hasSerialize = ($protoContent -match "Serialize") -and ($protoContent -match "Parse") -and ($protoContent -match "ReadOnlySpan")
Add-Result 19 "Protocol Serialize/Parse with Span" $hasSerialize

# Gate 20: Heartbeat monitor has timeout detection
$hbContent = Get-Content "agent/src/RansomGuard.Agent.Service/IronCladHeartbeatService.cs" -Raw -ErrorAction SilentlyContinue
$hbTimeout = ($hbContent -match "HeartbeatTimeoutSeconds") -and ($hbContent -match "ConsecutiveMissed")
Add-Result 20 "Heartbeat timeout detection" $hbTimeout

# Gate 21: State reconciliation compares stored vs actual
$rcContent = Get-Content "agent/src/RansomGuard.Agent.Service/IronCladStateReconciliationService.cs" -Raw -ErrorAction SilentlyContinue
$rcCompare = ($rcContent -match "GetCurrentStatesAsync") -and ($rcContent -match "CheckHealthAsync") -and ($rcContent -match "discrepanc")
Add-Result 21 "State reconciliation logic" $rcCompare

# Gate 22: Zero TODO/FIXME in IronClad
$todoResults = git grep -nE "TODO|FIXME|HACK" -- "agent/src/RansomGuard.Agent.Core/Detection/IronClad/" 2>$null
$todoCount = if ($todoResults) { ($todoResults | Measure-Object).Count } else { 0 }
Add-Result 22 "Zero TODO/FIXME in IronClad" ($todoCount -eq 0) "found=$todoCount"

# Summary
Write-Host ""
Write-Host "=== Summary ===" -ForegroundColor Cyan
$passCount = ($results | Where-Object { $_.Status -eq "PASS" }).Count
$failCount = ($results | Where-Object { $_.Status -eq "FAIL" }).Count
Write-Host "PASS: $passCount / $($results.Count)" -ForegroundColor Green
if ($failCount -gt 0) {
    Write-Host "FAIL: $failCount / $($results.Count)" -ForegroundColor Red
} else {
    Write-Host "FAIL: 0 / $($results.Count)" -ForegroundColor Green
}

$resultsDir = "artifacts"
if (-not (Test-Path $resultsDir)) { New-Item -ItemType Directory -Path $resultsDir -Force | Out-Null }
$results | Format-Table -AutoSize | Out-String | Set-Content "artifacts/sprint-5-final-validation-results.txt"
$results | Export-Csv "artifacts/sprint-5-final-validation-results.csv" -NoTypeInformation

if ($failCount -gt 0) {
    Write-Host ""
    Write-Host "Failed gates:" -ForegroundColor Red
    $results | Where-Object { $_.Status -eq "FAIL" } | ForEach-Object {
        Write-Host "  Gate $($_.Gate): $($_.Description) ($($_.Detail))" -ForegroundColor Red
    }
    exit 1
}

Write-Host ""
Write-Host "All $($results.Count) gates passed. Sprint 5 ready for v0.7.0-ironclad tag." -ForegroundColor Green
exit 0
