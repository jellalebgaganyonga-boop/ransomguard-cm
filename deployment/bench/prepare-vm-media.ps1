<#
.SYNOPSIS
    Put a fresh installer and the SENTINEL trigger into the bench VM's DVD drive, as a USB key would.

.DESCRIPTION
    1. Downloads a double-click installer from the GRID (fetch_installer.py: same calls as the
       console, TLS validated against the GRID's CA). Its enrollment code is valid 30 minutes.
    2. Builds an ISO with the installer, trigger-sentinel.ps1/.cmd and a LISEZ-MOI.txt.
    3. Inserts it into the VM's DVD drive (VirtualBox, works on a running VM).

    Why a disc: the bench VMs have no Guest Additions (no shared folder), and the VM does not
    trust the GRID's CA until the installer has run, so it cannot fetch from the console first.
    A disc, like a USB key, carries no Mark-of-the-Web: Windows shows the UAC prompt
    ("Publisher: Unknown"), not SmartScreen.

    Credentials come from the environment, required: RG_TENANT, RG_ADMIN_EMAIL, RG_ADMIN_PASSWORD.

    The ISO is written under agent/packages/bench/ -- inside the repository folder, the only one
    the endpoint-protection exclusion covers, and gitignored.

.EXAMPLE
    $env:RG_TENANT = 'hopital-yde'; $env:RG_ADMIN_EMAIL = 'admin@hopital-yde.cm'; $env:RG_ADMIN_PASSWORD = '...'
    pwsh deployment/bench/prepare-vm-media.ps1 -VmName win
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $VmName,
    [string] $GridUrl = 'https://192.168.56.1:8443',
    [string] $AgentHost = '192.168.56.1',
    [int]    $AgentPort = 443,
    [string] $VBoxManage = 'C:\Program Files\Oracle\VirtualBox\VBoxManage.exe',
    [string] $Python = 'python'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$Repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$Ca = Join-Path $Repo 'deployment\grid\pki\ca.crt'
$Work = Join-Path $Repo 'agent\packages\bench'
$Stage = Join-Path $Work 'media'
$Iso = Join-Path $Work 'ransomguard-bench.iso'

Write-Host "VM            : $VmName"
Write-Host "GRID console  : $GridUrl"
Write-Host "agent channel : https://${AgentHost}:$AgentPort"
Write-Host "CA            : $Ca"
Write-Host "ISO           : $Iso"
Write-Host ''

if (-not (Test-Path $Ca)) { throw "no GRID CA at ${Ca}: run deployment/grid/pki/init-pki.sh first" }

# Eject first: VirtualBox holds the ISO open while it is inserted, and it is about to be rewritten.
& $VBoxManage storageattach $VmName --storagectl SATA --port 1 --device 0 --type dvddrive --medium emptydrive --forceunmount 2>$null | Out-Null

if (Test-Path $Stage) { Remove-Item -Recurse -Force $Stage }
New-Item -ItemType Directory -Force $Stage | Out-Null

Write-Host '[1/3] installer from the GRID'
& $Python -I (Join-Path $PSScriptRoot 'fetch_installer.py') --grid $GridUrl --agent-host $AgentHost `
    --agent-port $AgentPort --ca $Ca --out $Stage
if ($LASTEXITCODE -ne 0) { throw "fetch_installer.py failed ($LASTEXITCODE)" }

Copy-Item (Join-Path $PSScriptRoot 'trigger-sentinel.ps1') $Stage
Copy-Item (Join-Path $PSScriptRoot 'trigger-sentinel.cmd') $Stage
$installer = (Get-ChildItem $Stage -Filter 'RansomGuard-CM-Installeur-*.exe' | Select-Object -First 1).Name
@"
RansomGuard-CM -- banc de demonstration / demo bench

1. Double-cliquez / double-click: $installer
   Windows demande l'elevation (UAC, "Editeur : inconnu" / "Publisher: Unknown") : Oui / Yes.
   Le code d'enrolement expire 30 minutes apres la preparation de ce disque.
   The enrollment code expires 30 minutes after this disc was prepared.

2. Attendez "Poste enrole" / wait for "enrolled". Le poste apparait dans la console.

3. Double-cliquez / double-click: trigger-sentinel.cmd
   Ecrit dans UN fichier leurre de SENTINEL. Inoffensif. / Writes into ONE SENTINEL decoy. Harmless.
"@ | Out-File -Encoding ascii (Join-Path $Stage 'LISEZ-MOI.txt')

Write-Host '[2/3] ISO'
if (-not ('IsoStreamWriter' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
public static class IsoStreamWriter {
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
$image.FileSystemsToCreate = 7          # ISO 9660 + Joliet + UDF
$image.VolumeName = 'RANSOMGUARD'
$image.Root.AddTree($Stage, $false)
if (Test-Path $Iso) { Remove-Item -Force $Iso }
[IsoStreamWriter]::Save($image.CreateResultImage().ImageStream, $Iso)
Write-Host ("        {0:N1} MB" -f ((Get-Item $Iso).Length / 1MB))

Write-Host "[3/3] insert into $VmName"
& $VBoxManage storageattach $VmName --storagectl SATA --port 1 --device 0 --type dvddrive --medium $Iso --forceunmount
if ($LASTEXITCODE -ne 0) { throw "VBoxManage storageattach failed ($LASTEXITCODE)" }

Write-Host ''
Write-Host "Done. In the VM, open the DVD drive (usually D:) and follow LISEZ-MOI.txt, within 30 minutes."
