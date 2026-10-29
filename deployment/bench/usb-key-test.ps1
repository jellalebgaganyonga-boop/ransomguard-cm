<#
.SYNOPSIS
    Test USB GUARD with a real key: hand it to a bench VM, then give it back and prove it is
    unchanged. Run on the HOST.

.DESCRIPTION
    Two steps, because the interesting part happens in between and a person has to watch it.

        pwsh deployment/bench/usb-key-test.ps1 -Attach
        ... in the VM: D:\run.cmd  (the evidence disc this inserts) ...
        pwsh deployment/bench/usb-key-test.ps1 -Detach

    -Attach measures the key on this machine first -- label, filesystem, free space, how many
    files and how many bytes -- writes that down, then hands the key to the VM and puts the
    evidence disc in its drive.

    -Detach takes the key back and measures it again, against what was written down. The point of
    the whole exercise is that the two measurements match: the shipped Permissive mode answers a
    Critical finding by making the drive read-only, so "nothing was written, nothing was removed,
    nothing was locked" is the claim, and this is how it is checked rather than asserted.

    The key is found by itself: the one removable volume on this machine, matched to VirtualBox's
    view of it by the serial number Windows reports. With more than one key plugged in, say which
    with -DriveLetter.

    Nothing here writes to the key.

.PARAMETER VmName
    The workstation to hand the key to. PC-LAB by default.

.PARAMETER DriveLetter
    Which removable drive, when this machine has more than one.

.PARAMETER Uuid
    The VirtualBox device, when the serial numbers cannot be matched (it says so if they cannot).

.EXAMPLE
    pwsh deployment/bench/usb-key-test.ps1 -Attach -VmName PC-LAB
