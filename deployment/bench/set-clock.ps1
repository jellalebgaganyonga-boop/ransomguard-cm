<#
.SYNOPSIS
    Set a bench VM's clock. Run inside the VM, from the bench disc, as administrator.

.DESCRIPTION
    The bench VMs have no Guest Additions, so nothing keeps their clocks with the host's. They
    are set from the host once, when the VM starts, and then free-run: PC-LAB was five hours and
    eighteen minutes behind on 2026-10-27.

    That is not cosmetic. The time an alert was detected is the workstation's own, and it is the
    whole point of the offline test -- an alert raised while the network was cut has to arrive
    with the time it happened, not the time it was delivered. A workstation five hours behind
    makes every alert look five hours old, on the console and in the demo.

    The time is passed in rather than read from anywhere, because the VM has nothing to read it
    from: the caller takes it from the host.

.PARAMETER Utc
    The current UTC time, as 'yyyy-MM-ddTHH:mm:ssZ'.

.EXAMPLE
    pwsh deployment/bench/build-script-iso.ps1 -VmName PC-LAB -ScriptName set-clock.ps1 `
        -Arguments "-Utc $((Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ'))" -Label CLOCK
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $Utc
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$target = [DateTime]::Parse($Utc, [Globalization.CultureInfo]::InvariantCulture,
    [Globalization.DateTimeStyles]::AdjustToUniversal -bor [Globalization.DateTimeStyles]::AssumeUniversal)

$before = (Get-Date).ToUniversalTime()
Write-Host ("before : {0:yyyy-MM-dd HH:mm:ss} UTC" -f $before)
Write-Host ("asked  : {0:yyyy-MM-dd HH:mm:ss} UTC" -f $target)
Write-Host ("off by : {0}" -f ($target - $before))

Set-Date -Date $target.ToLocalTime()

$after = (Get-Date).ToUniversalTime()
Write-Host ("after  : {0:yyyy-MM-dd HH:mm:ss} UTC" -f $after)
Write-Host ''
Write-Host 'The agent reads the clock for every alert it raises. Nothing else is changed.'
