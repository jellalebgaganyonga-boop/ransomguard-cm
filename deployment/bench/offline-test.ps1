<#
.SYNOPSIS
    Bench test: does the workstation still protect itself with no network?

.DESCRIPTION
    Cuts both of the VM's adapters, runs the harmless trigger, and watches what happens locally
    and what the console says. Then puts the network back and checks that the alerts raised while
    it was cut arrive carrying the time they actually happened, not the time they were delivered.

    Every stage is screenshotted. Nothing is asserted that was not read back from the GRID.

    **Before running it**, leave an elevated command prompt open on the VM's desktop, at a prompt,
    with nothing else in front of it. The script types into whatever has focus: these VMs have no
    Guest Additions, so there is no way to open a window and be sure it is the one being typed
    into. The first attempt at this test opened a second prompt with Win+R while the first was
    still there, and `keyboardputstring` dropped characters on the way -- the trigger never ran
    and the script spent six minutes waiting for a disconnection that meant nothing. So the
    command is tab-completed from seven characters rather than typed in full, and the line is
    photographed before Enter is pressed.

.PARAMETER Vm
    The workstation. PC-LAB by default.

.PARAMETER ShotDir
    Where the pictures go.

.EXAMPLE
    pwsh deployment/bench/offline-test.ps1 -ShotDir docs/screenshots/real/offline
#>
[CmdletBinding()]
param(
    [string] $Vm = 'PC-LAB',
    [Parameter(Mandatory)] [string] $ShotDir,
    [string] $VBoxManage = 'C:\Program Files\Oracle\VirtualBox\VBoxManage.exe'
)

$ErrorActionPreference = 'Continue'
New-Item -ItemType Directory -Force $ShotDir | Out-Null

function Shoot($name) {
    & $VBoxManage controlvm $Vm screenshotpng (Join-Path $ShotDir "$name.png") 2>$null | Out-Null
    Write-Host "    picture: $name.png"
}
function Keys([string[]] $c) { & $VBoxManage controlvm $Vm keyboardputscancode @c 2>$null | Out-Null }
function SendText([string] $t) { & $VBoxManage controlvm $Vm keyboardputstring $t 2>$null | Out-Null }
function Enter { Keys @('1c', '9c') }
function Tab { Keys @('0f', '8f') }
function Link([string] $state) {
    & $VBoxManage controlvm $Vm setlinkstate1 $state 2>$null | Out-Null
    & $VBoxManage controlvm $Vm setlinkstate2 $state 2>$null | Out-Null
}
function Agent { & python -I (Join-Path $PSScriptRoot 'offline_probe.py') @args 2>&1 | ForEach-Object { [string]$_ } }
function Say([string] $t) { Write-Host $t }

Say '=== 1. the workstation before the cut ==='
Agent status $Vm
Shoot '30-before-the-cut'

Say ''
Say '=== 2. cutting both adapters ==='
Link 'off'
Start-Sleep -Seconds 5
Say '    nic1 and nic2 are off'

Say ''
Say '=== 3. the trigger, with no network ==='
SendText 'D:\trig'
Start-Sleep -Seconds 1
Tab                       # the disc holds trigger-sentinel.cmd and .ps1; .cmd completes first
Start-Sleep -Seconds 2
Shoot '31-trigger-typed-offline'
Enter
Start-Sleep -Seconds 50
Shoot '32-suspended-with-no-network'
Say '    the trigger has run; see 31- and 32-'

Say ''
Say '=== 4. what the console says while it is cut ==='
Say '    waiting six minutes for the GRID to call it disconnected (it waits five on purpose)'
for ($i = 1; $i -le 7; $i++) {
    Start-Sleep -Seconds 60
    Say "    +$i min: $((Agent status $Vm) -join ' ')"
}
Shoot '33-still-frozen-offline'

Say ''
Say '=== 5. the network back ==='
Link 'on'
Say '    nic1 and nic2 are on'
for ($i = 1; $i -le 10; $i++) {
    Start-Sleep -Seconds 30
    $line = (Agent status $Vm) -join ' '
    Say "    +$($i*30)s: $line"
    if ($line -match 'active') { break }
}
Shoot '34-after-the-network-came-back'

Say ''
Say '=== 6. did the alerts arrive, with the time they happened? ==='
Agent alerts $Vm

Say ''
Say '=== 7. Resume from the console ==='
Agent suspensions $Vm
Agent resume $Vm
Start-Sleep -Seconds 60
Shoot '35-after-resume'
Agent suspensions $Vm
