#Requires -Version 7.0
<#
.SYNOPSIS
    Tests Sprint 4 migration upgrade on existing v0.5.1 (Sprint 3) database.
.DESCRIPTION
    Creates a simulated v0.5.1 database with Sprint 1-3 schema and sample data,
    then applies Sprint 4 migrations and verifies no data loss occurs.
.NOTES
    Run from repository root: .\scripts\test-migration-on-v0.5.1-database.ps1
#>
[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot   = Split-Path -Parent $PSScriptRoot
$coreProj   = Join-Path $repoRoot 'agent/src/RansomGuard.Agent.Core'
$testDbPath = Join-Path $repoRoot 'artifacts/test-v051-upgrade.db'
$connStr    = "Data Source=$testDbPath"
$passed     = 0
$failed     = 0

# Sprint 3 boundary
$sprint3Last = '20260518100157_AddGenealogyRecord'

# Sprint 3 tables with expected sample row counts
$sprint3TablesWithCounts = @{
    'AgentStates'      = 1
    'Alerts'           = 3
    'AuditLogs'        = 5
    'CanaryAlerts'     = 2
    'DetectionEvents'  = 4
    'EntropyAlerts'    = 2
    'EntropyBaselines' = 3
    'GenealogyRecords' = 2
    'SentinelCanaries' = 3
}

# Sprint 4 new tables (should be empty after migration)
$sprint4Tables = @(
    'UsbWhitelistEntries', 'UsbPolicies', 'UsbConnectionLogs',
    'UsbScanResults', 'UsbAlerts', 'QuarantinedFiles',
    'ExfilAlerts', 'NetworkBaselines', 'NetworkBaselineMetrics',
    'IndicatorRemovalEvents'
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

function Run-EfUpdate {
    param([string]$TargetMigration)
    $result = dotnet ef database update $TargetMigration `
        --project $coreProj `
        --startup-project $coreProj `
        --connection $connStr 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "EF update to '$TargetMigration' failed: $result"
    }
}

function Get-RowCount {
    param([string]$DbPath, [string]$Table)
    $count = sqlite3 $DbPath "SELECT COUNT(*) FROM ""$Table"";"
    return [int]$count
}

# ─── Cleanup ───
if (Test-Path $testDbPath) { Remove-Item $testDbPath -Force }

Write-Host "`n=== v0.5.1 Database Upgrade Test ===" -ForegroundColor Cyan
Write-Host "Test database: $testDbPath`n"

# ─── Step 1: Create v0.5.1 (Sprint 3) database ───
Write-Host "Step 1: Create v0.5.1 database with Sprint 1-3 schema..." -ForegroundColor Yellow
try {
    Run-EfUpdate $sprint3Last

    $migCount = sqlite3 $testDbPath "SELECT COUNT(*) FROM __EFMigrationsHistory;"
    Write-TestResult 'Sprint 1-3 schema created (5 migrations)' ([int]$migCount -eq 5) "Got $migCount"
} catch {
    Write-TestResult 'Create Sprint 3 schema' $false $_.Exception.Message
    Write-Host "`nFATAL: Cannot continue without base schema." -ForegroundColor Red
    exit 1
}

# ─── Step 2: Seed sample data ───
Write-Host "`nStep 2: Seed v0.5.1 sample data..." -ForegroundColor Yellow
try {
    $guid1 = [guid]::NewGuid().ToString()
    $guid2 = [guid]::NewGuid().ToString()
    $guid3 = [guid]::NewGuid().ToString()
    $now = (Get-Date).ToUniversalTime().ToString('yyyy-MM-dd HH:mm:ss')

    # AgentStates
    sqlite3 $testDbPath "INSERT INTO AgentStates (Id, State, AgentVersion, Hostname, CreatedAt, UpdatedAt) VALUES ('$guid1','Running','0.5.1','TESTHOST','$now','$now');"

    # Alerts
    foreach ($i in 1..3) {
        $g = [guid]::NewGuid().ToString()
        sqlite3 $testDbPath "INSERT INTO Alerts (Id, Severity, Title, Description, Timestamp, CreatedAt, UpdatedAt, Acknowledged) VALUES ('$g','High','Test Alert $i','Test description','$now','$now','$now',0);"
    }

    # AuditLogs (hash-chained)
    $prevHash = ''
    foreach ($i in 1..5) {
        $g = [guid]::NewGuid().ToString()
        $hash = [guid]::NewGuid().ToString().Substring(0,32) + [guid]::NewGuid().ToString().Substring(0,32)
        sqlite3 $testDbPath "INSERT INTO AuditLogs (Id, Action, Details, CurrentHash, PreviousHash, CreatedAt, UpdatedAt) VALUES ('$g','TestAction$i','Details $i','$hash','$prevHash','$now','$now');"
        $prevHash = $hash
    }

    # DetectionEvents
    foreach ($i in 1..4) {
        $g = [guid]::NewGuid().ToString()
        sqlite3 $testDbPath "INSERT INTO DetectionEvents (Id, EventType, FilePath, Timestamp, CreatedAt, UpdatedAt) VALUES ('$g','Modified','C:\test\file$i.txt','$now','$now','$now');"
    }

    # SentinelCanaries
    foreach ($i in 1..3) {
        $g = [guid]::NewGuid().ToString()
        $hash = [guid]::NewGuid().ToString().Substring(0,32) + [guid]::NewGuid().ToString().Substring(0,32)
        sqlite3 $testDbPath "INSERT INTO SentinelCanaries (Id, FilePath, FileName, Directory, TemplateUsed, OriginalContentHash, Status, FileSize, CreatedAt, UpdatedAt, LastCheckedAt) VALUES ('$g','C:\canary\file$i.docx','file$i.docx','C:\canary','docx','$hash','Active',1024,'$now','$now','$now');"
    }

    # CanaryAlerts
    foreach ($i in 1..2) {
        $g = [guid]::NewGuid().ToString()
        sqlite3 $testDbPath "INSERT INTO CanaryAlerts (Id, CanaryId, CanaryPath, AlertType, Severity, DetectedAt, CreatedAt, UpdatedAt) VALUES ('$g','$guid1','C:\canary\file1.docx','ContentModified','Critical','$now','$now','$now');"
    }

    # EntropyBaselines
    foreach ($i in 1..3) {
        $g = [guid]::NewGuid().ToString()
        sqlite3 $testDbPath "INSERT INTO EntropyBaselines (Id, FilePath, DirectoryPath, FileExtension, EntropyValue, FileSize, CapturedAt) VALUES ('$g','C:\data\file$i.dat','C:\data','.dat',4.5,$($i * 1000),'$now');"
    }

    # EntropyAlerts
    foreach ($i in 1..2) {
        $g = [guid]::NewGuid().ToString()
        sqlite3 $testDbPath "INSERT INTO EntropyAlerts (Id, FilePath, RuleName, RuleId, Severity, BaselineEntropy, CurrentEntropy, Delta, DetectedAt, CreatedAt) VALUES ('$g','C:\data\file$i.dat','SuddenSpikeRule',1,'High',4.5,7.9,3.4,'$now','$now');"
    }

    # GenealogyRecords
    foreach ($i in 1..2) {
        $g = [guid]::NewGuid().ToString()
        sqlite3 $testDbPath "INSERT INTO GenealogyRecords (Id, AlertId, ProcessTreeJson, SuspiciousPatternsJson, RootProcessId, RootProcessName, Summary, CapturedAt) VALUES ('$g','$guid2','{}','[]',$($i * 100),'explorer.exe','Test genealogy $i','$now');"
    }

    # Verify row counts
    $allSeeded = $true
    foreach ($table in $sprint3TablesWithCounts.Keys) {
        $count = Get-RowCount $testDbPath $table
        $expected = $sprint3TablesWithCounts[$table]
        if ($count -ne $expected) {
            Write-TestResult "Seed $table ($expected rows)" $false "Got $count"
            $allSeeded = $false
        }
    }
    if ($allSeeded) {
        $totalRows = ($sprint3TablesWithCounts.Values | Measure-Object -Sum).Sum
        Write-TestResult "All Sprint 3 tables seeded ($totalRows total rows)" $true
    }
} catch {
    Write-TestResult 'Seed sample data' $false $_.Exception.Message
}

# ─── Step 3: Record pre-migration row counts ───
Write-Host "`nStep 3: Record pre-migration state..." -ForegroundColor Yellow
$preUpgradeCounts = @{}
foreach ($table in $sprint3TablesWithCounts.Keys) {
    $preUpgradeCounts[$table] = Get-RowCount $testDbPath $table
}
Write-Host "  Pre-migration row counts:" -ForegroundColor Gray
foreach ($table in ($preUpgradeCounts.Keys | Sort-Object)) {
    Write-Host "    $table : $($preUpgradeCounts[$table])"
}

# ─── Step 4: Apply Sprint 4 migrations ───
Write-Host "`nStep 4: Apply Sprint 4 migrations on v0.5.1 database..." -ForegroundColor Yellow
try {
    Run-EfUpdate ''
    $migCount = sqlite3 $testDbPath "SELECT COUNT(*) FROM __EFMigrationsHistory;"
    Write-TestResult 'All 15 migrations applied' ([int]$migCount -eq 15) "Got $migCount"
} catch {
    Write-TestResult 'Apply Sprint 4 migrations' $false $_.Exception.Message
}

# ─── Step 5: Verify no data loss in existing tables ───
Write-Host "`nStep 5: Verify no data loss in Sprint 3 tables..." -ForegroundColor Yellow
$postUpgradeCounts = @{}
$dataLossDetected = $false
foreach ($table in $sprint3TablesWithCounts.Keys) {
    $postCount = Get-RowCount $testDbPath $table
    $preCount = $preUpgradeCounts[$table]
    $postUpgradeCounts[$table] = $postCount
    $ok = ($postCount -eq $preCount)
    Write-TestResult "$table preserved ($preCount -> $postCount rows)" $ok "Expected $preCount, got $postCount"
    if (-not $ok) { $dataLossDetected = $true }
}

if (-not $dataLossDetected) {
    $totalRows = ($postUpgradeCounts.Values | Measure-Object -Sum).Sum
    Write-Host "  Total Sprint 3 rows preserved: $totalRows" -ForegroundColor Gray
}

# ─── Step 6: Verify new Sprint 4 tables are empty ───
Write-Host "`nStep 6: Verify Sprint 4 tables created empty..." -ForegroundColor Yellow
foreach ($table in $sprint4Tables) {
    try {
        $count = Get-RowCount $testDbPath $table
        Write-TestResult "$table exists and is empty" ($count -eq 0) "Contains $count rows"
    } catch {
        Write-TestResult "$table exists" $false $_.Exception.Message
    }
}

# ─── Step 7: Verify indexes on new tables ───
Write-Host "`nStep 7: Verify Sprint 4 indexes..." -ForegroundColor Yellow
try {
    $indexes = sqlite3 $testDbPath "SELECT name FROM sqlite_master WHERE type='index' AND name LIKE 'IX_%' ORDER BY name;"
    $sprint4Indexes = $indexes | Where-Object {
        $_ -match 'UsbWhitelist|UsbPolic|UsbConnection|UsbScan|UsbAlert|Quarantine|Exfil|NetworkBaseline|IndicatorRemoval'
    }
    # Expected: 2+0+3+2+2+2+3+2+2+3 = 21 indexes
    Write-TestResult "Sprint 4 indexes created ($($sprint4Indexes.Count) indexes)" ($sprint4Indexes.Count -ge 20) "Found $($sprint4Indexes.Count)"
} catch {
    Write-TestResult 'Verify indexes' $false $_.Exception.Message
}

# ─── Cleanup ───
Write-Host "`nStep 8: Cleanup..." -ForegroundColor Yellow
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
