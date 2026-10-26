<#
.SYNOPSIS
    Return a bench VM to the state it was in before the agent was ever installed.

.DESCRIPTION
    Run inside the VM, from the bench disc, as administrator. A reinstall on top of an existing
    installation is not a fresh install: the canaries are already planted, so the deployment plants
    nothing, and the one thing a fresh install has to prove -- that it raises no alert while
    planting its decoys -- is never exercised.

    It removes, in this order:

      1. the Windows service (stopped, then deleted);
      2. C:\ProgramData\RansomGuard-CM -- the agent's database, its enrolment and its logs;
      3. C:\Program Files\RansomGuard-CM -- the binaries;
      4. the canary files left behind on disk. Those are hidden+system files whose database rows
         have just been deleted with the database, so nothing would ever clean them up again and
         the next install would plant its own alongside them.

    It touches nothing else: the FICTIONAL hospital documents, the `nurse` account and the
    workstation name all stay as they are.

.PARAMETER WatchedRoots
    Where to look for orphaned canaries. Defaults to the profiles and public folders SENTINEL
    watches on the bench.
#>
[CmdletBinding()]
param(
    [string] $ServiceName = 'RansomGuardAgent',
    [string[]] $WatchedRoots = @(
        "$env:USERPROFILE\Desktop",
        "$env:USERPROFILE\Documents",
        'C:\Users\Public\Documents',
        'C:\Users\Public\Desktop'
    )
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Step($n, $text) { Write-Host "[$n] $text" }

# -- 1. the service ----------------------------------------------------------
Step 1 "service $ServiceName"
$svc = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($svc) {
    if ($svc.Status -ne 'Stopped') {
        Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
        # sc.exe delete fails while the service is still stopping.
        for ($i = 0; $i -lt 30 -and (Get-Service -Name $ServiceName -ErrorAction SilentlyContinue).Status -ne 'Stopped'; $i++) {
            Start-Sleep -Milliseconds 500
        }
    }
    & sc.exe delete $ServiceName | Out-Null
    Write-Host '      deleted'

    # Deleting a service does not end the process running it. The agent went on running after
    # this, holding its own files open and re-planting the canaries that had just been removed,
    # so a reset followed by an install was not a fresh install at all. Wait for it, then end it.
    $process = Get-Process -Name 'RansomGuard.Agent.Service' -ErrorAction SilentlyContinue
    if ($process) {
        Write-Host '      waiting for the agent process to end'
        if (-not $process.WaitForExit(15000)) {
            Write-Host '      still running after 15 s, ending it'
            Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
            Start-Sleep -Seconds 2
        }
    }
} else {
    Write-Host '      not installed'
}

# -- 2 and 3. the agent's data and binaries ----------------------------------
foreach ($pair in @(
        @{ N = 2; Path = 'C:\ProgramData\RansomGuard-CM'; What = 'data (database, enrolment, logs)' },
        @{ N = 3; Path = 'C:\Program Files\RansomGuard-CM'; What = 'binaries' })) {
    Step $pair.N "$($pair.What): $($pair.Path)"
    if (Test-Path $pair.Path) {
        Remove-Item -Recurse -Force $pair.Path -ErrorAction SilentlyContinue
        Write-Host $(if (Test-Path $pair.Path) { '      still present (a file is held open)' } else { '      removed' })
    } else {
        Write-Host '      absent'
    }
}

# -- 4. the canaries left on disk --------------------------------------------
Step 4 'canary files left behind'
# A canary is named <prefix><template>_<8 hex>.<ext> and is hidden+system. The 8 hex characters
# are what keep this from matching an ordinary document that happens to start with 0001_.
$pattern = '^\d+_[a-z_]+_[0-9a-f]{8}\.'
$removed = 0
foreach ($root in ($WatchedRoots | Select-Object -Unique)) {
    if (-not (Test-Path $root)) { continue }
    # Recursive: SENTINEL also watches subfolders such as Desktop\RansomGuard-TestZone, and a
    # canary left behind there outlives the reset. The bench then triggers against a file that is
    # no longer a registered canary, nothing is detected, and the run looks like a detection
    # failure when it is a leftover.
    $candidates = Get-ChildItem -Force -File -Recurse -Path $root -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -match $pattern -and $_.Attributes -band [IO.FileAttributes]::Hidden }
    foreach ($f in $candidates) {
        Set-ItemProperty -Path $f.FullName -Name Attributes -Value ([IO.FileAttributes]::Normal) -ErrorAction SilentlyContinue
        Remove-Item -Force $f.FullName -ErrorAction SilentlyContinue
        if (-not (Test-Path $f.FullName)) {
            Write-Host "      removed $($f.FullName)"
            $removed++
        }
    }
}
Write-Host "      $removed canary file(s) removed"

Write-Host ''
Write-Host 'The workstation is back to before the agent was installed.'
Write-Host 'The FICTIONAL documents, the `nurse` account and the machine name are untouched.'
