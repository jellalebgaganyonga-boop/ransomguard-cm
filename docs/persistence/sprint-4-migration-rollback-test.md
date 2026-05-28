# Sprint 4 Migration Rollback Test Documentation

## Overview

Sprint 4 introduces 10 new database migrations that add persistence for USB Guard, Exfil Watch, Network Baseline, and Indicator Removal modules. This document covers the migration inventory, rollback testing procedures, forward-apply upgrade testing, and the v0.5.1 compatibility verification process.

All migrations target SQLite via Entity Framework Core 8.0.27. The agent uses a single-file SQLite database (`ransomguard-agent.db`) stored locally on each monitored endpoint.

## Migration Inventory

### Sprint 1-3 Baseline (5 migrations)

| # | Migration ID | Table(s) Created |
|---|-------------|-----------------|
| 1 | `20260518004118_InitialCreate` | AgentStates, Alerts, AuditLogs, DetectionEvents |
| 2 | `20260518011935_AddSentinelEntities` | SentinelCanaries, CanaryAlerts |
| 3 | `20260518053238_AddAuditLogSignature` | (adds Signature column to AuditLogs) |
| 4 | `20260518092123_AddEntropyEntities` | EntropyBaselines, EntropyAlerts |
| 5 | `20260518100157_AddGenealogyRecord` | GenealogyRecords |

### Sprint 4 Migrations (10 migrations)

| # | Migration ID | Table Created | Module | Indexes |
|---|-------------|--------------|--------|---------|
| 6 | `20260523010000_AddUsbWhitelist` | UsbWhitelistEntries | USB GUARD | SerialNumberHash, IsActive |
| 7 | `20260523010100_AddUsbPolicy` | UsbPolicies | USB GUARD | (none) |
| 8 | `20260523010200_AddUsbConnectionLog` | UsbConnectionLogs | USB GUARD | ConnectedAt, RetainUntil, SerialNumberHash |
| 9 | `20260523010300_AddUsbScanResult` | UsbScanResults | USB GUARD | ScannedAt, UsbConnectionLogId |
| 10 | `20260523010400_AddUsbAlert` | UsbAlerts | USB GUARD | GeneratedAt, UsbScanResultId |
| 11 | `20260523010500_AddQuarantine` | QuarantinedFiles | USB GUARD | QuarantinedAt, RetainUntil |
| 12 | `20260523020000_AddExfilAlert` | ExfilAlerts | EXFIL WATCH | DetectedAt, ProcessName, RuleName |
| 13 | `20260523020100_AddNetworkBaseline` | NetworkBaselines | EXFIL WATCH | Phase, Scope (unique) |
| 14 | `20260523020200_AddNetworkBaselineMetric` | NetworkBaselineMetrics | EXFIL WATCH | NetworkBaselineId, composite unique |
| 15 | `20260523030000_AddIndicatorRemovalEvent` | IndicatorRemovalEvents | INDICATOR REMOVAL | DetectedAt, EventType, KillChainCorrelationId |

**Total: 15 migrations (5 Sprint 1-3 + 10 Sprint 4), 19 tables, 21 Sprint 4 indexes.**

The ExfilAlerts table includes the `CrossLinkedEntropyAlertId` column for double-extortion correlation (Rule 8 cross-module linking) within the initial table creation migration rather than as a separate ALTER TABLE migration.

## Consolidated SQL Script

The file `artifacts/sprint-4-migrations.sql` contains the SQL for all 10 Sprint 4 migrations. It was generated using:

```powershell
dotnet ef migrations script 20260518100157_AddGenealogyRecord \
    --project RansomGuard.Agent.Core \
    --startup-project RansomGuard.Agent.Core \
    --output ../../artifacts/sprint-4-migrations.sql
```

Each migration is wrapped in a `BEGIN TRANSACTION` / `COMMIT` block, and each records itself in the `__EFMigrationsHistory` table. The full-stack script (`artifacts/all-migrations.sql`) covers Sprint 1 through Sprint 4.

## Rollback Test Procedure

### Script: `scripts/test-migration-rollback-sprint-4.ps1`

This script performs a complete apply-rollback-reapply cycle to verify migration reversibility.

**Steps:**

1. **Forward apply**: Creates a fresh SQLite database and applies all 15 migrations (Sprint 1-4). Verifies all 19 tables and 15 migration history entries exist.

2. **Reverse rollback**: Rolls back each Sprint 4 migration individually in reverse chronological order:
   - `AddIndicatorRemovalEvent` -> target `AddNetworkBaselineMetric`
   - `AddNetworkBaselineMetric` -> target `AddNetworkBaseline`
   - `AddNetworkBaseline` -> target `AddExfilAlert`
   - ... continuing through all 10 Sprint 4 migrations
   - `AddUsbWhitelist` -> target `AddGenealogyRecord` (Sprint 3 boundary)

   After each rollback step, the script verifies the migration count decrements correctly and the corresponding table is dropped.

3. **Sprint 3 baseline verification**: Confirms exactly 5 migrations remain, all 9 Sprint 3 tables are present, and no Sprint 4 tables remain.

4. **Forward re-apply**: Re-applies all 15 migrations and verifies all 19 tables return.

