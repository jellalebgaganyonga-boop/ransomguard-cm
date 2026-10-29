<#
.SYNOPSIS
    Show what SENTINEL did, from the agent's own log, in the order it happened.

.DESCRIPTION
    The agent writes structured JSON lines. Read raw they are unreadable on a VM console, so this
    prints one short line per event: the time, and the message with its properties filled in.

    It keeps the events that explain a canary alert -- the canaries being planted, the writes seen
    on them, the alerts raised and the responses attempted -- and drops the heartbeats.

    Read-only. It changes nothing on the workstation.
#>
[CmdletBinding()]
param(
    [string] $LogDirectory = 'C:\ProgramData\RansomGuard-CM\logs',
    [int] $Lines = 40,
    [switch] $First,
    [string] $Match = 'canary|Canary|SENTINEL|attribution|suspend'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$log = Get-ChildItem $LogDirectory -Filter 'agent-*.log' -ErrorAction SilentlyContinue |
    Sort-Object LastWriteTime | Select-Object -Last 1

if (-not $log) { Write-Host "no agent log in $LogDirectory"; return }
Write-Host "$($log.FullName)  ($([math]::Round($log.Length / 1KB)) KB)"
Write-Host ''

$events = foreach ($line in Get-Content $log.FullName) {
    if (-not $line.Trim().StartsWith('{')) { continue }
    try { $e = $line | ConvertFrom-Json } catch { continue }
    $text = $e.'@mt'
    if ($text -notmatch $Match) { continue }
    # Fill {Property} placeholders from the event's own fields, so the line reads as a sentence.
    foreach ($p in $e.PSObject.Properties) {
        if ($p.Name -like '@*') { continue }
        $text = $text -replace ("\{" + [regex]::Escape($p.Name) + "(:[^}]*)?\}"), [string]$p.Value
    }
    # Information-level events carry no '@l' at all, and StrictMode makes reading a missing
    # property an error rather than $null.
    $level = 'Info'
    if ($e.PSObject.Properties.Name -contains '@l') { $level = ([string]$e.'@l').Substring(0, 4) }

    [pscustomobject]@{
        Time = ([datetime]$e.'@t').ToString('HH:mm:ss')
        Level = $level
        Text = $text
    }
}

$shown = if ($First) { $events | Select-Object -First $Lines } else { $events | Select-Object -Last $Lines }
$shown | ForEach-Object {
    Write-Host ("{0} {1,-4} {2}" -f $_.Time, $_.Level, $_.Text)
}
Write-Host ''
Write-Host ("{0} matching events, {1} {2} shown" -f @($events).Count, $(if ($First) { 'first' } else { 'last' }), $Lines)
