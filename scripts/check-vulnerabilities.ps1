<#
.SYNOPSIS
    Checks all NuGet dependencies for known vulnerabilities.
.DESCRIPTION
    Fails with exit code 1 if any vulnerable package is found.
    Compliant with NIST SP 800-218 SSDF RV.1.
#>

$ErrorActionPreference = "Stop"

$solutionPath = Join-Path $PSScriptRoot "..\agent\src\RansomGuard.Agent.sln"

Write-Host "Scanning NuGet dependencies for known vulnerabilities..."
$output = dotnet list $solutionPath package --vulnerable --include-transitive 2>&1

if ($output -match "has the following vulnerable packages") {
    Write-Host "VULNERABLE PACKAGES DETECTED:" -ForegroundColor Red
    Write-Output $output
    exit 1
}

Write-Host "No vulnerable packages detected." -ForegroundColor Green
exit 0
