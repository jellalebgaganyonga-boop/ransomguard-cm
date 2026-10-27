<#
.SYNOPSIS
    What is wrong with the agent on this workstation? Reads it off the workstation itself.

.DESCRIPTION
    The bench VMs have no Guest Additions, so the only way to see anything inside one is to put
    it on the screen and take a picture of it. This prints, short enough to read from a
    screenshot: the service's current state, what the Service Control Manager recorded about it,
    what the agent itself wrote to the Windows Event Log at Warning or above, and the tail of its
    own log file.

    A .NET host stops with exit code 0 when a BackgroundService throws: that is the default
    BackgroundServiceExceptionBehavior. So "stopped, exit 0" is not evidence of a clean
    shutdown, and the line that says which service threw is the one worth finding.

    It also says whether the GRID can be reached from here, because the other half of what goes
    wrong on a bench workstation is a machine that is running perfectly and talking to nobody.
#>
[CmdletBinding()]
param(
    [int] $Events = 6,
    [string] $ServiceName = 'RansomGuardAgent',
    [string] $GridHost = '192.168.56.1',
    [int] $GridPort = 443,
    [string] $Match = 'enroll',
    [int] $Width = 170
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

    if ($Match) {
        Write-Host ""
        Write-Host ("  lines matching '{0}':" -f $Match) -ForegroundColor Yellow
        $hits = @(Get-Content $log.FullName -ErrorAction SilentlyContinue |
            Select-String -SimpleMatch $Match | Select-Object -Last 8)
        if ($hits.Count -eq 0) { Write-Host '  none' }
        foreach ($h in $hits) { Write-Host ('  ' + (Short $h.Line $Width)) }
    }
}

Section "can this workstation reach the GRID?"
$ping = Test-Connection -ComputerName $GridHost -Count 2 -Quiet -ErrorAction SilentlyContinue
Write-Host ("ping {0}: {1}" -f $GridHost, $(if ($ping) { 'answers' } else { 'no answer' }))
$tcp = Test-NetConnection -ComputerName $GridHost -Port $GridPort -WarningAction SilentlyContinue
Write-Host ("tcp {0}:{1}: {2}   (via {3})" -f $GridHost, $GridPort,
    $(if ($tcp.TcpTestSucceeded) { 'open' } else { 'refused or filtered' }), $tcp.SourceAddress.IPAddress)
try {
    $health = Invoke-WebRequest -Uri "https://${GridHost}:${GridPort}/api/v1/health" -TimeoutSec 10 -UseBasicParsing
    Write-Host ("https health: HTTP {0}  {1}" -f $health.StatusCode, (Short $health.Content 120))
} catch {
    Write-Host ("https health: " + (Short $_.Exception.Message 200))
}
try {
    # The enrolment endpoint takes no client certificate by design, so an empty body is enough to
    # see whether this machine reaches the GRID's own answer (422, it asks for the missing fields)
    # or something in between answering for it.
    $probe = Invoke-WebRequest -Uri "https://${GridHost}:${GridPort}/api/v1/agents/enroll" -Method POST `
        -ContentType 'application/json' -Body '{}' -TimeoutSec 15 -UseBasicParsing
    Write-Host ("enrolment endpoint: HTTP {0}" -f $probe.StatusCode)
} catch {
    $resp = $_.Exception.Response
    if ($resp) {
        Write-Host ("enrolment endpoint: HTTP {0} {1}" -f [int]$resp.StatusCode, $resp.StatusDescription)
    } else {
        Write-Host ("enrolment endpoint: " + (Short $_.Exception.Message 180))
    }
}

