# RansomGuard-CM — Database Strategy Document

**Version:** 1.0
**Author:** Jella Lebga (Kuate Abdel Yaniv)
**Date:** May 2026
**Status:** Production-Ready

---

## 1. SCHEMA OVERVIEW

The RansomGuard-CM database consists of **20 tables** organized in **8 bounded contexts** (Domain-Driven Design):

| Domain | Tables | Purpose |
|---|---|---|
| Tenancy | hospitals, subscriptions | Multi-tenant root |
| Identity | users, mfa_devices, sessions | Auth & RBAC |
| Asset | endpoints, agents | Protected resources |
| Detection | incidents, alerts, threat_signatures | Security events |
| Backup | backup_jobs, snapshots, backup_files, restore_requests | Resilience |
| Licensing | licenses | Commercial model |
| Audit | audit_logs | Immutable compliance trail |
| Config | whitelist_rules, critical_processes | System configuration |

**Total foreign key relationships:** 23
**Total indexes (excluding PK/UK):** 47
**Total stored procedures:** 1
**Total triggers:** 5
**Total views:** 2

---

## 2. INDEXING STRATEGY

### 2.1 Primary Keys

All tables use **CHAR(36) UUID** as primary key except `audit_logs` (BIGINT auto-increment for sequence integrity).

**Why UUIDs:**
- No enumeration attacks (`/api/users/1`, `/api/users/2`, ...)
- Distributed generation possible (cloud + on-prem)
- No conflicts in multi-tenant federation
- UUID v4 random — unpredictable

**Why CHAR(36) instead of BINARY(16):**
- Human-readable in logs and debugging
- Direct JSON serialization (no encoding step)
- Adequate performance for our scale (target: 1000 hospitals max)
- Tradeoff accepted: 36 bytes vs 16 bytes per key

### 2.2 Composite Indexes — Dashboard Query Optimization

**Index #1: `idx_incidents_hospital_status (hospital_id, status, detected_at DESC)`**
- **Query optimized:** "Show all active incidents for hospital X, newest first"
- **Frequency:** Loaded on every dashboard open (high traffic)
- **Cardinality:** hospital_id (low), status (very low), detected_at (high) — optimal order

**Index #2: `idx_alerts_endpoint_time (endpoint_id, created_at DESC)`**
- **Query optimized:** "Show last 100 alerts for this specific workstation"
- **Frequency:** Loaded on endpoint detail view

**Index #3: `idx_snapshots_endpoint_time (endpoint_id, created_at DESC)`**
- **Query optimized:** "List available snapshots for restore — newest first"
- **Frequency:** Critical path during incident recovery

**Index #4: `idx_endpoints_criticality (is_medical_critical, criticality)`**
- **Query optimized:** "Find all medical-critical endpoints requiring special handling"
- **Frequency:** Used by detection rules engine on every alert evaluation

**Index #5: `idx_licenses_expires (expires_at, status)`**
- **Query optimized:** "Find licenses expiring in next 30 days"
- **Frequency:** Daily cron job for renewal notifications

### 2.3 Unique Constraints

**Hospital-scoped uniqueness:** `uq_endpoints_hospital_hostname (hospital_id, hostname)`
- Same hostname can exist in different hospitals
- Prevents duplicate registration within one hospital

**Globally unique:** `uq_users_email` — emails are tenant-spanning identifiers
**Globally unique:** `uq_licenses_key` — license keys must be universally unique
**Globally unique:** `uq_agents_uuid` — agent UUIDs must be universally unique

### 2.4 Covering Indexes (Strategic)

Where SELECT queries return only a few columns, indexes **include** those columns to avoid heap lookups:

```sql
-- Example: heartbeat monitoring (very high frequency)
KEY idx_agents_heartbeat (last_heartbeat_at, health_status)
```

The query `SELECT health_status WHERE last_heartbeat_at < ...` is satisfied entirely from the index without touching the row data.

---

## 3. DATA INTEGRITY GUARANTEES

### 3.1 Foreign Key Behavior Matrix

| Relationship | ON DELETE | Rationale |
|---|---|---|
| hospital → subscriptions | RESTRICT | Never lose subscription history |
| hospital → users | RESTRICT | Users must be transferred or anonymized first |
| hospital → endpoints | RESTRICT | Audit obligation under Loi 2024/017 |
| user → sessions | CASCADE | Sessions are ephemeral |
| user → mfa_devices | CASCADE | MFA tied to user lifecycle |
| endpoint → agent | CASCADE | Agent dies with endpoint |
| snapshot → backup_files | CASCADE | Files belong to snapshot atomically |
| backup_job → snapshots | RESTRICT | Audit trail preservation |
| user → restore_requests | RESTRICT | Accountability preservation |

### 3.2 Soft Delete Pattern

