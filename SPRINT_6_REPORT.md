# Sprint 6 Report — GRID Central Server (FastAPI + MySQL + mTLS)

**Sprint Duration**: 2026-05-29
**Baseline**: v0.7.0-ironclad (576 agent tests, 6 modules operational)
**Target**: GRID central server with 94+ Python tests
**Tag**: v0.8.0-grid

---

## 1. Executive Summary

Sprint 6 delivers GRID, the first non-C# component in the RansomGuard-CM project — a FastAPI Python 3.12 central server with MySQL 8.4, nginx mTLS, and multi-tenant isolation. The server receives security alerts from hospital agents via mTLS-authenticated endpoints, distributes Ed25519-signed threat intelligence packages aggregated from 5 public sources, and provides a JWT-authenticated REST API for the future Sprint 7 React dashboard. Multi-tenant isolation is enforced at the repository level with 10 explicit cross-tenant leak prevention tests all passing. The production deployment stack (Docker Compose with 4 services, nginx TLS 1.3, PKI initialization, encrypted backups) was validated through 5 deployment bug fixes resolved in sequence. 94 Python tests pass with 21 SQLAlchemy models across 22 MySQL tables.

---

## 2. Module Status

| # | Component | Sprint | Status |
|---|-----------|--------|--------|
| 1 | SENTINEL Agent Module | 2 | OPERATIONAL |
| 2 | ENTROPY Agent Module | 3 | OPERATIONAL |
| 3 | GENEALOGY Agent Module | 3 | OPERATIONAL |
| 4 | USB GUARD Agent Module | 4 | OPERATIONAL |
| 5 | EXFIL WATCH Agent Module | 4 | OPERATIONAL |
| 6 | IRONCLAD Agent Module | 5 | OPERATIONAL (software-only) |
| 7 | **GRID Central Server** | **6** | **OPERATIONAL** |

**GRID Subsystems:**

| Subsystem | Status |
|-----------|--------|
| Agent Ingestion API (mTLS) | OPERATIONAL |
| Dashboard API (JWT + RBAC) | OPERATIONAL |
| Threat Intel Updater | OPERATIONAL (5/6 sources) |
| Multi-Tenant Isolation | VERIFIED (10/10 tests) |
| Production Deployment (Docker) | VALIDATED (5 bugs fixed) |

---

## 3. Files Created

### Python Source (grid/src/ransomguard_grid/)

```
core/
  config.py, security.py, jwt_service.py, ed25519.py,
  logging.py, rate_limit.py, exceptions.py

api/v1/
  routes/: enrollment.py, alerts.py, audit_log.py, heartbeat.py,
           threat_intel.py, auth.py, dashboard.py
  schemas/: agent.py, alert.py, audit_log.py, threat_intel.py,
            auth.py, dashboard.py
  dependencies/: auth.py (mTLS), jwt_auth.py (JWT)

db/
  base.py, session.py
  models/: enums.py, tenant_user.py (5), agent.py (4),
           alerts.py (6), threat_intel.py (3), operations.py (3)
  repositories/: base_repository.py, tenant_repository.py,
                 agent_repository.py, agent_certificate_repository.py,
                 agent_heartbeat_repository.py, user_repository.py,
                 role_repository.py, alert_repository.py,
                 audit_log_repository.py, threat_intel_repository.py

services/
  enrollment_otp_service.py, grid_signing_key_service.py,
  threat_intel_aggregator.py, threat_intel_package_builder.py
  threat_intel_sources/: tor_project.py, threatfox.py,
                         alienvault_otx.py, aws.py, gcp.py

workers/
  threat_intel_updater_worker.py
```

### Tests (grid/tests/)

```
conftest.py (shared fixtures with mTLS cert + Ed25519 key)
test_config.py, test_db.py, test_main.py, test_security.py, test_logging.py
api/: test_enrollment.py, test_alerts.py, test_audit_logs.py,
      test_heartbeat.py, test_threat_intel_endpoints.py,
      test_auth.py, test_tenant_isolation.py, test_rbac_distinction.py
db/: test_models.py, test_repositories.py
services/: test_threat_intel_sources.py, test_threat_intel_aggregator.py,
           test_threat_intel_package_builder.py
```

### Deployment (deployment/grid/)

```
docker-compose.yml, Dockerfile
nginx/: nginx.conf, conf.d/agent.conf, conf.d/dashboard.conf, conf.d/redirect.conf
pki/: init-pki.sh
mysql/conf.d/: grid.cnf
```

### Documentation

```
grid/README.md
grid/.env.example
docs/adr/023-grid-fastapi-stack.md
docs/adr/024-grid-multi-tenant-design.md
docs/architecture/adr/ADR-022-ironclad-software-only-sprint5.md (Sprint 5)
```

---

## 4. Files Modified

| File | Change |
|------|--------|
| (none from agent/) | Agent codebase unchanged in Sprint 6 |

---

## 5. Migrations Applied

| # | Migration | Tables | Database |
|---|-----------|--------|----------|
| 1 | `28c095d60e26_initial_schema_sprint_6_21_tables` | 21 tables | MySQL 8.4 |
| 2 | `0fdc10883121_add_ed25519_public_key_pem` | +1 column on agent_certificates | MySQL 8.4 |

**Total: 2 Alembic migrations, 22 MySQL tables (21 app + 1 alembic_version).**

---

## 6. Test Coverage

