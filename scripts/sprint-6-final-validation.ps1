#Requires -Version 7.0
<#
.SYNOPSIS
    Sprint 6 GRID Final Validation — 25 gates before v0.8.0 tag.
#>
$ErrorActionPreference = "Continue"
$results = @()

function Add-Result {
    param($Number, $Description, $Pass, $Detail = "")
    $script:results += [PSCustomObject]@{
        Gate = $Number; Description = $Description
        Status = if ($Pass) { "PASS" } else { "FAIL" }; Detail = $Detail
    }
    $color = if ($Pass) { "Green" } else { "Red" }
    Write-Host ("[{0}] Gate {1}: {2} {3}" -f $(if ($Pass) { "PASS" } else { "FAIL" }), $Number, $Description, $Detail) -ForegroundColor $color
}

Write-Host "=== Sprint 6 GRID Final Validation ===" -ForegroundColor Cyan
Write-Host ""

# --- Python Build and Tests (5 gates) ---

Push-Location "grid"

# Gate 1: Python tests 90+
$testOutput = .venv/Scripts/python -m pytest tests/ -q --tb=no 2>&1 | Out-String
$passedMatch = [regex]::Match($testOutput, "(\d+) passed")
$passed = if ($passedMatch.Success) { [int]$passedMatch.Groups[1].Value } else { 0 }
Add-Result 1 "Python tests 90+ passing" ($passed -ge 90) "passed=$passed"

# Gate 2: ruff clean
$ruffOutput = .venv/Scripts/ruff check src/ 2>&1 | Out-String
$ruffClean = $ruffOutput -match "All checks passed"
Add-Result 2 "ruff lint clean" $ruffClean

# Gate 3: FastAPI app imports
$importResult = .venv/Scripts/python -c "from ransomguard_grid.main import app; print('OK')" 2>&1 | Out-String
Add-Result 3 "FastAPI app imports" ($importResult -match "OK")

# Gate 4: Health endpoint
$healthResult = .venv/Scripts/python -c "
import asyncio, httpx
from ransomguard_grid.main import app
async def t():
    async with httpx.AsyncClient(transport=httpx.ASGITransport(app=app), base_url='http://test') as c:
        r = await c.get('/api/v1/health')
        print(r.status_code)
asyncio.run(t())
" 2>&1 | Out-String
Add-Result 4 "Health endpoint returns 200" ($healthResult -match "200")

# Gate 5: 21 models import
$modelsResult = .venv/Scripts/python -c "
from ransomguard_grid.db.models import *
import ransomguard_grid.db.models as m
print(len(m.__all__))
" 2>&1 | Out-String
Add-Result 5 "21 SQLAlchemy models import" ($modelsResult -match "21")

Pop-Location

# --- Database Migrations (3 gates) ---

# Gate 6: Alembic migration files exist
$migFiles = Get-ChildItem -Path "grid/alembic/versions/" -Filter "*.py" -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -ne "__pycache__" }
$migCount = ($migFiles | Measure-Object).Count
Add-Result 6 "Alembic migrations exist (2)" ($migCount -ge 2) "found=$migCount"

# Gate 7: alembic.ini configured
$alembicIni = Get-Content "grid/alembic.ini" -Raw -ErrorAction SilentlyContinue
$alembicConfigured = ($alembicIni -match "prepend_sys_path") -and ($alembicIni -match "script_location")
Add-Result 7 "alembic.ini configured" $alembicConfigured

# Gate 8: Models have tenant_id
$tenantModels = (git grep -l "tenant_id" -- "grid/src/ransomguard_grid/db/models/" 2>$null | Measure-Object).Count
Add-Result 8 "Models with tenant_id (4+ files)" ($tenantModels -ge 4) "files=$tenantModels"

# --- Endpoint Coverage (5 gates) ---

# Gate 9: Agent endpoints exist
$agentRoutes = @("enrollment.py", "alerts.py", "audit_log.py", "heartbeat.py")
$agentFound = ($agentRoutes | Where-Object { Test-Path "grid/src/ransomguard_grid/api/v1/routes/$_" }).Count
Add-Result 9 "Agent API routes (4)" ($agentFound -eq 4) "found=$agentFound/4"

# Gate 10: Dashboard endpoints exist
$dashRoutes = @("auth.py", "dashboard.py")
$dashFound = ($dashRoutes | Where-Object { Test-Path "grid/src/ransomguard_grid/api/v1/routes/$_" }).Count
Add-Result 10 "Dashboard API routes (2)" ($dashFound -eq 2) "found=$dashFound/2"

# Gate 11: Threat intel endpoints exist
Add-Result 11 "Threat intel route" (Test-Path "grid/src/ransomguard_grid/api/v1/routes/threat_intel.py")

# Gate 12: Pydantic schemas
$schemaFiles = Get-ChildItem "grid/src/ransomguard_grid/api/v1/schemas/" -Filter "*.py" -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -ne "__init__.py" }
$schemaCount = ($schemaFiles | Measure-Object).Count
Add-Result 12 "Pydantic schemas (5+)" ($schemaCount -ge 5) "found=$schemaCount"