5. **Schema comparison**: Compares the SQLite schema fingerprint (all CREATE TABLE and CREATE INDEX statements) between the initial apply and the re-apply. Any difference indicates schema drift introduced by the rollback/reapply cycle.

6. **Cleanup**: Removes the test database.

**Expected output**: 16+ test assertions, all PASS.

### Running the rollback test:

```powershell
cd C:\Projects\RansomGuard-CM
.\scripts\test-migration-rollback-sprint-4.ps1
```

**Prerequisites**: `dotnet ef` CLI tool installed, `sqlite3` on PATH.

## v0.5.1 Upgrade Test Procedure

### Script: `scripts/test-migration-on-v0.5.1-database.ps1`

This script simulates upgrading an existing v0.5.1 deployment (Sprint 3 schema with production data) to Sprint 4.

**Steps:**

1. **Create v0.5.1 database**: Applies Sprint 1-3 migrations to create the baseline schema.

2. **Seed sample data**: Inserts representative rows into all Sprint 3 tables:
   - 1 AgentState, 3 Alerts, 5 AuditLogs (hash-chained), 4 DetectionEvents
   - 3 SentinelCanaries, 2 CanaryAlerts, 3 EntropyBaselines, 2 EntropyAlerts
   - 2 GenealogyRecords
   - **Total: 25 rows across 9 tables**

3. **Record pre-migration state**: Captures row counts for all Sprint 3 tables before upgrade.

4. **Apply Sprint 4 migrations**: Runs `dotnet ef database update` to apply all 10 Sprint 4 migrations on top of existing data.

5. **Verify no data loss**: Compares post-migration row counts against pre-migration counts for every Sprint 3 table. Any discrepancy is flagged as FAIL.

6. **Verify new tables empty**: Confirms all 10 Sprint 4 tables exist and contain zero rows (no orphan data introduced by migrations).

7. **Verify indexes**: Counts Sprint 4 indexes to ensure all 21 were created.

8. **Cleanup**: Removes the test database.

**Expected output**: 22+ test assertions, all PASS.

### Running the upgrade test:

```powershell
cd C:\Projects\RansomGuard-CM
.\scripts\test-migration-on-v0.5.1-database.ps1
```

## Schema Design Notes

### Column Types (SQLite)

SQLite uses a limited type affinity system. EF Core maps .NET types as follows:
- `Guid` -> `TEXT` (stored as string representation)
- `string` -> `TEXT` with optional `maxLength` constraint
- `int`, `long`, `bool` -> `INTEGER`
- `double` -> `REAL`
- `DateTime` -> `TEXT` (ISO 8601 format)
- `Guid?`, `DateTime?` -> nullable variants

### Enum Storage

Enums with `.HasConversion<string>()` are stored as their string name (e.g., `"Learning"`, `"Critical"`) rather than integer ordinals. This applies to: UsbPolicyLevel, UsbOperatingMode, QuarantineSeverity, BaselinePhase, BaselineMetricType, IndicatorRemovalType, CanaryAlertType, CanaryStatus.

### Index Strategy

Sprint 4 adds 21 indexes optimized for the most common query patterns:
- **Temporal indexes** (DetectedAt, ConnectedAt, ScannedAt, GeneratedAt, QuarantinedAt): support time-range queries for dashboard and retention cleanup
- **Retention indexes** (RetainUntil): support cleanup service scans for expired records
- **Correlation indexes** (UsbConnectionLogId, UsbScanResultId, KillChainCorrelationId, NetworkBaselineId): support cross-table joins for alert correlation
- **Unique constraints** (NetworkBaselines.Scope, NetworkBaselineMetrics composite): enforce business rules at the database level

## Known Issues

1. **SQLite idempotent scripts not supported**: EF Core's `--idempotent` flag is not available for SQLite. The generated SQL scripts are non-idempotent and will fail if applied to a database that already has the tables. The migration history table (`__EFMigrationsHistory`) provides the actual idempotency mechanism at runtime.

2. **No foreign key constraints in Sprint 4**: The Sprint 4 entities use Guid columns for cross-references (e.g., `UsbScanResult.UsbConnectionLogId`) without explicit FK constraints. This is intentional for SQLite compatibility and to avoid cascade-delete complexity in the single-file database model. Referential integrity is enforced at the application layer.

3. **Designer files use minimal BuildTargetModel**: The Sprint 4 Designer.cs files contain minimal model snapshots. This is sufficient for migration script generation and database update operations. If a new migration needs to be added after Sprint 4, regenerate the snapshot using `dotnet ef migrations add` to get a full differential snapshot.

## File Inventory

| File | Purpose |
|------|---------|
| `artifacts/sprint-4-migrations.sql` | Sprint 4 SQL (10 migrations) |
| `artifacts/all-migrations.sql` | Full SQL (Sprint 1-4, 15 migrations) |
| `scripts/test-migration-rollback-sprint-4.ps1` | Rollback cycle test |
| `scripts/test-migration-on-v0.5.1-database.ps1` | v0.5.1 upgrade test |
| `agent/src/RansomGuard.Agent.Core/Persistence/Migrations/` | Migration source files |
| `agent/src/RansomGuard.Agent.Core/Persistence/DesignTimeDbContextFactory.cs` | EF CLI factory |
