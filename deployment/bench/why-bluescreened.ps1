<#
.SYNOPSIS
    Why this workstation bluescreened, and what state it is in now. Run inside the VM.

.DESCRIPTION
    PC-LAB stopped with DRIVER_PORTION_MUST_BE_NONPAGED in clipsp.sys on 2026-10-28, in the
    middle of a working session. clipsp.sys is Windows' own Client License Service driver, not
    anything this product installs, but a workstation that stops during a demonstration is this
    product's problem whoever's driver it is.

    This prints what a decision needs:

      1. every bug check Windows recorded, with its code and when -- so "it crashed once" can be
         told apart from "it crashes";
      2. the crash dumps left on disk;
      3. memory, the page file and free disk space, because a paged-pool fault is the symptom
         that a machine too tight on memory produces first;
      4. whether Windows is activated, since clipsp.sys is the licensing driver and an expired
         or unactivated install is the usual company this bug check keeps.

    Read-only. It changes nothing.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Continue'
Set-StrictMode -Version Latest

Write-Host '== bug checks Windows recorded ==' -ForegroundColor Cyan
$events = Get-WinEvent -FilterHashtable @{ LogName = 'System'; Id = 1001 } -MaxEvents 40 -ErrorAction SilentlyContinue |
    Where-Object { $_.ProviderName -like '*BugCheck*' }
if (-not $events) {
    Write-Host '  none in the System log'
} else {
    foreach ($e in $events) {
        $text = ($e.Message -replace '\s+', ' ').Trim()
        Write-Host "  $($e.TimeCreated.ToString('yyyy-MM-dd HH:mm:ss'))  $($text.Substring(0, [Math]::Min(150, $text.Length)))"
    }
    Write-Host "  total bug checks in the log: $($events.Count)"
}

Write-Host ''
Write-Host '== unexpected shutdowns (kernel-power 41) ==' -ForegroundColor Cyan
$power = Get-WinEvent -FilterHashtable @{ LogName = 'System'; Id = 41 } -MaxEvents 10 -ErrorAction SilentlyContinue
if (-not $power) { Write-Host '  none' } else {
    foreach ($e in $power) { Write-Host "  $($e.TimeCreated.ToString('yyyy-MM-dd HH:mm:ss'))" }
}

Write-Host ''
Write-Host '== crash dumps ==' -ForegroundColor Cyan
$dumps = @(Get-ChildItem 'C:\Windows\Minidump' -Filter '*.dmp' -ErrorAction SilentlyContinue)
if ($dumps.Count -eq 0) { Write-Host '  none in C:\Windows\Minidump' } else {
    foreach ($d in $dumps) { Write-Host "  $($d.LastWriteTime.ToString('yyyy-MM-dd HH:mm:ss'))  $($d.Name)  $([math]::Round($d.Length/1KB)) KB" }
}
if (Test-Path 'C:\Windows\MEMORY.DMP') {
    $m = Get-Item 'C:\Windows\MEMORY.DMP'
    Write-Host "  C:\Windows\MEMORY.DMP  $([math]::Round($m.Length/1MB)) MB  $($m.LastWriteTime)"
}

Write-Host ''
Write-Host '== memory, page file, disk ==' -ForegroundColor Cyan
$os = Get-CimInstance Win32_OperatingSystem
Write-Host "  RAM total      : $([math]::Round($os.TotalVisibleMemorySize/1MB,2)) GB"
Write-Host "  RAM free now   : $([math]::Round($os.FreePhysicalMemory/1MB,2)) GB"
$auto = (Get-CimInstance Win32_ComputerSystem).AutomaticManagedPagefile
Write-Host "  page file      : automatic = $auto"
foreach ($pf in Get-CimInstance Win32_PageFileUsage -ErrorAction SilentlyContinue) {
    Write-Host "                   $($pf.Name)  allocated $([math]::Round($pf.AllocatedBaseSize/1KB,2)) GB, peak $([math]::Round($pf.PeakUsage/1KB,2)) GB"
}
foreach ($d in Get-CimInstance Win32_LogicalDisk -Filter 'DriveType=3') {
    Write-Host "  disk $($d.DeviceID)        : $([math]::Round($d.Size/1GB,2)) GB total, $([math]::Round($d.FreeSpace/1GB,2)) GB free"
}

Write-Host ''
Write-Host '== Windows licensing (clipsp.sys is its driver) ==' -ForegroundColor Cyan
$lic = Get-CimInstance SoftwareLicensingProduct -ErrorAction SilentlyContinue |
    Where-Object { $_.PartialProductKey -and $_.Name -like 'Windows*' }
if (-not $lic) { Write-Host '  no licensed Windows product reported (unactivated)' } else {
    foreach ($l in $lic) {
        $status = switch ($l.LicenseStatus) {
            0 { 'Unlicensed' } 1 { 'Licensed' } 2 { 'Out-of-box grace' } 3 { 'Out-of-tolerance grace' }
            4 { 'Non-genuine grace' } 5 { 'Notification (not activated)' } 6 { 'Extended grace' }
            default { "unknown ($($l.LicenseStatus))" }
        }
        Write-Host "  $($l.Name): $status"
        if ($l.GracePeriodRemaining) { Write-Host "      grace remaining: $([math]::Round($l.GracePeriodRemaining/1440,1)) days" }
    }
}

Write-Host ''
Write-Host '== the agent service ==' -ForegroundColor Cyan
$svc = Get-Service -Name 'RansomGuardAgent' -ErrorAction SilentlyContinue
if (-not $svc) { Write-Host '  not installed' } else { Write-Host "  $($svc.Name): $($svc.Status), start = $((Get-CimInstance Win32_Service -Filter "Name='RansomGuardAgent'").StartMode)" }
