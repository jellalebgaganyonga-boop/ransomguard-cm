<#
.SYNOPSIS
    Generates a CycloneDX SBOM for RansomGuard-CM.
.DESCRIPTION
    Produces a machine-readable Software Bill of Materials in JSON format.
    Compliant with NIST SP 800-218 SSDF PS.3.2 and EU Cyber Resilience Act.
#>

$ErrorActionPreference = "Stop"

$solutionPath = Join-Path $PSScriptRoot "..\agent\src\RansomGuard.Agent.sln"
$outputPath = Join-Path $PSScriptRoot "..\artifacts\sbom"
$version = git describe --tags --always 2>$null
if (-not $version) { $version = "dev" }

New-Item -ItemType Directory -Force -Path $outputPath | Out-Null

Write-Host "Generating CycloneDX SBOM for RansomGuard-CM v$version..."

dotnet CycloneDX $solutionPath `
    --output $outputPath `
    --filename "ransomguard-cm-sbom.json" `
    --json `
    --set-name "RansomGuard-CM" `
    --set-version $version

if ($LASTEXITCODE -eq 0) {
    $sbomFile = Join-Path $outputPath "ransomguard-cm-sbom.json"
    $size = (Get-Item $sbomFile).Length
    Write-Host "SBOM generated: $sbomFile ($size bytes)" -ForegroundColor Green
} else {
    Write-Error "SBOM generation failed"
    exit 1
}
