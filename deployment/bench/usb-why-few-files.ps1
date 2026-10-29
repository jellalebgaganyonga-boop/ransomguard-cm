<#
.SYNOPSIS
    Why did USB GUARD see fewer files on this key than the host can? Run inside the VM.

.DESCRIPTION
    On 2026-10-29 the host counted 773 files on a key and the agent reported "2 files scanned, 2
    skipped". The scanner does recurse (RecurseSubdirectories = true, IgnoreInaccessible = true),
    so the difference is not the scanner walking the wrong tree: it is this workstation not being
    allowed to read the rest, or the files carrying an attribute the scan skips on purpose.

    This prints what is at the top of the key with each entry's attributes, how many files the
    workstation can actually see, and -- for each folder -- the error, in full, when it cannot be
    read. An access denial has to be printed; swallowing it is what turns a permissions problem
    into a wrong number.

    Plain Windows PowerShell 5.1: the bench VMs have no PowerShell 7.

    Read-only.
#>
param([string] $Drive = 'E')

$ErrorActionPreference = 'Continue'
$root = "${Drive}:\"

Write-Host "== the top of $root ==" -ForegroundColor Cyan
Get-ChildItem $root -Force -ErrorAction SilentlyContinue | ForEach-Object {
    $kind = 'file'
    if ($_.PSIsContainer) { $kind = 'dir ' }
    Write-Host ("  {0} {1,-44} {2}" -f $kind, $_.Name, $_.Attributes)
}

Write-Host ''
Write-Host '== how much of it this workstation can see ==' -ForegroundColor Cyan
$visible = @(Get-ChildItem $root -Recurse -File -Force -ErrorAction SilentlyContinue)
$sum = ($visible | Measure-Object Length -Sum).Sum
if ($null -eq $sum) { $sum = 0 }
Write-Host "  $($visible.Count) files, $sum bytes"

$system = @($visible | Where-Object { $_.Attributes -band [IO.FileAttributes]::System })
$canary = @($visible | Where-Object { $_.Name -like '0001_*' })
$big    = @($visible | Where-Object { $_.Length -gt 100MB })
Write-Host "    marked System : $($system.Count)  (the scan skips these on purpose)"
Write-Host "    decoy names   : $($canary.Count)  (0001_*, the agent's own)"
Write-Host "    over 100 MB   : $($big.Count)  (over MaxFileSizeForScanMB)"

Write-Host ''
Write-Host '== each folder, and the error if it cannot be read ==' -ForegroundColor Cyan
$dirs = @(Get-ChildItem $root -Force -ErrorAction SilentlyContinue | Where-Object { $_.PSIsContainer })
if ($dirs.Count -eq 0) { Write-Host '  no folders at the top of this key' }
foreach ($d in $dirs) {
    Write-Host ''
    Write-Host "  $($d.Name)   attributes: $($d.Attributes)"
    try {
        $inside = @(Get-ChildItem $d.FullName -Recurse -File -Force -ErrorAction Stop)
        Write-Host "    readable: $($inside.Count) files"
    } catch {
        Write-Host "    NOT readable: $($_.Exception.Message)" -ForegroundColor Yellow
    }
    try {
        $acl = Get-Acl $d.FullName
        Write-Host "    owner: $($acl.Owner)"
    } catch {
        Write-Host "    owner: cannot read the permissions" -ForegroundColor Yellow
    }
}
