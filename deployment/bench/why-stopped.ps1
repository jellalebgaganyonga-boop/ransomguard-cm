<#
.SYNOPSIS
    Why did the agent service stop? Reads it off the workstation itself.

.DESCRIPTION
    The bench VMs have no Guest Additions, so the only way to see anything inside one is to put
    it on the screen and take a picture of it. This prints, short enough to read from a
    screenshot: the service's current state, what the Service Control Manager recorded about it,
    what the agent itself wrote to the Windows Event Log at Warning or above, and the tail of its
    own log file.

    A .NET host stops with exit code 0 when a BackgroundService throws: that is the default
    BackgroundServiceExceptionBehavior. So "stopped, exit 0" is not evidence of a clean
    shutdown, and the line that says which service threw is the one worth finding.
#>
[CmdletBinding()]
param(
    [int] $Events = 6,
    [string] $ServiceName = 'RansomGuardAgent'
)

$ErrorActionPreference = 'Continue'

function Section([string] $Title) {
    Write-Host ''
    Write-Host "== $Title" -ForegroundColor Cyan
}

function Short([string] $Text, [int] $Max = 220) {
    if (-not $Text) { return '' }
    $one = ($Text -replace '\s+', ' ').Trim()
    if ($one.Length -le $Max) { return $one }
    return $one.Substring(0, $Max) + ' ...'
}

Section "service"
$svc = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($null -eq $svc) {
    Write-Host "$ServiceName is not installed"
} else {
    $wmi = Get-CimInstance Win32_Service -Filter "Name='$ServiceName'"
    Write-Host ("state={0}  start={1}  pid={2}" -f $svc.Status, $wmi.StartMode, $wmi.ProcessId)
}

Section "Service Control Manager (System log)"
$scm = Get-WinEvent -FilterHashtable @{LogName = 'System'; ProviderName = 'Service Control Manager' } `
    -MaxEvents 200 -ErrorAction SilentlyContinue |
    Where-Object { $_.Message -match 'RansomGuard' } | Select-Object -First $Events
if (-not $scm) { Write-Host 'nothing about RansomGuard' }
foreach ($e in $scm) {
    Write-Host ("{0:HH:mm:ss}  id={1}  {2}" -f $e.TimeCreated, $e.Id, (Short $e.Message 150))
}

Section "the agent's own Event Log entries (Warning and above)"
$app = Get-WinEvent -FilterHashtable @{LogName = 'Application'; ProviderName = 'RansomGuard-CM' } `
    -MaxEvents $Events -ErrorAction SilentlyContinue
if (-not $app) { Write-Host 'nothing from source RansomGuard-CM' }
foreach ($e in $app) {
    Write-Host ("{0:HH:mm:ss}  {1}" -f $e.TimeCreated, $e.LevelDisplayName) -ForegroundColor Yellow
    Write-Host ("    " + (Short $e.Message 400))
}

Section ".NET Runtime / Application Error"
$crash = Get-WinEvent -FilterHashtable @{LogName = 'Application'; Level = 1, 2 } `
    -MaxEvents 60 -ErrorAction SilentlyContinue |
    Where-Object { $_.Message -match 'RansomGuard' } | Select-Object -First 3
if (-not $crash) { Write-Host 'no error-level entry naming RansomGuard' }
foreach ($e in $crash) {
    Write-Host ("{0:HH:mm:ss}  {1}  {2}" -f $e.TimeCreated, $e.ProviderName, (Short $e.Message 300))
}

Section "the agent's log file"
$dir = 'C:\ProgramData\RansomGuard-CM\logs'
$log = Get-ChildItem $dir -Filter 'agent-*.log' -ErrorAction SilentlyContinue |
    Sort-Object LastWriteTime -Descending | Select-Object -First 1
if ($null -eq $log) {
    Write-Host "no agent log in $dir"
} else {
    Write-Host ("{0}  {1} bytes  last written {2:HH:mm:ss}" -f $log.Name, $log.Length, $log.LastWriteTime)
    Get-Content $log.FullName -Tail 12 -ErrorAction SilentlyContinue |
        ForEach-Object { Write-Host ('  ' + (Short $_ 150)) }
}