#>
[CmdletBinding(DefaultParameterSetName = 'Attach')]
param(
    [Parameter(ParameterSetName = 'Attach')] [switch] $Attach,
    [Parameter(ParameterSetName = 'Detach')] [switch] $Detach,
    [string] $VmName = 'PC-LAB',
    [string] $DriveLetter,
    [string] $Uuid,
    [string] $VBoxManage = 'C:\Program Files\Oracle\VirtualBox\VBoxManage.exe'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$Repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$State = Join-Path $env:TEMP 'ransomguard-usb-key-test.json'

function Measure-Key([string] $letter) {
    $v = Get-Volume -DriveLetter $letter -ErrorAction Stop
    $files = @(Get-ChildItem "${letter}:\" -Recurse -File -Force -ErrorAction SilentlyContinue)
    $bytes = ($files | Measure-Object Length -Sum).Sum
    [pscustomobject]@{
        DriveLetter = $letter
        Label       = $v.FileSystemLabel
        FileSystem  = $v.FileSystem
        SizeGB      = [math]::Round($v.Size / 1GB, 3)
        FreeGB      = [math]::Round($v.SizeRemaining / 1GB, 3)
        Files       = $files.Count
        Bytes       = [int64]($bytes ?? 0)
    }
}

function Show-Key($m, [string] $when) {
    Write-Host "  $when" -ForegroundColor Cyan
    Write-Host "    $($m.DriveLetter):  label='$($m.Label)'  $($m.FileSystem)  $($m.SizeGB) GB, $($m.FreeGB) GB free"
    Write-Host "    $($m.Files) files, $($m.Bytes) bytes"
}

function Find-Removable([string] $wanted) {
    $volumes = @(Get-Volume | Where-Object { $_.DriveType -eq 'Removable' -and $_.DriveLetter })
    if ($wanted) {
        $v = $volumes | Where-Object { $_.DriveLetter -eq $wanted.TrimEnd(':') }
        if (-not $v) { throw "no removable drive $wanted on this machine" }
        return $v.DriveLetter
    }
    if ($volumes.Count -eq 0) { throw 'no removable drive on this machine: plug the key in first' }
    if ($volumes.Count -gt 1) {
        throw ("more than one removable drive (" + (($volumes.DriveLetter) -join ', ') + "): say which with -DriveLetter")
    }
    return $volumes[0].DriveLetter
}

function Find-UsbDevice([string] $letter) {
    # Windows spells the key's serial into the PNP device id of its disk; VirtualBox reports the
    # same serial. Matching on it is what keeps this from attaching the webcam.
    $serials = @()
    foreach ($disk in Get-CimInstance Win32_DiskDrive -ErrorAction SilentlyContinue) {
        if ($disk.InterfaceType -ne 'USB') { continue }
        $parts = @(Get-CimAssociatedInstance -InputObject $disk -ResultClassName Win32_DiskPartition -ErrorAction SilentlyContinue)
        foreach ($part in $parts) {
            $logical = @(Get-CimAssociatedInstance -InputObject $part -ResultClassName Win32_LogicalDisk -ErrorAction SilentlyContinue)
            if ($logical.DeviceID -contains "${letter}:") {
                $id = [string]$disk.PNPDeviceID
                $serials += ($id -split '\\')[-1] -replace '&\d+$', ''
            }
        }
    }

    $devices = @()
    $current = $null
    foreach ($line in (& $VBoxManage list usbhost)) {
        if ($line -match '^UUID:\s+(\S+)') {
            if ($current) { $devices += $current }
            $current = [pscustomobject]@{ Uuid = $Matches[1]; Product = ''; Manufacturer = ''; Serial = '' }
        } elseif ($current -and $line -match '^Product:\s+(.+?)\s*$')      { $current.Product = $Matches[1]
        } elseif ($current -and $line -match '^Manufacturer:\s+(.+?)\s*$') { $current.Manufacturer = $Matches[1]
        } elseif ($current -and $line -match '^SerialNumber:\s+(\S+)')     { $current.Serial = $Matches[1] }
    }
    if ($current) { $devices += $current }

    foreach ($serial in $serials) {
        $hit = $devices | Where-Object { $_.Serial -and $_.Serial -eq $serial }
        if ($hit) { return $hit[0] }
    }

    Write-Host '  could not match the key to a VirtualBox device by serial number.' -ForegroundColor Yellow
    Write-Host '  the devices this machine can hand over:'
    foreach ($d in $devices) {
        Write-Host ("    {0}  {1} {2}  serial {3}" -f $d.Uuid, $d.Manufacturer, $d.Product, $d.Serial)
    }
    throw 'say which with -Uuid'
}

if ($Detach) {
    if (-not (Test-Path $State)) { throw "no attach recorded: run -Attach first" }
    $before = Get-Content -Raw $State | ConvertFrom-Json

    Write-Host "taking the key back from $($before.VmName)" -ForegroundColor Cyan
    & $VBoxManage controlvm $before.VmName usbdetach $before.Uuid
    Start-Sleep -Seconds 8

    Write-Host ''
    Show-Key $before.Measurement 'before it went to the workstation'
    $after = Measure-Key $before.Measurement.DriveLetter
    Show-Key $after 'after it came back'

    Write-Host ''
    $same = ($after.Files -eq $before.Measurement.Files) -and ($after.Bytes -eq $before.Measurement.Bytes)
    if ($same) {
        Write-Host '  the same files and the same bytes: nothing was written, nothing was removed.' -ForegroundColor Green
    } else {
        Write-Host '  THE KEY CHANGED. Say so; do not explain it away.' -ForegroundColor Red
        exit 1
    }
    Remove-Item $State -Force
    return
}

# -- attach --------------------------------------------------------------------

$letter = Find-Removable $DriveLetter
$measurement = Measure-Key $letter
Write-Host "the key, on this machine" -ForegroundColor Cyan
Show-Key $measurement 'measured now'

$device = if ($Uuid) {
    [pscustomobject]@{ Uuid = $Uuid; Product = '(given)'; Manufacturer = ''; Serial = '' }
} else {
    Find-UsbDevice $letter
}
Write-Host ''
Write-Host "  VirtualBox device: $($device.Uuid)  $($device.Manufacturer) $($device.Product)"

@{ VmName = $VmName; Uuid = $device.Uuid; Measurement = $measurement } |
    ConvertTo-Json -Depth 4 | Set-Content -Path $State -Encoding utf8

Write-Host ''
Write-Host "handing it to $VmName" -ForegroundColor Cyan
& $VBoxManage controlvm $VmName usbattach $device.Uuid
Start-Sleep -Seconds 10

Write-Host '  inserting the evidence disc'
& $VBoxManage storageattach $VmName --storagectl SATA --port 1 --device 0 --medium emptydrive 2>$null
Start-Sleep -Seconds 5
& pwsh -NoProfile -File (Join-Path $PSScriptRoot 'build-script-iso.ps1') `
    -VmName $VmName -ScriptName 'usb-evidence.ps1' -Label 'USBCHK' | Out-Null

Write-Host ''
Write-Host '  In the VM, in an elevated prompt:  D:\run.cmd' -ForegroundColor Yellow
Write-Host '  Look for: "USB SCAN: Completed ... findings, severity: ..." and "read-only = False".'
Write-Host ''
Write-Host "  Then, back here:  pwsh deployment/bench/usb-key-test.ps1 -Detach"