# Gate 13: Ed25519 verification module
$ed25519File = Test-Path "grid/src/ransomguard_grid/core/ed25519.py"
$ed25519Content = if ($ed25519File) { Get-Content "grid/src/ransomguard_grid/core/ed25519.py" -Raw } else { "" }
$ed25519HasVerify = $ed25519Content -match "verify_ed25519_signature"
Add-Result 13 "Ed25519 verification module" $ed25519HasVerify

# --- mTLS Configuration (4 gates) ---

# Gate 14: nginx agent config has mTLS
$agentConf = Get-Content "deployment/grid/nginx/conf.d/agent.conf" -Raw -ErrorAction SilentlyContinue
$mTlsPresent = ($agentConf -match "ssl_verify_client") -and ($agentConf -match "X-Client-Cert")
Add-Result 14 "nginx mTLS for agent endpoints" $mTlsPresent

# Gate 15: TLS 1.3 only
$tlsOnly = $agentConf -match "ssl_protocols TLSv1.3"
Add-Result 15 "TLS 1.3 only (no TLS 1.2)" $tlsOnly

# Gate 16: Dashboard no mTLS
$dashConf = Get-Content "deployment/grid/nginx/conf.d/dashboard.conf" -Raw -ErrorAction SilentlyContinue
$dashNoMtls = $dashConf -match "ssl_verify_client off"
Add-Result 16 "Dashboard no mTLS (JWT only)" $dashNoMtls

# Gate 17: PKI init script
Add-Result 17 "PKI init script exists" (Test-Path "deployment/grid/pki/init-pki.sh")

# --- Multi-Tenant Isolation (5 gates) ---

# Gate 18: BaseRepository enforces tenant_id
$baseRepoContent = Get-Content "grid/src/ransomguard_grid/db/repositories/base_repository.py" -Raw -ErrorAction SilentlyContinue
$tenantEnforced = ($baseRepoContent -match "self\.tenant_id") -and ($baseRepoContent -match "tenant_id.*required")
Add-Result 18 "BaseRepository enforces tenant_id" $tenantEnforced

# Gate 19: Tenant isolation test file exists
$isoTestFile = Test-Path "grid/tests/api/test_tenant_isolation.py"
Add-Result 19 "Tenant isolation test file exists" $isoTestFile

# Gate 20: 10 isolation tests
if ($isoTestFile) {
    $isoContent = Get-Content "grid/tests/api/test_tenant_isolation.py" -Raw
    $isoTestCount = ([regex]::Matches($isoContent, "async def test_")).Count
    Add-Result 20 "10 tenant isolation tests" ($isoTestCount -ge 10) "found=$isoTestCount"
} else {
    Add-Result 20 "10 tenant isolation tests" $false "file missing"
}

# Gate 21: RBAC distinction tests
$rbacFile = Test-Path "grid/tests/api/test_rbac_distinction.py"
if ($rbacFile) {
    $rbacContent = Get-Content "grid/tests/api/test_rbac_distinction.py" -Raw
    $rbacCount = ([regex]::Matches($rbacContent, "async def test_")).Count
    Add-Result 21 "6 RBAC distinction tests" ($rbacCount -ge 6) "found=$rbacCount"
} else {
    Add-Result 21 "6 RBAC distinction tests" $false "file missing"
}

# Gate 22: 404 not 403 pattern
$dashboardRoute = Get-Content "grid/src/ransomguard_grid/api/v1/routes/dashboard.py" -Raw -ErrorAction SilentlyContinue
$has404 = $dashboardRoute -match '404.*".*not found"'
Add-Result 22 "Cross-tenant returns 404 not 403" $has404

# --- Deployment and Documentation (3 gates) ---

# Gate 23: Docker compose exists
Add-Result 23 "docker-compose.yml exists" (Test-Path "deployment/grid/docker-compose.yml")

# Gate 24: Dockerfile exists
Add-Result 24 "Dockerfile exists" (Test-Path "deployment/grid/Dockerfile")

# Gate 25: README + 2 ADRs
$readmeExists = Test-Path "grid/README.md"
$adr023 = Test-Path "docs/adr/023-grid-fastapi-stack.md"
$adr024 = Test-Path "docs/adr/024-grid-multi-tenant-design.md"
Add-Result 25 "Documentation (README + 2 ADRs)" ($readmeExists -and $adr023 -and $adr024)

# Summary
Write-Host ""
Write-Host "=== Summary ===" -ForegroundColor Cyan
$passCount = ($results | Where-Object { $_.Status -eq "PASS" }).Count
$failCount = ($results | Where-Object { $_.Status -eq "FAIL" }).Count
Write-Host "PASS: $passCount / $($results.Count)" -ForegroundColor Green
if ($failCount -gt 0) {
    Write-Host "FAIL: $failCount / $($results.Count)" -ForegroundColor Red
    Write-Host ""
    Write-Host "Failed gates:" -ForegroundColor Red
    $results | Where-Object { $_.Status -eq "FAIL" } | ForEach-Object {
        Write-Host "  Gate $($_.Gate): $($_.Description) ($($_.Detail))" -ForegroundColor Red
    }
    exit 1
} else {
    Write-Host "FAIL: 0 / $($results.Count)" -ForegroundColor Green
    Write-Host ""
    Write-Host "All $($results.Count) gates passed. Sprint 6 ready for v0.8.0-grid tag." -ForegroundColor Green
}
exit 0
