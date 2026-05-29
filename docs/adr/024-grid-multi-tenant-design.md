# ADR-024: GRID Multi-Tenant Architecture

**Status:** Accepted
**Date:** 2026-05-29
**Sprint:** 6

## Context

Cameroon has 200+ public hospitals and 50+ private clinics. Each deployment requires isolated data (Hospital X alerts invisible to Hospital Y), independent user management (admin per hospital), independent audit trails, and shared threat intelligence (one central feed for all).

Legal constraint: Loi 2024/017 Article 22 requires audit logs be tenant-scoped and immutable.

## Decision

**Single-database, tenant_id column-based isolation** with **repository-level enforcement**.

NOT chosen: separate database per tenant, separate schema per tenant.

## Architecture

1. **Tables**: 19/21 tables have `tenant_id` column (NOT NULL, FK to `tenants.id`, ON DELETE RESTRICT)
   - Excluded: `tenants` (the table itself), `roles` (system roles shared across tenants)
   - Also excluded from tenant filter: `threat_intel_versions`, `threat_intel_packages` (global)

2. **BaseRepository[T]** generic class enforces tenant_id filter on every query:
   ```python
   class BaseRepository(Generic[T]):
       def __init__(self, session, tenant_id: str):
           if not tenant_id:
               raise ValueError("tenant_id required")
           self.tenant_id = tenant_id

       async def get_by_id(self, id):
           stmt = select(self.model).where(
               self.model.id == id,
               self.model.tenant_id == self.tenant_id  # ALWAYS
           )
   ```

3. **JWT injection**: tenant_id is embedded in JWT claim, extracted at every dashboard request via `get_current_user` dependency

4. **mTLS injection**: tenant_id is derived from agent's certificate lookup (cert -> agent -> tenant_id) via `get_authenticated_agent` dependency

5. **404 not 403**: Cross-tenant access returns HTTP 404 to prevent tenant enumeration (CWE-285)

## Rationale

1. **Operational simplicity**: 1 MySQL database vs 200+ databases to manage
2. **Cost**: 1 VPS handles all hospitals (~$30/month)
3. **Backup simplicity**: 1 `mysqldump` backs up everything
4. **Threat intel sharing**: Shared tables (`threat_intel_versions`) are trivial with single DB
5. **Migration simplicity**: 1 `alembic upgrade head` per release applies to all tenants

## Trade-offs

**Risks:**
- A SQL injection or repository bug could leak cross-tenant data
- A query without tenant_id filter would expose all tenants' data

**Mitigations:**
- BaseRepository forces tenant_id at the type system level (constructor requires it)
- `add()` method validates entity's tenant_id matches repository's tenant_id
- 10 explicit cross-tenant isolation tests verify enforcement
- Code review focuses on repository layer — any raw SQL query is a red flag

## Validation

10 explicit cross-tenant tests in `tests/api/test_tenant_isolation.py`:

1. `test_user_a_cannot_list_tenant_b_alerts` — **PASS**
2. `test_user_a_gets_404_for_tenant_b_alert` — **PASS**
3. `test_user_a_cannot_list_tenant_b_agents` — **PASS**
4. `test_user_a_gets_404_for_tenant_b_agent` — **PASS**
5. `test_jwt_tampering_rejected` — **PASS**
6. `test_admin_role_does_not_bypass_tenant` — **PASS**
7. `test_query_param_cannot_escape_tenant` — **PASS**
8. `test_cross_tenant_command_blocked` — **PASS**
9. `test_cross_tenant_user_list_isolated` — **PASS**
10. `test_cross_tenant_metrics_isolated` — **PASS**

Additionally, 6 RBAC distinction tests in `tests/api/test_rbac_distinction.py` verify role-based access within a tenant (analyst vs auditor vs admin separation of duties).

## Consequences

**Positive:**
- Conformite loi 2024/017 Article 22 (tenant-scoped audit logs with Ed25519 signatures)
- Operational simplicity for solo-admin hospitals
- Cost-effective ($30/month VPS handles 200+ tenants)
- Composite indexes optimize hot queries: `ix_alert_tenant_severity_status_detected`

**Negative:**
- Large index on `(tenant_id, ...)` needed on hot tables — implemented via composite indexes
- Single point of failure if MySQL goes down — mitigated by daily encrypted backups

## Future Considerations

Sprint 8+: If a single tenant exceeds 50 agents OR 10M alerts/year, consider migrating that tenant to a dedicated database. Current architecture supports this via data export filtered by tenant_id + removal of the tenant_id column in the dedicated DB.
