#Requires -Version 7.0
<#
.SYNOPSIS
    Sprint 4 migration rollback test — apply, rollback, re-apply cycle.
.DESCRIPTION
    Tests that all Sprint 4 migrations can be applied, rolled back individually
    in reverse order to the Sprint 3 baseline, and then re-applied forward,
    producing an identical schema each time.
.NOTES
    Run from repository root: .\scripts\test-migration-rollback-sprint-4.ps1
#>
[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot   = Split-Path -Parent $PSScriptRoot
$coreProj   = Join-Path $repoRoot 'agent/src/RansomGuard.Agent.Core'
$testDbPath = Join-Path $repoRoot 'artifacts/test-rollback-sprint4.db'
$connStr    = "Data Source=$testDbPath"
$passed     = 0
$failed     = 0

# Sprint 3 boundary (last Sprint 3 migration)
$sprint3Last = '20260518100157_AddGenealogyRecord'

# Sprint 4 migrations in order
$sprint4Migrations = @(
    '20260523010000_AddUsbWhitelist',
    '20260523010100_AddUsbPolicy',
    '20260523010200_AddUsbConnectionLog',
    '20260523010300_AddUsbScanResult',
    '20260523010400_AddUsbAlert',
    '20260523010500_AddQuarantine',
    '20260523020000_AddExfilAlert',
    '20260523020100_AddNetworkBaseline',
    '20260523020200_AddNetworkBaselineMetric',
    '20260523030000_AddIndicatorRemovalEvent'
)

# Sprint 4 tables (expected after full apply)
$sprint4Tables = @(
    'UsbWhitelistEntries', 'UsbPolicies', 'UsbConnectionLogs',
    'UsbScanResults', 'UsbAlerts', 'QuarantinedFiles',
    'ExfilAlerts', 'NetworkBaselines', 'NetworkBaselineMetrics',
    'IndicatorRemovalEvents'
)

# Sprint 3 tables (baseline)
$sprint3Tables = @(
    'AgentStates', 'Alerts', 'AuditLogs', 'CanaryAlerts',
    'DetectionEvents', 'EntropyAlerts', 'EntropyBaselines',
    'GenealogyRecords', 'SentinelCanaries'
)

function Write-TestResult {
    param([string]$Name, [bool]$Pass, [string]$Detail = '')
    if ($Pass) {
        Write-Host "  [PASS] $Name" -ForegroundColor Green
        $script:passed++
    } else {
        Write-Host "  [FAIL] $Name — $Detail" -ForegroundColor Red
        $script:failed++
    }
}

function Get-UserTables {
    param([string]$DbPath)
    $tables = sqlite3 $DbPath "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE '__EF%' AND name NOT LIKE 'sqlite_%' ORDER BY name;"
    return $tables
}

function Get-MigrationCount {
    param([string]$DbPath)
    $count = sqlite3 $DbPath "SELECT COUNT(*) FROM __EFMigrationsHistory;"
    return [int]$count
}

function Run-EfUpdate {
    param([string]$TargetMigration)
    $env:ConnectionStrings__AgentDb = $connStr
    $result = dotnet ef database update $TargetMigration `
        --project $coreProj `
        --startup-project $coreProj `
        --connection $connStr 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "EF update to '$TargetMigration' failed: $result"
    }
}

# ─── Cleanup ───
if (Test-Path $testDbPath) { Remove-Item $testDbPath -Force }

Write-Host "`n=== Sprint 4 Migration Rollback Test ===" -ForegroundColor Cyan
Write-Host "Test database: $testDbPath`n"

# ─── Step 1: Apply ALL migrations (Sprint 1 through Sprint 4) ───
Write-Host "Step 1: Apply all migrations (Sprint 1-4)..." -ForegroundColor Yellow
try {
    Run-EfUpdate ''
    $tables1 = Get-UserTables $testDbPath
    $migCount = Get-MigrationCount $testDbPath

    Write-TestResult 'All 15 migrations applied' ($migCount -eq 15) "Got $migCount"

    $allExpected = $sprint3Tables + $sprint4Tables
    $missingTables = $allExpected | Where-Object { $_ -notin $tables1 }
    Write-TestResult 'All 19 tables present' ($missingTables.Count -eq 0) "Missing: $($missingTables -join ', ')"

    # Capture schema fingerprint for later comparison
    $schema1 = sqlite3 $testDbPath "SELECT sql FROM sqlite_master WHERE type IN ('table','index') AND name NOT LIKE 'sqlite_%' ORDER BY name;" | Out-String
} catch {
    Write-TestResult 'Apply all migrations' $false $_.Exception.Message
}

# ─── Step 2: Roll back each Sprint 4 migration in reverse order ───
Write-Host "`nStep 2: Rollback Sprint 4 migrations in reverse order..." -ForegroundColor Yellow

$reverseTargets = @($sprint3Last) + ($sprint4Migrations | Select-Object -First ($sprint4Migrations.Count - 1) | Sort-Object -Descending)
# Rolling back means targeting the migration BEFORE the one we want to remove
# Sprint4[9] -> target Sprint4[8], ..., Sprint4[0] -> target Sprint3Last

for ($i = $sprint4Migrations.Count - 1; $i -ge 0; $i--) {
    $migToRemove = $sprint4Migrations[$i]
    if ($i -eq 0) {
        $target = $sprint3Last
    } else {
        $target = $sprint4Migrations[$i - 1]
    }
    try {
        Run-EfUpdate $target
        $currentCount = Get-MigrationCount $testDbPath
        $expectedCount = 5 + $i  # 5 Sprint 1-3 + remaining Sprint 4
        Write-TestResult "Rollback $migToRemove" ($currentCount -eq $expectedCount) "Expected $expectedCount migrations, got $currentCount"
    } catch {
        Write-TestResult "Rollback $migToRemove" $false $_.Exception.Message
    }
}

# ─── Step 3: Verify schema is back to Sprint 3 state ───
Write-Host "`nStep 3: Verify Sprint 3 baseline schema..." -ForegroundColor Yellow
try {
    $tables2 = Get-UserTables $testDbPath
    $migCount2 = Get-MigrationCount $testDbPath

    Write-TestResult 'Only 5 Sprint 1-3 migrations remain' ($migCount2 -eq 5) "Got $migCount2"

    $sprint4Present = $sprint4Tables | Where-Object { $_ -in $tables2 }
    Write-TestResult 'No Sprint 4 tables remain' ($sprint4Present.Count -eq 0) "Still present: $($sprint4Present -join ', ')"

    $sprint3Missing = $sprint3Tables | Where-Object { $_ -notin $tables2 }
    Write-TestResult 'All Sprint 3 tables preserved' ($sprint3Missing.Count -eq 0) "Missing: $($sprint3Missing -join ', ')"
} catch {
    Write-TestResult 'Verify Sprint 3 baseline' $false $_.Exception.Message
}

# ─── Step 4: Re-apply all migrations forward ───
Write-Host "`nStep 4: Re-apply all migrations forward..." -ForegroundColor Yellow
try {
    Run-EfUpdate ''
    $tables3 = Get-UserTables $testDbPath
    $migCount3 = Get-MigrationCount $testDbPath

    Write-TestResult 'All 15 migrations re-applied' ($migCount3 -eq 15) "Got $migCount3"

    $allExpected = $sprint3Tables + $sprint4Tables
    $missingTables3 = $allExpected | Where-Object { $_ -notin $tables3 }
    Write-TestResult 'All 19 tables present after re-apply' ($missingTables3.Count -eq 0) "Missing: $($missingTables3 -join ', ')"
} catch {
    Write-TestResult 'Re-apply all migrations' $false $_.Exception.Message
}

# ─── Step 5: Verify schema matches initial apply ───
Write-Host "`nStep 5: Verify schema matches initial apply..." -ForegroundColor Yellow
try {
    $schema2 = sqlite3 $testDbPath "SELECT sql FROM sqlite_master WHERE type IN ('table','index') AND name NOT LIKE 'sqlite_%' ORDER BY name;" | Out-String
    Write-TestResult 'Schema fingerprint matches' ($schema1 -eq $schema2) 'Schema drift detected between apply/rollback/re-apply'
} catch {
    Write-TestResult 'Schema comparison' $false $_.Exception.Message
}

# ─── Cleanup ───
Write-Host "`nStep 6: Cleanup..." -ForegroundColor Yellow
if (Test-Path $testDbPath) { Remove-Item $testDbPath -Force }
Write-TestResult 'Test database cleaned up' (-not (Test-Path $testDbPath))

# ─── Summary ───
$total = $passed + $failed
Write-Host "`n=== Results ===" -ForegroundColor Cyan
Write-Host "  Passed: $passed / $total"
Write-Host "  Failed: $failed / $total"
if ($failed -eq 0) {
    Write-Host "`n  OVERALL: PASS" -ForegroundColor Green
} else {
    Write-Host "`n  OVERALL: FAIL" -ForegroundColor Red
    exit 1
}