Tables with `deleted_at TIMESTAMP(6) DEFAULT NULL`:
- `hospitals`
- `users`
- `endpoints`

**Why:** Loi 2024/017 requires audit trail preservation. Physical deletion is forbidden for compliance.

**Query pattern:** All application queries MUST filter `WHERE deleted_at IS NULL`.

**Index strategy:** Composite indexes include `deleted_at` filtering implicitly via index `idx_*_deleted_at`.

### 3.3 Optimistic Locking

Tables with `version INT UNSIGNED NOT NULL DEFAULT 1`:
- hospitals, subscriptions, users, endpoints, incidents, licenses

**Pattern:** Every UPDATE must include `WHERE version = ?`. Triggers auto-increment version.

**Why:** Prevents lost updates when two operators modify the same record concurrently.

```python
# Application pattern
result = await db.execute(
    "UPDATE hospitals SET name = ?, version = version + 1 WHERE id = ? AND version = ?",
    (new_name, hospital_id, expected_version)
)
if result.rowcount == 0:
    raise OptimisticLockException("Record was modified by another transaction")
```

### 3.4 Hash-Chained Audit Log

The `audit_logs` table implements a **blockchain-light** integrity guarantee:

```
Entry N:
  sequence_number   = N
  previous_hash     = SHA-256 of entry N-1
  current_hash      = SHA-256(N || prev_hash || payload || timestamp)
```

**Property:** Any modification of any past entry invalidates ALL subsequent hashes — instantly detectable.

**Verification procedure** (run weekly):
```sql
-- Pseudocode
prev_hash = '0000...0000'
FOR EACH log IN audit_logs ORDER BY sequence_number:
    computed = SHA-256(log.sequence_number || prev_hash || log.payload || log.occurred_at)
    IF computed != log.current_hash:
        RAISE "TAMPERING DETECTED at sequence " || log.sequence_number
    prev_hash = log.current_hash
```

---

## 4. PERFORMANCE CONSIDERATIONS

### 4.1 Volume Estimates

For a hospital of 100 endpoints over 1 year:

| Table | Rows/year | Storage |
|---|---|---|
| alerts | 50,000 - 200,000 | 50 - 200 MB |
| audit_logs | 100,000 - 500,000 | 100 - 500 MB |
| snapshots | 4,400 (every 2h × 100 endpoints) | 5 MB metadata |
| backup_files | 4-40 million | 1-10 GB metadata |
| incidents | 50 - 500 | 1 MB |

**Total database size estimate:** 2 - 15 GB per hospital per year (metadata only — actual backup files stored in MinIO).

### 4.2 Partitioning Strategy (Future)

When a single hospital exceeds 500GB in 3 years, partition the high-volume tables by month:

```sql
-- Future optimization (not v1.0)
ALTER TABLE alerts
PARTITION BY RANGE (TO_DAYS(created_at)) (
    PARTITION p202601 VALUES LESS THAN (TO_DAYS('2026-02-01')),
    PARTITION p202602 VALUES LESS THAN (TO_DAYS('2026-03-01')),
    ...
);
```

### 4.3 Read/Write Patterns

| Operation | Frequency | Optimization |
|---|---|---|
| Agent heartbeat | Every 30s × N agents | Indexed write, no read |
| Alert ingestion | Variable | Write-heavy, async processing |
| Dashboard load | On user action | Read-heavy, view-cached |
| Audit log append | Every state change | Append-only, hash compute |
| Backup metadata | Every 2h × N endpoints | Batch insert |

---

## 5. MIGRATION STRATEGY (Alembic)

### 5.1 Why Alembic

Industry-standard Python database migration tool. Used by FastAPI/SQLAlchemy projects worldwide. Provides:
- Version-controlled schema changes
- Up/down migration scripts
- Auto-generation from SQLAlchemy models
- Multi-environment support (dev/staging/prod)

### 5.2 Migration Naming Convention

```
migrations/versions/
  20260515_120000_001_initial_schema.py
  20260520_140000_002_add_endpoint_location.py
  20260601_090000_003_add_audit_log_indexes.py
```

Format: `{ISO_TIMESTAMP}_{SEQUENCE}_{DESCRIPTION}.py`

### 5.3 Migration Rules (Senior-style)

**Rule 1 — Never modify production data in migrations**
Migrations only change schema. Data migrations are separate scripts.

**Rule 2 — Always provide downgrade**
Every `upgrade()` function must have a corresponding `downgrade()` that reverses the change.

**Rule 3 — Test migrations before deploying**
Run on staging copy of production data. Time the migration. If > 1 minute on production-size data, plan a maintenance window.

**Rule 4 — Backwards compatibility for 1 version**
A new schema must work with both N-1 and N application versions during the rolling deploy.

