<#
.SYNOPSIS
    Give a bench workstation room to install the agent again. Run inside the VM, as administrator.

.DESCRIPTION
    PC-RECEPTION reached **0 bytes free** on a 25 GB disc on 2026-10-27, and the installer could
    not extract its 94 MB payload. Nothing said so: the install simply never reached "Workstation
    enrolled", and Windows put a "Low Disk Space" balloon in the corner that is easy to read past.

    This clears the two caches Windows is designed to let you clear, reports what it got back, and
    touches nothing else:

      1. the Windows Update download cache (C:\Windows\SoftwareDistribution\Download) -- Windows
         re-downloads whatever it still needs;
      2. the machine-wide temporary folder (C:\Windows\Temp), and each user's;
      3. the recycle bin;
      4. hibernation, which costs a hidden file the size of the machine's memory. A bench VM is
         never hibernated, and `powercfg /h on` puts it back.

    It does not touch user documents, the FICTIONAL hospital files, the agent, or anything the
    bench tests depend on. It prints the free space before and after, so a run that frees nothing
    says so rather than looking like it worked.

.EXAMPLE
    pwsh deployment/bench/build-script-iso.ps1 -VmName PC-RECEPTION -ScriptName free-disk.ps1 -Label DISK
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Continue'
Set-StrictMode -Version Latest

function FreeGb {
    $c = Get-PSDrive C
    return [math]::Round($c.Free / 1GB, 2)
}

function Clear-Folder([string] $Path, [string] $What) {
    Write-Host ""
    Write-Host "== $What"
    Write-Host "   $Path"
    if (-not (Test-Path $Path)) { Write-Host '   not there, nothing to do'; return }
    $before = FreeGb
    Get-ChildItem $Path -Force -ErrorAction SilentlyContinue |
        ForEach-Object { Remove-Item $_.FullName -Recurse -Force -ErrorAction SilentlyContinue }
    $after = FreeGb
    Write-Host ("   freed {0} GB (a file still open is kept; that is normal)" -f [math]::Round($after - $before, 2))
}

$start = FreeGb
Write-Host ("free before : {0} GB" -f $start)

Write-Host ''
Write-Host '== stopping Windows Update while its cache is cleared'
Stop-Service wuauserv -Force -ErrorAction SilentlyContinue
Stop-Service bits -Force -ErrorAction SilentlyContinue

Clear-Folder 'C:\Windows\SoftwareDistribution\Download' 'Windows Update downloads'
Clear-Folder 'C:\Windows\Temp' 'machine-wide temporary files'
foreach ($who in (Get-ChildItem 'C:\Users' -Directory -Force -ErrorAction SilentlyContinue)) {
    Clear-Folder (Join-Path $who.FullName 'AppData\Local\Temp') "temporary files of $($who.Name)"
}

Write-Host ''
Write-Host '== the recycle bin'
$binBefore = FreeGb
Clear-RecycleBin -Force -ErrorAction SilentlyContinue
Write-Host ("   freed {0} GB" -f [math]::Round((FreeGb) - $binBefore, 2))

Write-Host ''
Write-Host '== hibernation (a hidden file the size of this machine memory; never used on a bench VM)'
$hibBefore = FreeGb
& powercfg /h off 2>&1 | ForEach-Object { Write-Host "   $_" }
Write-Host ("   freed {0} GB -- put it back with: powercfg /h on" -f [math]::Round((FreeGb) - $hibBefore, 2))

Start-Service wuauserv -ErrorAction SilentlyContinue
Start-Service bits -ErrorAction SilentlyContinue
Write-Host ''
Write-Host '== Windows Update started again'

$end = FreeGb
Write-Host ''
Write-Host ("free after  : {0} GB  (gained {1} GB)" -f $end, [math]::Round($end - $start, 2))
if ($end -lt 1) {
    Write-Host ''
    Write-Host 'Still under 1 GB free. The agent installer needs about 250 MB to unpack and install,' -ForegroundColor Yellow
    Write-Host 'so find the space elsewhere before installing: the biggest folders under C:\ are' -ForegroundColor Yellow
    Get-ChildItem C:\ -Directory -Force -ErrorAction SilentlyContinue | ForEach-Object {
        $measured = Get-ChildItem $_.FullName -Recurse -File -Force -ErrorAction SilentlyContinue |
            Measure-Object Length -Sum
        $size = if ($measured -and $measured.Sum) { $measured.Sum } else { 0 }
        [pscustomobject]@{ Folder = $_.Name; GB = [math]::Round(($size / 1GB), 2) }
    } | Sort-Object GB -Descending | Select-Object -First 8 | Format-Table -AutoSize
}
