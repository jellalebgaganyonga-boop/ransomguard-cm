<#
.SYNOPSIS
    The decoy files SENTINEL is watching on this workstation. Run inside the VM.

.DESCRIPTION
    For the demonstration: before anybody attacks anything, show what is being protected. The
    agent plants decoys -- "canaries" -- in the folders ransomware goes for first, named like the
    documents a hospital keeps, so that anything sweeping a folder for patient files finds one of
    these before it finds a real one.

    Nothing here is a real patient record. The names are deliberate and the contents say so.

    This prints where they are, how big they are, when they were last touched, and the first
    lines of one of them. It reads; it writes nothing, so running it raises no alert.
#>
[CmdletBinding()]
param(
    [switch] $ShowContent = $true
)

$ErrorActionPreference = 'Continue'
Set-StrictMode -Version Latest

$folders = @(
    (Join-Path $env:USERPROFILE 'Desktop\RansomGuard-TestZone'),
    (Join-Path $env:USERPROFILE 'Desktop'),
    (Join-Path $env:USERPROFILE 'Documents')
)

Write-Host '== the decoy files SENTINEL is watching ==' -ForegroundColor Cyan
Write-Host ''

$all = @()
foreach ($folder in $folders) {
    if (-not (Test-Path $folder)) { continue }
    $found = @(Get-ChildItem -Path $folder -Filter '0001_*' -File -Force -ErrorAction SilentlyContinue)
    if ($found.Count -eq 0) { continue }
    Write-Host "  $folder" -ForegroundColor Yellow
    foreach ($f in $found) {
        Write-Host ('    {0,-42} {1,6} bytes   last written {2:HH:mm:ss}' -f $f.Name, $f.Length, $f.LastWriteTime)
        $all += $f
    }
    Write-Host ''
}

if ($all.Count -eq 0) {
    Write-Host '  none found.' -ForegroundColor Red
    Write-Host '  SENTINEL plants them when the agent starts: is the agent installed, and has it enrolled?'
    return
}

Write-Host ("  {0} decoys, in {1} folders." -f $all.Count, ($all | Group-Object DirectoryName).Count)
Write-Host '  They are named like the documents a hospital keeps, so that anything sweeping a'
Write-Host '  folder for patient files meets one of these before it meets a real one.'

if ($ShowContent) {
    $one = $all | Sort-Object Name | Select-Object -First 1
    Write-Host ''
    Write-Host "== what is inside one of them ($($one.Name)) ==" -ForegroundColor Cyan
    Get-Content -Path $one.FullName -TotalCount 12 | ForEach-Object { Write-Host "  $_" }
    Write-Host ''
    Write-Host '  Not a real record. Writing to this file is what raises the alert.'
}
