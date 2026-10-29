<#
.SYNOPSIS
    Give this workstation a page file big enough that Windows stops paging out its own drivers.
    Run inside the VM, from the bench disc, as administrator.

.DESCRIPTION
    PC-LAB stopped with DRIVER_PORTION_MUST_BE_NONPAGED in clipsp.sys on 2026-10-28. That bug
    check means a part of a driver that must stay in memory was paged out and could not be
    brought back. clipsp.sys is Windows' own Client License Service driver -- nothing this
    product installs -- but the condition that provokes it is ours to remove: 4 GB of RAM and an
    automatically managed page file that Windows had sized at 0.69 GB, on a machine also running
    a browser, an installer and the agent.

    A fixed page file of several gigabytes costs nothing but disk and takes that condition away.
    It is the smallest change that addresses the cause; the other half is RAM, which is set on
    the host (VBoxManage modifyvm <vm> --memory).

    A restart is needed before the new size takes effect, and this script does not restart
    anything: a workstation decides when it reboots.

.PARAMETER SizeMB
    Initial and maximum size, in megabytes. 4096 by default.
#>
[CmdletBinding()]
param(
    [int] $SizeMB = 4096
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$before = Get-CimInstance Win32_PageFileUsage -ErrorAction SilentlyContinue
Write-Host 'before:' -ForegroundColor Cyan
if (-not $before) {
    Write-Host '  no page file in use'
} else {
    foreach ($p in $before) {
        Write-Host "  $($p.Name)  allocated $([math]::Round($p.AllocatedBaseSize/1024,2)) GB, peak $([math]::Round($p.PeakUsage/1024,2)) GB"
    }
}
$auto = (Get-CimInstance Win32_ComputerSystem).AutomaticManagedPagefile
Write-Host "  automatic = $auto"

# Windows ignores a fixed size while it manages the page file itself, so that goes first.
if ($auto) {
    $cs = Get-CimInstance Win32_ComputerSystem
    Set-CimInstance -InputObject $cs -Property @{ AutomaticManagedPagefile = $false } | Out-Null
    Write-Host ''
    Write-Host 'automatic management turned off' -ForegroundColor Yellow
}

# Win32_PageFileSetting takes UInt32 and refuses the Int32 PowerShell hands it ("Type mismatch
# for property InitialSize"), which left this machine with automatic management off and no size
# set -- a workstation that boots with no page file at all. The registry is what that class
# writes anyway, and it takes the value without argument.
$key = 'HKLM:\SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management'
Set-ItemProperty -Path $key -Name 'PagingFiles' -Value @("C:\pagefile.sys $SizeMB $SizeMB") -Type MultiString

Write-Host ''
Write-Host 'after (takes effect on the next restart):' -ForegroundColor Cyan
foreach ($entry in (Get-ItemProperty -Path $key -Name 'PagingFiles').PagingFiles) {
    Write-Host "  $entry   (file, initial MB, maximum MB)"
}
Write-Host "  automatic = $((Get-CimInstance Win32_ComputerSystem).AutomaticManagedPagefile)"

$free = [math]::Round((Get-CimInstance Win32_LogicalDisk -Filter "DeviceID='C:'").FreeSpace / 1GB, 2)
Write-Host ''
Write-Host "disk C: has $free GB free; the page file needs $([math]::Round($SizeMB/1024,2)) GB of it."
Write-Host 'Restart the workstation when convenient. Nothing else was changed.'
