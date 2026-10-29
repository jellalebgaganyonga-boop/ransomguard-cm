<#
.SYNOPSIS
    What USB GUARD did with the key that is plugged in. Run inside the VM, from the bench disc.

.DESCRIPTION
    The console only shows what the agent decided to raise, and a key that is left alone raises
    nothing -- which is the right behaviour and no evidence at all. This prints the three things
    that together say the module ran and chose to do nothing:

      1. the removable volumes Windows sees, with their free space and whether they are read-only
         (the shipped Permissive mode answers a Critical finding by making the drive read-only,
         so "still writable" is the thing to show);
      2. the USB GUARD lines of the agent's own log, in order;
      3. the files on the key, counted, so the same count can be compared with the host's.

    Read-only. It writes nothing to the key and nothing to the workstation.
#>
[CmdletBinding()]
param(
    [string] $LogDirectory = 'C:\ProgramData\RansomGuard-CM\logs',
    [int] $Lines = 14
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$volumeReport = {
Write-Host '== removable volumes ==' -ForegroundColor Cyan
$found = $false
foreach ($v in Get-Volume | Where-Object { $_.DriveType -eq 'Removable' }) {
    $found = $true
    $letter = if ($v.DriveLetter) { "$($v.DriveLetter):" } else { '(no letter)' }
    $size = if ($v.Size) { [math]::Round($v.Size / 1GB, 3) } else { 0 }
    $free = if ($v.SizeRemaining) { [math]::Round($v.SizeRemaining / 1GB, 3) } else { 0 }
    Write-Host "  $letter  label='$($v.FileSystemLabel)'  $($v.FileSystem)  $size GB, $free GB free"

    # Read-only is the question. A Critical finding in Permissive mode sets exactly this.
    $disk = Get-Partition -DriveLetter $v.DriveLetter -ErrorAction SilentlyContinue |
        Get-Disk -ErrorAction SilentlyContinue
    if ($disk) {
        Write-Host "      disk $($disk.Number): read-only = $($disk.IsReadOnly), offline = $($disk.IsOffline)"
    }

    if ($v.DriveLetter) {
        # -Force, because without it this counts only what is not hidden. On 2026-10-29 this
        # line reported "4 files" for a key holding 773: the other 769 were in a Hidden + System
        # folder, which is exactly the part worth counting on a USB key.
        $files = Get-ChildItem "$($v.DriveLetter):\" -Recurse -File -Force -ErrorAction SilentlyContinue
        $bytes = ($files | Measure-Object Length -Sum).Sum
        Write-Host "      files: $($files.Count), bytes: $bytes"
    }
}
if (-not $found) { Write-Host '  none: no removable volume is mounted' -ForegroundColor Yellow }
}

Write-Host ''
Write-Host '== what USB GUARD logged ==' -ForegroundColor Cyan
$log = Get-ChildItem $LogDirectory -Filter 'agent-*.log' -ErrorAction SilentlyContinue |
    Sort-Object LastWriteTime | Select-Object -Last 1
if (-not $log) {
    Write-Host "  no agent log in $LogDirectory" -ForegroundColor Yellow
} else {
    Write-Host "  $($log.FullName)"
    $shown = 0
    $lines_out = @()
    foreach ($line in Get-Content $log.FullName) {
        if (-not $line.Trim().StartsWith('{')) { continue }
        try { $e = $line | ConvertFrom-Json } catch { continue }
        $text = [string]$e.'@mt'
        if ($text -notmatch 'USB|usb|removable|Removable') { continue }
        foreach ($p in $e.PSObject.Properties) {
            if ($p.Name -like '@*') { continue }
            $text = $text -replace ("\{" + [regex]::Escape($p.Name) + "(:[^}]*)?\}"), [string]$p.Value
        }
        $when = if ($e.'@t') { ([datetime]$e.'@t').ToLocalTime().ToString('HH:mm:ss') } else { '--:--:--' }
        $lines_out += "  $when  $text"
        $shown++
    }
    if ($shown -eq 0) {
        Write-Host '  nothing about USB in this log file' -ForegroundColor Yellow
    } else {
        # The scan lines are what matter; the heartbeats before them are not worth a screen.
        $lines_out | Where-Object { $_ -match 'SCAN|connected' } | Select-Object -Last $Lines |
            ForEach-Object { Write-Host $_ }
    }
}

Write-Host ''
& $volumeReport
