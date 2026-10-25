<#
.SYNOPSIS
    Harmless SENTINEL trigger for the demo bench. Run it ON THE BENCH VM, after the agent has enrolled.

.DESCRIPTION
    Writes one byte, five times, three seconds apart, into ONE of the decoy files SENTINEL planted
    for exactly this purpose: the "canaries", 0001_* in Desktop\RansomGuard-TestZone. It encrypts
    nothing, deletes nothing, and touches no other file.

    What should happen, if the product works:
      - SENTINEL raises SentinelCanaryModified at the first write, and its ETW attribution names
        this PowerShell process (ADR-025);
      - Agent.Sentinel.Response.SuspendProcess is true by default, so the agent suspends this
        process (ADR-026): the counter below STOPS, the window looks frozen;
      - in the console, agent page > Suspended processes > Resume: the counter carries on and the
        script prints "finished".

    If the counter never stops, the suspension did not happen: say so, do not hide it.

    Messages are plain ASCII: the console code page of the bench VM is unknown.
#>
$ErrorActionPreference = 'Stop'

$zone = Join-Path $env:USERPROFILE 'Desktop\RansomGuard-TestZone'
$canary = Get-ChildItem -Path $zone -Filter '0001_*' -File -Force -ErrorAction SilentlyContinue |
    Sort-Object Name | Select-Object -First 1

if (-not $canary) {
    Write-Host "No canary in $zone." -ForegroundColor Red
    Write-Host 'SENTINEL plants them when the agent starts: is the agent installed, and has it enrolled?'
    Read-Host 'Press Enter to close'
    exit 1
}

Write-Host "This PowerShell PID : $PID"
Write-Host "Canary written to   : $($canary.FullName)"
Write-Host ''

for ($i = 1; $i -le 5; $i++) {
    [System.IO.File]::AppendAllText($canary.FullName, 'x')
    Write-Host ('{0:HH:mm:ss}  write {1}/5 done' -f (Get-Date), $i)
    Start-Sleep -Seconds 3
}

Write-Host ''
Write-Host 'finished. A pause above was the suspension; the resume let the script continue.' -ForegroundColor Green
Read-Host 'Press Enter to close'
