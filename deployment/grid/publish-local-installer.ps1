<#
.SYNOPSIS
    Publish the double-click installer parts for a GRID running on this machine.

.DESCRIPTION
    Does locally what the "Publish" job of .github/workflows/cd.yml does on the
    server, step for step, so the console's "Download the installer" has
    something to serve (ADR-027):

      RansomGuard-Agent-win-x64.zip          the agent, self-contained win-x64
      RansomGuard-Agent-win-x64.zip.sha256
      RansomGuard-Installer-stub.exe         the single-file stub the GRID appends to
      RansomGuard-Installer-stub.exe.sha256
      install.ps1                            the scripted (GPO/SCCM) path

    The output directory is the one docker-compose mounts as AGENT_PACKAGES_DIR
    (read-only into grid-api at /srv/agent-packages and into nginx at
    /usr/share/nginx/agent). The GRID also needs its CA at GRID_GRID_CA_CERT_PATH,
    which compose provides from deployment/grid/pki/ca.crt.

    Outputs stay inside the repository folder on purpose: it is the only folder
    the endpoint-protection exclusion covers. agent/packages/ is gitignored.

.PARAMETER OutDir
    Where to write the parts. Default: <repo>/agent/packages. Printed at the end.

.PARAMETER Configuration
    Build configuration. Default: Release, as the CD.

.EXAMPLE
    pwsh deployment/grid/publish-local-installer.ps1
#>
[CmdletBinding()]
param(
    [string] $OutDir,
    [string] $Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$Repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
if (-not $OutDir) { $OutDir = Join-Path $Repo 'agent\packages' }
$Publish = Join-Path $Repo 'agent\publish'
$ServiceOut = Join-Path $Publish 'agent'
$InstallerOut = Join-Path $Publish 'installer'

Write-Host "repo          : $Repo"
Write-Host "configuration : $Configuration"
Write-Host "output        : $OutDir"
Write-Host ''

foreach ($dir in @($ServiceOut, $InstallerOut)) {
    if (Test-Path $dir) { Remove-Item -Recurse -Force $dir }
}
New-Item -ItemType Directory -Force $OutDir | Out-Null

# Self-contained, as the CD: endpoints are clinical workstations and must not need a .NET runtime.
Write-Host '[1/4] dotnet publish RansomGuard.Agent.Service (win-x64, self-contained)'
dotnet publish (Join-Path $Repo 'agent\src\RansomGuard.Agent.Service\RansomGuard.Agent.Service.csproj') `
    -c $Configuration -r win-x64 --self-contained true -o $ServiceOut --nologo -v q
if ($LASTEXITCODE -ne 0) { throw "dotnet publish of the agent service failed ($LASTEXITCODE)" }

Write-Host '[2/4] dotnet publish RansomGuard.Agent.Installer (single file)'
dotnet publish (Join-Path $Repo 'agent\src\RansomGuard.Agent.Installer\RansomGuard.Agent.Installer.csproj') `
    -c $Configuration -r win-x64 --self-contained true -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true `
    -o $InstallerOut --nologo -v q
if ($LASTEXITCODE -ne 0) { throw "dotnet publish of the installer stub failed ($LASTEXITCODE)" }

Write-Host '[3/4] zip the package, write its .sha256'
$zip = Join-Path $OutDir 'RansomGuard-Agent-win-x64.zip'
if (Test-Path $zip) { Remove-Item -Force $zip }
Compress-Archive -Path (Join-Path $ServiceOut '*') -DestinationPath $zip -Force
$zipHash = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLower()
"$zipHash  RansomGuard-Agent-win-x64.zip" | Out-File -Encoding ascii "$zip.sha256"

Write-Host '[4/4] stub and install.ps1'
$stubSrc = Join-Path $InstallerOut 'RansomGuard-Installer.exe'
if (-not (Test-Path $stubSrc)) { throw "the installer publish did not produce $stubSrc" }
$stub = Join-Path $OutDir 'RansomGuard-Installer-stub.exe'
Copy-Item $stubSrc $stub -Force
$stubHash = (Get-FileHash $stub -Algorithm SHA256).Hash.ToLower()
"$stubHash  RansomGuard-Installer-stub.exe" | Out-File -Encoding ascii "$stub.sha256"
Copy-Item (Join-Path $Repo 'agent\install.ps1') (Join-Path $OutDir 'install.ps1') -Force

$version = [Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $ServiceOut 'RansomGuard.Agent.Service.dll')).ProductVersion

Write-Host ''
Write-Host "agent version : $version"
Get-ChildItem $OutDir | Select-Object Name, @{ n = 'MB'; e = { [math]::Round($_.Length / 1MB, 2) } } | Format-Table -AutoSize
Write-Host "package sha256: $zipHash"
Write-Host "stub    sha256: $stubHash"
