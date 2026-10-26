<#
.SYNOPSIS
    Stop this workstation falling asleep, and say what it was doing before.

.DESCRIPTION
    Run inside the VM, from the bench disc, as administrator. A sleeping workstation is an
    unprotected one: Windows suspended PC-LAB after about an hour idle on 2026-10-26, VirtualBox
    still reported the VM "running", and the service still read RUNNING when it woke -- but
    nothing ran in between. The heartbeat stopped, the GRID marked the workstation disconnected
    after its five minutes, and it came back on its own when somebody touched the keyboard. The
    console was right and the agent was not at fault; the machine really was off.

    bench-bootstrap.ps1 does this for a workstation it sets up. This is the same thing for one
    that already exists, without touching anything else on it.

    The screen is left alone: a dark monitor costs nothing and nobody watches one overnight.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

Write-Host 'Before (0x00000000 is never; anything else is a countdown to being unprotected):'
& powercfg.exe /query SCHEME_CURRENT SUB_SLEEP STANDBYIDLE |
    Select-String -Pattern 'Power Setting Index' | ForEach-Object { "      $($_.Line.Trim())" }

foreach ($setting in @(
        @{ Flag = 'standby-timeout-ac'; What = 'sleep on mains' },
        @{ Flag = 'standby-timeout-dc'; What = 'sleep on battery' },
        @{ Flag = 'hibernate-timeout-ac'; What = 'hibernate on mains' },
        @{ Flag = 'hibernate-timeout-dc'; What = 'hibernate on battery' })) {
    & powercfg.exe /change $setting.Flag 0 2>&1 | Out-Null
    Write-Host "      $($setting.What): never"
}

& powercfg.exe /hibernate off 2>&1 | Out-Null
Write-Host '      hibernation: off'

Write-Host ''
Write-Host 'After (0x00000000 is never):'
& powercfg.exe /query SCHEME_CURRENT SUB_SLEEP STANDBYIDLE |
    Select-String -Pattern 'Power Setting Index' | ForEach-Object { "      $($_.Line.Trim())" }

Write-Host ''
Write-Host 'This workstation will not sleep. The screen may still go dark.'
