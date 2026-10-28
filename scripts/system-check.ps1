<#
.SYNOPSIS
    One command that says whether RansomGuard-CM works, in plain words.

.DESCRIPTION
    Runs every check to the end. A failure is recorded and the run continues, because stopping at
    the first problem tells you about one problem and hides the rest.

    Each line is one of:

      PASS               the thing was checked and it worked
      FAIL               it was checked and it did not work; the reason and what to do follow
      NOT BUILT          it does not exist yet, and why
      NOT TESTABLE HERE  it exists but this bench cannot exercise it, and why

    Nothing prints PASS unless it was actually checked. Passwords are read from
    deployment/bench/credentials.local.md and never printed.

    The exit code is 0 only when nothing FAILed. NOT BUILT and NOT TESTABLE HERE do not fail the
    run: they are the honest state of the system, not defects.

.PARAMETER WithDetection
    The VirtualBox name of a workstation to run a live detection against (for example PC-LAB).
    The script asks you to run the trigger on that workstation, waits, then confirms through the
    API that the alert, the attribution, the process chain and the suspension arrived, sends
    Resume and confirms the closure.

.PARAMETER SkipSuites
    Leaves out the automated test suites, which are the slow part (several minutes).

.EXAMPLE
    pwsh scripts/system-check.ps1

.EXAMPLE
    pwsh scripts/system-check.ps1 -WithDetection PC-LAB
#>
[CmdletBinding()]
param(
    [string] $WithDetection,
    [switch] $SkipSuites,
    [string] $VBoxManage = 'C:\Program Files\Oracle\VirtualBox\VBoxManage.exe'
)

$ErrorActionPreference = 'Continue'
Set-StrictMode -Version Latest

$Repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$Results = [System.Collections.Generic.List[pscustomobject]]::new()

# -- how a result is recorded and shown ---------------------------------------

function Add-Result {
    param(
        [Parameter(Mandatory)] [string] $Section,
        [Parameter(Mandatory)] [string] $Name,
        [Parameter(Mandatory)] [ValidateSet('PASS', 'FAIL', 'NOT BUILT', 'NOT TESTABLE HERE')] [string] $Status,
        [string] $Detail = '',
        [string] $Fix = ''
    )

    $Results.Add([pscustomobject]@{
        Section = $Section; Name = $Name; Status = $Status; Detail = $Detail; Fix = $Fix
    })

    $colour = switch ($Status) {
        'PASS' { 'Green' }
        'FAIL' { 'Red' }
        default { 'Yellow' }
    }
    Write-Host ("  {0,-18}" -f $Status) -ForegroundColor $colour -NoNewline
    Write-Host " $Name"
    if ($Detail) { Write-Host "                     $Detail" -ForegroundColor DarkGray }
    if ($Status -eq 'FAIL' -and $Fix) { Write-Host "                     -> $Fix" -ForegroundColor DarkYellow }
}

function Write-Section {
    param([string] $Title)
    Write-Host ''
    Write-Host $Title -ForegroundColor Cyan
}

# -- 1. the platform ----------------------------------------------------------

Write-Section '1. The platform'

