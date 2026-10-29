<#
.SYNOPSIS
    One command that gets the demonstration ready, and says plainly whether it is.

.DESCRIPTION
    Starts whatever is not running -- the GRID containers, the console, the workstation -- waits
    for each to answer, then runs the full health check and prints DEMO READY or NOT READY with
    every problem and what to do about it.

    It is meant to be the first thing run on the morning of a demonstration, before anybody is
    watching. Everything it starts, it starts only if it is not already up; running it twice
    changes nothing.

    It never prints a password. The accounts and where their passwords are kept are printed,
    because somebody has to sign in; the passwords themselves stay in
    deployment/bench/credentials.local.md, which is gitignored and never leaves this machine.

    The Ubuntu virtual machine is never started: it is not part of this product and only takes
    memory from the ones that are.

.PARAMETER Vm
    The workstation to bring up. PC-RECEPTION by default: the one the owner tests.

.PARAMETER NoVm
    Leave the virtual machines alone (the platform only).

.PARAMETER SkipSuites
    Pass through to system-check.ps1: skip the automated suites, which are the slow part.

.EXAMPLE
    pwsh scripts/demo-ready.ps1

.EXAMPLE
    pwsh scripts/demo-ready.ps1 -Vm PC-LAB
#>
[CmdletBinding()]
param(
    [string] $Vm = 'PC-RECEPTION',
    [switch] $NoVm,
    [switch] $SkipSuites,
    [string] $VBoxManage = 'C:\Program Files\Oracle\VirtualBox\VBoxManage.exe'
)

$ErrorActionPreference = 'Continue'
Set-StrictMode -Version Latest

$Repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$Console = 'http://localhost:5175'
$Grid = 'https://192.168.56.1:8443'
$Credentials = Join-Path $Repo 'deployment\bench\credentials.local.md'

$Problems = [System.Collections.Generic.List[pscustomobject]]::new()
$Transcript = [System.Collections.Generic.List[string]]::new()

function Say {
    param([string] $Text, [string] $Colour = 'Gray')
    Write-Host $Text -ForegroundColor $Colour
    $Transcript.Add($Text)
}

function Problem {
    param([string] $What, [string] $Fix)
    $Problems.Add([pscustomobject]@{ What = $What; Fix = $Fix })
}

function Step { param([string] $Text) Say '' ; Say $Text 'Cyan' }

# -- 1. Docker -----------------------------------------------------------------

Step '[1/5] Docker'

$dockerUp = $false
try {
    & docker info *>$null
    $dockerUp = $LASTEXITCODE -eq 0
} catch {
    $dockerUp = $false
}

if ($dockerUp) {
    Say '      Docker is running'
} else {
    Say '      Docker is not answering; starting Docker Desktop' 'Yellow'
    $desktop = 'C:\Program Files\Docker\Docker\Docker Desktop.exe'
    if (Test-Path $desktop) {
        Start-Process $desktop | Out-Null
        for ($i = 0; $i -lt 90 -and -not $dockerUp; $i++) {
            Start-Sleep -Seconds 2
            try { & docker info *>$null; $dockerUp = $LASTEXITCODE -eq 0 } catch { }
        }
    }

    if ($dockerUp) {
        Say '      Docker is running'
    } else {
        Say '      Docker did not come up' 'Red'
        Problem 'Docker is not running' 'open Docker Desktop and wait for "Engine running", then run this again'
    }
}

# -- 2. the GRID ---------------------------------------------------------------

Step '[2/5] The GRID'

$expected = @('grid-nginx', 'grid-api', 'grid-mysql', 'grid-redis', 'grid-notifier')

function Running-Containers {
    $names = @{}
    try {
        foreach ($line in (& docker ps --format '{{.Names}}|{{.Status}}' 2>$null)) {
            $parts = $line -split '\|', 2
            if ($parts.Count -eq 2) { $names[$parts[0]] = $parts[1] }
        }
    } catch { }
    return $names
}