**Rule 5 — No DROP TABLE in migrations**
Renamed columns/tables are kept for at least one release cycle.

### 5.4 Example Migration

```python
"""add_endpoint_location_field

Revision ID: 002_add_endpoint_location
Revises: 001_initial_schema
Create Date: 2026-05-20 14:00:00
"""
from alembic import op
import sqlalchemy as sa

def upgrade():
    op.add_column(
        'endpoints',
        sa.Column('location', sa.String(255), nullable=True)
    )
    op.create_index(
        'idx_endpoints_location',
        'endpoints',
        ['hospital_id', 'location']
    )

def downgrade():
    op.drop_index('idx_endpoints_location', 'endpoints')
    op.drop_column('endpoints', 'location')
```

---

## 6. SECURITY CONSIDERATIONS

### 6.1 Sensitive Data Handling

| Field | Encryption | Justification |
|---|---|---|
| users.password_hash | Argon2id hashed | Industry standard for password storage |
| mfa_devices.secret_encrypted | AES-256-GCM | TOTP secrets must be reversible |
| sessions.token_hash | SHA-256 | Tokens compared by hash, never stored plain |
| licenses.license_key | Stored + hashed | Original key for display, hash for fast lookup |
| backup_files.relative_path_encrypted | AES-256-GCM | Patient file paths are sensitive |
| audit_logs.payload | Plain JSON (no PII) | Application MUST anonymize before logging |

### 6.2 Database User Privileges

```sql
-- Application user (least privilege)
CREATE USER 'ransomguard_app'@'%' IDENTIFIED BY '<strong_password>';
GRANT SELECT, INSERT, UPDATE ON ransomguard_cm.* TO 'ransomguard_app'@'%';
GRANT EXECUTE ON PROCEDURE ransomguard_cm.sp_append_audit_log TO 'ransomguard_app'@'%';
-- NO DELETE permission — enforce soft deletes at DB level
-- NO DROP permission

-- Migration user (used only by Alembic)
CREATE USER 'ransomguard_migrator'@'localhost' IDENTIFIED BY '<separate_strong_password>';
GRANT ALL PRIVILEGES ON ransomguard_cm.* TO 'ransomguard_migrator'@'localhost';

-- Read-only user (analytics, support)
CREATE USER 'ransomguard_reader'@'%' IDENTIFIED BY '<another_strong_password>';
GRANT SELECT ON ransomguard_cm.* TO 'ransomguard_reader'@'%';
```

### 6.3 SQL Injection Prevention

- **Application layer:** SQLAlchemy ORM with parameterized queries — NEVER raw string concatenation
- **Database layer:** Strict mode enabled, prepared statements only
- **Audit:** Bandit (Python) + manual review of all raw SQL

---

## 7. BACKUP & RECOVERY OF THE DATABASE ITSELF

### 7.1 RPO (Recovery Point Objective): 1 hour
- Binary log replication every 1 hour to MinIO
- Full dump nightly at 03:00 local time

### 7.2 RTO (Recovery Time Objective): 4 hours
- Documented restoration procedure (runbook)
- Tested monthly on staging environment

### 7.3 Disaster Recovery Tiers

| Tier | Hospital Scenario | Recovery Strategy |
|---|---|---|
| 1 | Server hardware failure | Restore from local nightly dump (4h) |
| 2 | Local NAS destroyed | Restore from MinIO encrypted offsite copy (8h) |
| 3 | Hospital building destroyed | Restore from cloud snapshot to new hardware (48h) |

---

## 8. COMPLIANCE MAPPING

| Requirement | Implementation |
|---|---|
| Loi 2024/017 Article 7 — Data minimization | Soft delete + scheduled anonymization |
| Loi 2024/017 Article 12 — Audit trail | audit_logs table with hash chain |
| Loi 2024/017 Article 18 — Right to deletion | Anonymization procedure (TBD) |
| ISO 27001 A.12.4 — Logging | audit_logs append-only |
| OWASP A01 — Broken Access Control | users.role + RBAC enforcement |
| OWASP A02 — Cryptographic Failures | AES-256-GCM, Argon2id, Ed25519 |
| OWASP A03 — Injection | Parameterized queries only |
| OWASP A09 — Logging Failures | Hash-chained audit logs |

---

## 9. OPEN QUESTIONS (For Future Iteration)

1. **PII anonymization scheduled job** — Should patient-related identifiers be hashed after 5 years per RGPD/Loi 2024/017?
2. **Multi-region replication** — Should we replicate to a Cameroon-hosted secondary for sovereignty?
3. **Read replicas** — At what scale (number of agents per server) do we need read replicas?
4. **Time-series migration** — At what point does `alerts` table need migration to TimescaleDB or InfluxDB?

These questions are intentionally deferred — they will be addressed when the product scales.

---

## END OF DOCUMENT