$expected = @('grid-nginx', 'grid-api', 'grid-mysql', 'grid-redis', 'grid-notifier')
$running = @{}
try {
    foreach ($line in (& docker ps --format '{{.Names}}|{{.Status}}' 2>$null)) {
        $parts = $line -split '\|', 2
        if ($parts.Count -eq 2) { $running[$parts[0]] = $parts[1] }
    }
} catch {
    Add-Result -Section 'Platform' -Name 'Docker answers' -Status 'FAIL' `
        -Detail $_.Exception.Message -Fix 'start Docker Desktop and wait for "Engine running"'
}

foreach ($container in $expected) {
    if (-not $running.ContainsKey($container)) {
        Add-Result -Section 'Platform' -Name "Container $container" -Status 'FAIL' `
            -Detail 'not running' `
            -Fix 'cd deployment/grid; docker compose -p grid-local --env-file .env up -d'
        continue
    }
    $state = $running[$container]
    # nginx has no health probe, so "Up" is all it can say; the others report healthy.
    if ($state -like 'Up*unhealthy*') {
        Add-Result -Section 'Platform' -Name "Container $container" -Status 'FAIL' -Detail $state `
            -Fix "docker logs $container"
    } elseif ($state -like 'Up*') {
        Add-Result -Section 'Platform' -Name "Container $container" -Status 'PASS' -Detail $state
    } else {
        Add-Result -Section 'Platform' -Name "Container $container" -Status 'FAIL' -Detail $state `
            -Fix "docker logs $container"
    }
}

# -- 2-5. everything that talks to the GRID -----------------------------------

# The probe runs once, for everything that needs the GRID. It signs in once too: a sign-in per
# section meant sixteen logins in a few seconds, and the session store answered 503 -- a failure
# the checker caused itself and then blamed on the platform.

Write-Section '2-5. The GRID, the accounts, the workstations and the installer'

$probeOutput = & python -I (Join-Path $Repo 'scripts\system_check_probe.py') --section all 2>&1

# The workstation clocks. These VMs have no Guest Additions, so nothing keeps their clocks with
# the host's: they free-run and are corrected by hand. A drifted workstation makes every alert on
# the console look hours old, and before 2026-10-28 it blinded attribution outright -- the ETW
# session timestamps and Process.StartTime ended up in different frames and every writer looked
# like a recycled PID. An agent that has uploaded nothing recently says so rather than passing.
$clockOutput = & python -I (Join-Path $Repo 'scripts\clock_skew_probe.py') --max-seconds 120 2>&1
$clockOk = $LASTEXITCODE -eq 0
$clockDetail = ($clockOutput | Where-Object { $_ -match '\S' } | Select-Object -First 4) -join '; '
Add-Result -Section 'The platform answers' -Name 'Workstation clocks match the GRID' `
    -Status $(if ($clockOk) { 'PASS' } else { 'FAIL' }) `
    -Detail $clockDetail `
    -Fix 'in the VM as administrator: Set-Date -Date "<correct local time>", then restart the VM so the ETW session reopens on the new clock'

$currentSection = 'General'
foreach ($line in $probeOutput) {
    $text = [string]$line
    if (-not $text.TrimStart().StartsWith('{')) {
        if ($text.Trim()) { Write-Host "  $text" -ForegroundColor DarkGray }
        continue
    }
    try { $item = $text | ConvertFrom-Json } catch { continue }

    if ($item.PSObject.Properties.Name -contains 'section_heading') {
        $currentSection = $item.section_heading
        Write-Host ''
        Write-Host "  $currentSection" -ForegroundColor DarkCyan
        continue
    }

    Add-Result -Section $item.section -Name $item.name -Status $item.status `
        -Detail $item.detail -Fix $item.fix
}

# -- 6. the automated suites --------------------------------------------------

Write-Section '6. The automated suites'

function Invoke-Suite {
    param(
        [string] $Name,
        [string] $WorkingDirectory,
        [scriptblock] $Command,
        [string] $Fix
    )

    Push-Location (Join-Path $Repo $WorkingDirectory)
    try {
        $global:LASTEXITCODE = 0
        $output = & $Command 2>&1 | Out-String
        $code = $LASTEXITCODE
        $ok = $code -eq 0
        # A summary that reports only the passing count hides the failure above it: Playwright
        # prints "1 failed" and then "80 passed", so taking the last matching line said
        # "80 passed" on a run that had failed. When the suite failed, say what failed.
        $lines = $output -split "`n"
        if ($ok) {
            $summary = ($lines | Where-Object { $_ -match 'passed|no French' } | Select-Object -Last 1)
        } else {
            $summary = ($lines | Where-Object { $_ -match '\d+ failed|error|FAILED' } | Select-Object -First 1)
            $passed = ($lines | Where-Object { $_ -match '\d+ passed' } | Select-Object -Last 1)
            if ($passed) { $summary = "$($summary.Trim()); $($passed.Trim())" }
        }
        if ($summary) { $summary = $summary.Trim() }
        if (-not $summary) { $summary = "exit code $code" }
        Add-Result -Section 'Suites' -Name $Name -Status $(if ($ok) { 'PASS' } else { 'FAIL' }) `
            -Detail $summary -Fix $(if ($ok) { '' } else { $Fix })
    } finally {
        Pop-Location
    }
}

if ($SkipSuites) {
    Add-Result -Section 'Suites' -Name 'Automated suites' -Status 'NOT TESTABLE HERE' `
        -Detail 'skipped with -SkipSuites'
} else {
    Write-Host '  (several minutes)' -ForegroundColor DarkGray

    Invoke-Suite -Name 'GRID suite (pytest)' -WorkingDirectory 'grid' `
        -Command { & .\.venv\Scripts\python.exe -m pytest tests -q } `
        -Fix 'cd grid; .\.venv\Scripts\python.exe -m pytest tests -q'

    Invoke-Suite -Name 'Console suite (vitest)' -WorkingDirectory 'dashboard' `
        -Command { & npx vitest run } -Fix 'cd dashboard; npx vitest run'

    Invoke-Suite -Name 'No French reaches a user' -WorkingDirectory '.' `
        -Command { & python -I scripts/check-no-french.py } `
        -Fix 'python scripts/check-no-french.py, and translate what it names'

    # The agent suite needs Windows and a free assembly: endpoint protection on this machine holds
    # a read handle on the freshly built test DLL, so it cannot run here. CI is the evidence.
    $ciStatus = 'NOT TESTABLE HERE'
    $ciDetail = 'the agent test DLL cannot be loaded on this machine (endpoint protection)'
    $ciFix = ''
    try {
        $run = & gh run list --branch sprint8/recovery --workflow 'Agent full' --limit 1 `
            --json databaseId,conclusion,headSha 2>$null | ConvertFrom-Json
        if ($run -and $run.Count -gt 0) {
            $head = $run[0].headSha.Substring(0, 7)
            if ($run[0].conclusion -eq 'success') {
                $ciStatus = 'PASS'
                $ciDetail = "CI run $($run[0].databaseId) green on $head"
            } else {
                $ciStatus = 'FAIL'
                $ciDetail = "CI run $($run[0].databaseId) is $($run[0].conclusion) on $head"
                $ciFix = "gh run view $($run[0].databaseId) --log-failed"
            }
        }
    } catch { }
    Add-Result -Section 'Suites' -Name 'Agent suite (CI, cannot run here)' -Status $ciStatus `
        -Detail $ciDetail -Fix $ciFix

    # Playwright needs its browser; absent, that is a missing tool and not a product failure.
    $browsers = Join-Path $env:USERPROFILE 'AppData\Local\ms-playwright'
    if (Test-Path $browsers) {
        Invoke-Suite -Name 'Console end-to-end (Playwright)' -WorkingDirectory 'dashboard' `
            -Command { & npx playwright test --project=chromium --reporter=line } `
            -Fix 'cd dashboard; npx playwright test --project=chromium'
    } else {
        Add-Result -Section 'Suites' -Name 'Console end-to-end (Playwright)' `
            -Status 'NOT TESTABLE HERE' -Detail 'the Playwright browser is not installed here' `
            -Fix ''
    }
}

# -- 7. a live detection, if asked for ----------------------------------------

if ($WithDetection) {
    Write-Section "7. A live detection on $WithDetection"

    $before = & python -I (Join-Path $Repo 'scripts\system_check_detection.py') --stage before `
        --vm $WithDetection 2>&1 | Out-String

    Write-Host ''
    Write-Host "  On $WithDetection, open a Command Prompt and run:" -ForegroundColor Yellow
    Write-Host '      D:\trigger-sentinel.cmd' -ForegroundColor White
    Write-Host '  It writes into ONE decoy file and nothing else.' -ForegroundColor DarkGray
    Write-Host '  Press Enter here once it has printed "write 1/5 done".' -ForegroundColor Yellow
    [void](Read-Host)

    $output = & python -I (Join-Path $Repo 'scripts\system_check_detection.py') --stage confirm `
        --vm $WithDetection 2>&1
    foreach ($line in $output) {
        $text = [string]$line
        if (-not $text.TrimStart().StartsWith('{')) {
            if ($text.Trim()) { Write-Host "  $text" -ForegroundColor DarkGray }
            continue
        }
        try { $item = $text | ConvertFrom-Json } catch { continue }
        Add-Result -Section 'Live detection' -Name $item.name -Status $item.status `
            -Detail $item.detail -Fix $item.fix
    }
}