if ($dockerUp) {
    $containers = Running-Containers
    $missing = @($expected | Where-Object { -not $containers.ContainsKey($_) })

    if ($missing.Count -gt 0) {
        Say "      $($missing.Count) container(s) not running; starting the stack" 'Yellow'
        Push-Location (Join-Path $Repo 'deployment\grid')
        & docker compose -p grid-local --env-file .env up -d *>$null
        Pop-Location
        Start-Sleep -Seconds 5
        $containers = Running-Containers
        $missing = @($expected | Where-Object { -not $containers.ContainsKey($_) })
    }

    foreach ($name in $expected) {
        if ($containers.ContainsKey($name)) {
            Say "      $name  $($containers[$name])"
        } else {
            Say "      $name  NOT RUNNING" 'Red'
            Problem "the container $name is not running" `
                'cd deployment/grid; docker compose -p grid-local --env-file .env up -d'
        }
    }

    # Healthy, not merely started: the API answering is what a demonstration needs.
    $healthy = $false
    for ($i = 0; $i -lt 60 -and -not $healthy; $i++) {
        try {
            $ca = Join-Path $Repo 'deployment\grid\pki\ca.crt'
            $probe = & python -I -c @"
import ssl, sys, urllib.request
ctx = ssl.create_default_context(cafile=r'$ca')
try:
    with urllib.request.urlopen('$Grid/api/v1/health', context=ctx, timeout=5) as r:
        sys.exit(0 if r.status == 200 else 1)
except Exception:
    sys.exit(1)
"@
            $healthy = $LASTEXITCODE -eq 0
        } catch { }
        if (-not $healthy) { Start-Sleep -Seconds 2 }
    }

    if ($healthy) {
        Say '      the GRID answers over TLS, validated against its own authority'
    } else {
        Say '      the GRID does not answer yet' 'Red'
        Problem 'the GRID API does not answer on 8443' 'docker logs grid-api, and check grid-mysql is healthy'
    }
}

# -- 3. the console ------------------------------------------------------------

Step '[3/5] The console'

function Console-Answers {
    try {
        $r = Invoke-WebRequest -Uri $Console -UseBasicParsing -TimeoutSec 5
        return $r.StatusCode -eq 200
    } catch { return $false }
}

if (Console-Answers) {
    Say "      the console answers on $Console"
} else {
    Say '      the console is not answering; starting it' 'Yellow'
    $starter = Join-Path $env:USERPROFILE 'grid-local\start-console-real.sh'
    if (Test-Path $starter) {
        Start-Process -WindowStyle Hidden -FilePath 'bash' -ArgumentList @('-lc', "bash '$($starter -replace '\\','/')'") | Out-Null
        for ($i = 0; $i -lt 60; $i++) {
            Start-Sleep -Seconds 2
            if (Console-Answers) { break }
        }
    }

    if (Console-Answers) {
        Say "      the console answers on $Console"
    } else {
        Say '      the console did not come up' 'Red'
        Problem "the console does not answer on $Console" `
            'in Git Bash: bash ~/grid-local/start-console-real.sh'
    }
}

# -- 4. the workstation --------------------------------------------------------

Step '[4/5] The workstation'

if ($NoVm) {
    Say '      skipped (-NoVm)'
} elseif (-not (Test-Path $VBoxManage)) {
    Say '      VirtualBox not found at the expected path' 'Red'
    Problem 'VBoxManage was not found' "install VirtualBox, or pass -VBoxManage <path>"
} else {
    $running = @(& $VBoxManage list runningvms 2>$null | ForEach-Object {
        if ($_ -match '^"(.+)"\s') { $Matches[1] } })

    # Never the Ubuntu machine: it is not part of this product and only takes memory.
    foreach ($other in $running) {
        if ($other -like '*Ubuntu*') {
            Say "      $other is running and is not part of this demonstration" 'Yellow'
            Problem "$other is running and takes memory from the workstations" `
                "VBoxManage controlvm `"$other`" acpipowerbutton"
        }
    }

    if ($running -contains $Vm) {
        Say "      $Vm is already running"
    } else {
        Say "      starting $Vm" 'Yellow'
        & $VBoxManage startvm $Vm --type gui *>$null
        Start-Sleep -Seconds 10
        $running = @(& $VBoxManage list runningvms 2>$null | ForEach-Object {
            if ($_ -match '^"(.+)"\s') { $Matches[1] } })
        if ($running -contains $Vm) {
            Say "      $Vm is running (sign in: vboxuser, no password -- press Enter)"
        } else {
            Say "      $Vm did not start" 'Red'
            Problem "$Vm did not start" "open VirtualBox and start $Vm by hand; check it is not in a saved state"
        }
    }
}

# -- 4b. the workstation clocks ------------------------------------------------

# No Guest Additions on these VMs, so nothing keeps their clocks with the host's: they free-run
# and are corrected by hand. A drifted workstation makes every alert on the console look hours
# old, and before 2026-10-28 it blinded attribution outright. Worth one line before a demo.
if (-not $NoVm) {
    Say ''
    Say '      workstation clocks' 'Cyan'
    $clock = & python -I (Join-Path $Repo 'scripts\clock_skew_probe.py') --max-seconds 120 2>&1
    $clockOk = $LASTEXITCODE -eq 0
    foreach ($line in $clock) { Say "      $line" }
    if (-not $clockOk) {
        Problem 'a workstation clock is more than 2 minutes from the GRID' `
            'in the VM as administrator: Set-Date -Date "<correct local time>", then restart the VM'
    }
}

# -- 4c. room on the disks -----------------------------------------------------

# A disk that fills stops the agent writing its database and the installer extracting, and the
# first symptom is a workstation that has gone quiet. Both bench VMs reached zero bytes free on
# 2026-10-27 and nothing had looked.
Say ''
Say '      room on the disks' 'Cyan'
$disk = & python -I (Join-Path $Repo 'scripts\disk_space_probe.py') 2>&1
$diskOk = $LASTEXITCODE -eq 0
foreach ($line in $disk) { Say "      $line" }
if (-not $diskOk) {
    Problem 'a disk is low on space' `
        'docker builder prune -f, docker image prune -f, or remove a VM that is not the bench''s'
}

# -- 5. the health check -------------------------------------------------------

Step '[5/5] The health check'
Say '      running scripts/system-check.ps1; this takes several minutes'

$checkArgs = @('-NoProfile', '-File', (Join-Path $Repo 'scripts\system-check.ps1'))
if ($SkipSuites) { $checkArgs += '-SkipSuites' }
$checkOutput = & pwsh @checkArgs 2>&1 | ForEach-Object { [string]$_ }
$checkFailed = $LASTEXITCODE -ne 0
foreach ($line in $checkOutput) { $Transcript.Add($line) }

$summary = @($checkOutput | Select-String -Pattern '^\s+(PASS|FAIL|NOT BUILT|NOT TESTABLE HERE)\s+\d+\s*$')
foreach ($line in $summary) { Say ("      " + $line.Line.Trim()) }

if ($checkFailed) {
    foreach ($line in ($checkOutput | Select-String -Pattern '^\s+- ')) {
        Problem ("the health check: " + $line.Line.Trim().TrimStart('-', ' ')) `
            'see the report named at the end of the check'
    }
    if ($Problems.Count -eq 0) {
        Problem 'the health check reported a failure' 'run pwsh scripts/system-check.ps1 and read its output'
    }
}

# -- the verdict ---------------------------------------------------------------

Say ''
Say '============================================================' 'Cyan'
if ($Problems.Count -eq 0) {
    Say '  DEMO READY' 'Green'
} else {
    Say '  NOT READY' 'Red'
}
Say '============================================================' 'Cyan'

if ($Problems.Count -gt 0) {
    Say ''
    Say "  $($Problems.Count) thing(s) to put right:" 'Red'
    foreach ($problem in $Problems) {
        Say "    - $($problem.What)" 'Red'
        Say "      -> $($problem.Fix)" 'DarkYellow'
    }
}

Say ''
Say '  Where things are' 'Cyan'
Say "    console            $Console"
Say "    GRID               $Grid"
Say '    hospital           organization code yde-hospital'
Say '                       admin@yde-hospital.cm      (can do everything)'
Say '                       analyst@yde-hospital.cm    (handles alerts)'
Say '                       auditor@yde-hospital.cm    (reads, never acts)'
Say '    the other customer organization code test-clinic'
Say '                       admin@test-clinic.cm       (sees none of the hospital)'
Say ''
if (Test-Path $Credentials) {
    Say "    passwords          $Credentials"
    Say '                       (that file stays on this machine; it is never committed)'
} else {
    Say "    passwords          MISSING: $Credentials" 'Red'
    Problem 'the credentials note is missing' 'it is gitignored and local; ask whoever set the bench up'
}
Say ''
Say '    the owner guide    docs/OWNER_TEST_GUIDE.md'

# -- the transcript ------------------------------------------------------------

$reportDir = Join-Path $Repo 'docs\testing'
New-Item -ItemType Directory -Force $reportDir | Out-Null
$reportPath = Join-Path $reportDir ("demo-ready-{0}.md" -f (Get-Date -Format 'yyyy-MM-dd'))

$header = @(
    "# Demo readiness - $(Get-Date -Format 'yyyy-MM-dd HH:mm')",
    '',
    "Verdict: **$(if ($Problems.Count -eq 0) { 'DEMO READY' } else { 'NOT READY' })**",
    '',
    'Written by `scripts/demo-ready.ps1`. No password appears here.',
    '',
    '```'
)
($header + $Transcript + @('```')) | Set-Content -Path $reportPath -Encoding utf8

Say ''
Say "  Written to $reportPath" 'DarkGray'

exit $(if ($Problems.Count -eq 0) { 0 } else { 1 })
