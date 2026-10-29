<#
.SYNOPSIS
    Put USB GUARD into Audit mode, and report what the agent is writing to its log.

.DESCRIPTION
    Run inside the VM, from the bench disc, as administrator.

    Audit mode means USB GUARD raises alerts and never acts on the device: it does not make the
    drive read-only and it does not move files into quarantine. The shipped default, Permissive,
    does both -- read-only on a Critical finding, quarantine on a High one -- which is right for a
    hospital workstation and wrong for a borrowed key that belongs to somebody.

    It also prints the size of the agent's log before and after a restart, because a log file that
    stays at zero bytes is the thing that makes every other problem hard to diagnose.
#>
[CmdletBinding()]
param(
    [string] $Settings = 'C:\Program Files\RansomGuard-CM\appsettings.json',
    [string] $LogDir = 'C:\ProgramData\RansomGuard-CM\logs'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function LogSizes($when) {
    Write-Host "  the agent's log, $when"
    $any = $false
    foreach ($f in (Get-ChildItem $LogDir -Filter '*.log' -ErrorAction SilentlyContinue)) {
        Write-Host ("      {0,-26} {1,10} bytes" -f $f.Name, $f.Length)
        $any = $true
    }
    if (-not $any) { Write-Host '      (no log files at all)' }
}

Write-Host '[1/4] before'
LogSizes 'before the restart'

Write-Host ''
Write-Host '[2/4] USB GUARD -> Audit (alerts only; never touches the device or its files)'
$text = Get-Content $Settings -Raw
$before = ([regex]'"OperatingMode"\s*:\s*"(\w+)"').Match($text).Groups[1].Value
Write-Host "      was: $before"
$text = $text -replace '("OperatingMode"\s*:\s*)"\w+"', '$1"Audit"'
Set-Content -Path $Settings -Value $text -Encoding utf8
$after = ([regex]'"OperatingMode"\s*:\s*"(\w+)"').Match((Get-Content $Settings -Raw)).Groups[1].Value
Write-Host "      now: $after"

Write-Host ''
Write-Host '[3/4] restarting the agent so it reads the new setting'
Restart-Service RansomGuardAgent
Start-Sleep -Seconds 20
Write-Host ('      service: {0}' -f (Get-Service RansomGuardAgent).Status)

Write-Host ''
Write-Host '[4/4] after'
LogSizes 'after the restart'
Write-Host ''
Write-Host 'USB GUARD is in Audit mode. A key attached now is read, never written.'