# -- the verdict --------------------------------------------------------------

$counts = @{
    'PASS' = @($Results | Where-Object Status -eq 'PASS').Count
    'FAIL' = @($Results | Where-Object Status -eq 'FAIL').Count
    'NOT BUILT' = @($Results | Where-Object Status -eq 'NOT BUILT').Count
    'NOT TESTABLE HERE' = @($Results | Where-Object Status -eq 'NOT TESTABLE HERE').Count
}

Write-Host ''
Write-Host '------------------------------------------------------------' -ForegroundColor Cyan
Write-Host ' SUMMARY' -ForegroundColor Cyan
Write-Host '------------------------------------------------------------' -ForegroundColor Cyan
Write-Host ("  PASS               {0}" -f $counts['PASS']) -ForegroundColor Green
Write-Host ("  FAIL               {0}" -f $counts['FAIL']) -ForegroundColor $(if ($counts['FAIL']) { 'Red' } else { 'DarkGray' })
Write-Host ("  NOT BUILT          {0}" -f $counts['NOT BUILT']) -ForegroundColor Yellow
Write-Host ("  NOT TESTABLE HERE  {0}" -f $counts['NOT TESTABLE HERE']) -ForegroundColor Yellow

if ($counts['FAIL'] -gt 0) {
    Write-Host ''
    Write-Host '  What failed:' -ForegroundColor Red
    foreach ($failure in ($Results | Where-Object Status -eq 'FAIL')) {
        Write-Host "    - $($failure.Name): $($failure.Detail)" -ForegroundColor Red
        if ($failure.Fix) { Write-Host "      -> $($failure.Fix)" -ForegroundColor DarkYellow }
    }
}