| Category | Tests |
|----------|-------|
| Config + DB + Main + Security + Logging | 15 |
| Models + Repositories | 15 |
| Enrollment + Alerts + Audit Logs + Heartbeat | 23 |
| Threat Intel (sources + aggregator + builder + endpoints) | 17 |
| Auth (login, refresh, logout) | 5 |
| Tenant Isolation (cross-tenant leak prevention) | 10 |
| RBAC Distinction (analyst vs auditor vs admin) | 6 |
| Deployment Bug Fix (structlog, driver, etc.) | 3 |
| **GRID Python Total** | **94** |
| Agent C# (.NET) | 576 |
| **Project Grand Total** | **670** |

---

## 7. API Surface

### Agent Endpoints (mTLS Required)

| Method | Path | Purpose |
|--------|------|---------|
| POST | /api/v1/agents/enroll | Agent onboarding (no mTLS) |
| POST | /api/v1/agents/{id}/alerts | Single alert ingestion |
| POST | /api/v1/agents/{id}/alerts/batch | Batch alert ingestion (up to 100) |
| POST | /api/v1/agents/{id}/audit-logs | Ed25519-verified audit log batch |
| POST | /api/v1/agents/{id}/heartbeat | Liveness + pending commands |
| GET | /api/v1/threat-intel/manifest | Latest threat intel version |
| GET | /api/v1/threat-intel/package/{ver} | ZIP package download |
| POST | /api/v1/threat-intel/agents/{id}/threat-intel-version | Report applied version |

### Dashboard Endpoints (JWT Required)

| Method | Path | RBAC |
|--------|------|------|
| POST | /api/v1/auth/login | public |
| POST | /api/v1/auth/refresh | authenticated |
| POST | /api/v1/auth/logout | authenticated |
| GET | /api/v1/dashboard/alerts | all roles |
| GET | /api/v1/dashboard/alerts/{id} | all roles |
| POST | /api/v1/dashboard/alerts/{id}/status | admin, analyst |
| GET | /api/v1/dashboard/agents | all roles |
| GET | /api/v1/dashboard/agents/{id} | all roles |
| GET | /api/v1/dashboard/metrics/summary | all roles |
| POST | /api/v1/dashboard/commands | admin only |
| GET | /api/v1/dashboard/users | admin only |
| POST | /api/v1/dashboard/users | admin only |
| PUT | /api/v1/dashboard/users/{id}/roles | admin only |
| POST | /api/v1/dashboard/users/{id}/disable | admin only |
| GET | /api/v1/dashboard/audit-logs | admin, auditor |
| GET | /api/v1/health | public |
| GET | /api/v1/health/ready | public |

**Total: 19 endpoints.**

---

## 8. Multi-Tenant Isolation Verification

10/10 cross-tenant leak prevention tests **ALL PASS**:

1. User A cannot list tenant B alerts
2. User A gets 404 (not 403) for tenant B alert by ID
3. User A cannot list tenant B agents
4. User A gets 404 for tenant B agent by ID
5. JWT tampering (changing tenant_id claim) rejected
6. Admin role does not bypass tenant filter
7. Query parameter injection ignored (server uses JWT)
8. Cross-tenant command issuance blocked (404)
9. Cross-tenant user list isolated
10. Cross-tenant metrics count only own data

---

## 9. RBAC Verification

6/6 role distinction tests **ALL PASS**:

1. Security analyst CAN update alert status
2. Read-only auditor CANNOT update alert status (403)
3. Security analyst CANNOT search audit logs (403)
4. Read-only auditor CAN search audit logs
5. Only tenant admin can update user roles (analyst gets 403)
6. Admin cannot self-demote (400 anti-brick safety)

---

## 10. Deployment Validation

5 deployment bugs identified and fixed in sequence:

| # | Bug | Fix | Commit |
|---|-----|-----|--------|
| 1 | PKI certs not generated | init-pki.sh script created | (PKI commit) |
| 2 | ModuleNotFoundError: alembic | PYTHONUSERBASE + --chown in Dockerfile | dbe58a2 |
| 3 | ModuleNotFoundError: aiomysql | Added aiomysql+asyncmy to requirements.txt | 2f0c925 |
| 4 | structlog PrintLogger has no .name | Removed add_logger_name processor | b13c8bb |
| 5 | nginx SSL cipher mismatch | Removed ssl_ciphers (TLS 1.3 uses defaults) | 40ac4db |

Final state: All 4 containers Up (healthy), health endpoint returns 200.

---

## 11. Known Limitations + Debt

| Item | Severity | Target |
|------|----------|--------|
| Azure IP ranges source not implemented | Low | Sprint 6.5 |
| Docker E2E tests not in pytest | Medium | Sprint 8 |
| Worker lifespan tests not added | Low | Sprint 6.5 |
| Redis rate limiting not wired (in-memory fallback) | Medium | Sprint 8 |
| asyncmy/aiomysql dual install (Python 3.14 compat) | Low | Resolved when dev moves to 3.12 |
| docker-compose version key deprecated | Cosmetic | Sprint 8 |
| Metrics timeline endpoint not implemented | Low | Sprint 7 |

---

## 12. Sprint 7 Dashboard Readiness

Sprint 6 delivers everything Sprint 7 React Dashboard needs:

- **19 REST endpoints** with auto-generated OpenAPI docs at `/docs`
- **JWT auth flow** (login, refresh, logout) ready for React OAuth2 client
- **Paginated responses** with filter support on alerts (severity, status, agent, time, MITRE)
- **RBAC enforcement** on all endpoints (3 roles)
- **Metrics summary** endpoint for dashboard KPIs
- **Command issuance** endpoint for remote agent actions
- **User management** endpoints for tenant admin

The codebase is ready for v0.8.0-grid tag.

---

*Generated: 2026-05-29 | Sprint 6 commits: 63198a8..f91161f | 12 commits*
