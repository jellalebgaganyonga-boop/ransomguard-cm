<#
.SYNOPSIS
    Put a bench script on a small ISO and insert it into a bench VM's DVD drive.

.DESCRIPTION
    The bench VMs have no Guest Additions, so the only way to run something inside one is to type
    it on the virtual keyboard with `VBoxManage controlvm keyboardputstring`. That is not
    reliable: over a long command it drops characters, duplicates them and leaks shift state --
    `RansomGuardAgent` arrived once as `ARDAgent`, and a mistyped command that still parses does
    something other than what was meant.

    So nothing long is ever typed. The script goes on a disc with a `run.cmd` beside it, and the
    only thing typed into the VM is `D:\run.cmd`, which is short enough to read back from a
    screenshot before pressing Enter.

    `run.cmd` ends with a pause, because the disc is also meant to be double-clicked by hand and
    a window that closes on its own takes its output with it. Driven from a shell, that pause
    looks like a hang: send one keypress when the work is done.

.PARAMETER VmName
    The VM to insert the disc into. It does not have to be running.

.PARAMETER ScriptName
    A .ps1 in deployment/bench, for example reset-agent.ps1.

.PARAMETER Arguments
    Passed to the script verbatim. Keep secrets out of it: the disc is written to
    agent/packages/bench, which is gitignored but is still a file on disk.

.EXAMPLE
    pwsh deployment/bench/build-script-iso.ps1 -VmName PC-LAB -ScriptName reset-agent.ps1 -Label RESET
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $VmName,
    [Parameter(Mandatory)] [string] $ScriptName,
    [string] $Arguments = '',
    [string] $Label = 'BENCH',
    [string] $VBoxManage = 'C:\Program Files\Oracle\VirtualBox\VBoxManage.exe'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$Repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$Source = Join-Path $PSScriptRoot $ScriptName
if (-not (Test-Path $Source)) { throw "no such bench script: $Source" }

$Work = Join-Path $Repo 'agent\packages\bench'
$Stage = Join-Path $Work "iso-$Label"
$Iso = Join-Path $Work "$Label.iso"

# Eject first. VirtualBox holds an inserted ISO open, so the file cannot be rewritten while the
# VM still has it; and a guest that is handed a swapped disc without an eject keeps showing the
# contents of the old one.
& $VBoxManage storageattach $VmName --storagectl SATA --port 1 --device 0 --type dvddrive --medium emptydrive --forceunmount 2>$null | Out-Null

if (Test-Path $Stage) { Remove-Item -Recurse -Force $Stage }
New-Item -ItemType Directory -Force $Stage | Out-Null
Copy-Item $Source $Stage

@"
@echo off
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0$ScriptName" $Arguments
echo.
echo ---- done, press any key ----
pause > nul
"@ | Out-File -Encoding ascii (Join-Path $Stage 'run.cmd')

# IMAPI2 hands back a COM IStream; PowerShell cannot copy one to a file on its own.
if (-not ('BenchIsoWriter' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
public static class BenchIsoWriter {
    public static void Save(object comStream, string path) {
        var stream = (IStream)comStream;
        var buffer = new byte[1 << 20];
        IntPtr read = Marshal.AllocHGlobal(sizeof(int));
        try {
            using (var file = File.Create(path)) {
                while (true) {
                    stream.Read(buffer, buffer.Length, read);
                    int n = Marshal.ReadInt32(read);
                    if (n <= 0) break;
                    file.Write(buffer, 0, n);
                }
            }
        } finally { Marshal.FreeHGlobal(read); }
    }
}
'@
}

$image = New-Object -ComObject IMAPI2FS.MsftFileSystemImage
$image.FileSystemsToCreate = 7   # ISO9660 + Joliet + UDF
$image.VolumeName = $Label
$image.Root.AddTree($Stage, $false)
if (Test-Path $Iso) { Remove-Item -Force $Iso }
[BenchIsoWriter]::Save($image.CreateResultImage().ImageStream, $Iso)

& $VBoxManage storageattach $VmName --storagectl SATA --port 1 --device 0 --type dvddrive --medium $Iso --forceunmount
if ($LASTEXITCODE -ne 0) { throw "VBoxManage storageattach failed ($LASTEXITCODE)" }

Write-Host "inserted $Iso into $VmName"
Write-Host 'In the VM, open an elevated prompt and run: D:\run.cmd'