Write-Host ''
if ($counts['FAIL'] -eq 0) {
    Write-Host '  Everything that could be checked, works.' -ForegroundColor Green
    Write-Host '  NOT BUILT and NOT TESTABLE HERE above are the honest state, not failures.' -ForegroundColor DarkGray
} else {
    Write-Host "  $($counts['FAIL']) check(s) failed. The platform is not ready." -ForegroundColor Red
}

# -- the report ---------------------------------------------------------------

$stamp = Get-Date -Format 'yyyy-MM-dd'
$reportDir = Join-Path $Repo 'docs\testing'
New-Item -ItemType Directory -Force $reportDir | Out-Null
$reportPath = Join-Path $reportDir "system-check-$stamp.md"

$report = [System.Text.StringBuilder]::new()
[void]$report.AppendLine("# System check - $stamp")
[void]$report.AppendLine()
[void]$report.AppendLine("Run at $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') on $env:COMPUTERNAME by ``scripts/system-check.ps1``.")
[void]$report.AppendLine()
[void]$report.AppendLine('A line says PASS only if it was checked and worked. NOT BUILT and NOT TESTABLE HERE are')
[void]$report.AppendLine('the honest state of the system, not failures, and do not change the exit code.')
[void]$report.AppendLine()
[void]$report.AppendLine('| | Count |')
[void]$report.AppendLine('|---|---|')
[void]$report.AppendLine("| PASS | $($counts['PASS']) |")
[void]$report.AppendLine("| FAIL | $($counts['FAIL']) |")
[void]$report.AppendLine("| NOT BUILT | $($counts['NOT BUILT']) |")
[void]$report.AppendLine("| NOT TESTABLE HERE | $($counts['NOT TESTABLE HERE']) |")
[void]$report.AppendLine()

foreach ($section in ($Results.Section | Select-Object -Unique)) {
    [void]$report.AppendLine("## $section")
    [void]$report.AppendLine()
    [void]$report.AppendLine('| Status | Check | Detail |')
    [void]$report.AppendLine('|---|---|---|')
    foreach ($row in ($Results | Where-Object Section -eq $section)) {
        $name = $row.Name -replace '\|', '\|'
        $detail = ($row.Detail -replace '\|', '\|')
        if ($row.Status -eq 'FAIL' -and $row.Fix) { $detail += " - **fix:** $($row.Fix)" }
        [void]$report.AppendLine("| $($row.Status) | $name | $detail |")
    }
    [void]$report.AppendLine()
}

Set-Content -Path $reportPath -Value $report.ToString() -Encoding UTF8
Write-Host ''
Write-Host "  Report: $reportPath" -ForegroundColor Cyan
Write-Host ''

exit $(if ($counts['FAIL'] -gt 0) { 1 } else { 0 })
