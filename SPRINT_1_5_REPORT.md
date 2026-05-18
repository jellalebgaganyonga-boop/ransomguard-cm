# RansomGuard-CM -- Sprint 1.5 Report: Technical Debt Cleanup

**Date:** 2026-05-18
**Version:** v0.3.1-debt-cleanup
**Purpose:** Resolve P0 and P1 technical debt before Sprint 2 (SENTINEL module)

---

## Summary of Debt Resolved

Three critical technical debts identified in Sprint 1 code review have been resolved:

1. **P0 BLOCKING -- EF Core Migrations**: Replaced `Database.EnsureCreated()` with `Database.Migrate()` and generated the `InitialCreate` migration. Production hospitals can now receive schema updates without losing data.

2. **P1 CRITICAL -- Event Deduplication**: Added `FileEventDeduplicator` with configurable sliding window (default 500ms) to eliminate the 2-4x event amplification inherent in Windows FileSystemWatcher. Prevents SENTINEL from running entropy analysis on duplicate events.

3. **P1 CRITICAL -- License Compliance**: Migrated all 79 tests from FluentAssertions 8.x (commercial Xceed license) to Shouldly 4.3.0 (MIT, free forever). ADR-016 documents the decision.

---

## Files Created/Modified Per Task

### Task 1 -- EF Core Migrations
| Action | File |
|--------|------|
| Created | `agent/src/RansomGuard.Agent.Core/Persistence/Migrations/20260518004118_InitialCreate.cs` |
| Created | `agent/src/RansomGuard.Agent.Core/Persistence/Migrations/20260518004118_InitialCreate.Designer.cs` |
| Created | `agent/src/RansomGuard.Agent.Core/Persistence/Migrations/AgentDbContextModelSnapshot.cs` |
| Created | `agent/src/RansomGuard.Agent.Tests/Persistence/MigrationTests.cs` |
| Modified | `agent/src/RansomGuard.Agent.Service/Program.cs` (EnsureCreated -> Migrate) |
| Modified | `agent/src/RansomGuard.Agent.Service/RansomGuard.Agent.Service.csproj` (added EF Design) |

### Task 2 -- Event Deduplication
| Action | File |
|--------|------|
| Created | `agent/src/RansomGuard.Agent.Core/Detection/IFileEventDeduplicator.cs` |
| Created | `agent/src/RansomGuard.Agent.Core/Detection/FileEventDeduplicator.cs` |
| Created | `agent/src/RansomGuard.Agent.Tests/Detection/FileEventDeduplicatorTests.cs` |
| Modified | `agent/src/RansomGuard.Agent.Core/Configuration/AgentConfiguration.cs` (DeduplicationWindowMs) |
| Modified | `agent/src/RansomGuard.Agent.Core/Configuration/AgentConfigurationValidator.cs` (validation rule) |
| Modified | `agent/src/RansomGuard.Agent.Service/Program.cs` (DI registration) |
| Modified | `agent/src/RansomGuard.Agent.Service/Worker.cs` (dedup integration + counters) |
| Modified | `agent/src/RansomGuard.Agent.Service/appsettings.json` (DeduplicationWindowMs) |

### Task 3 -- Shouldly Migration
| Action | File |
|--------|------|
| Created | `docs/architecture/adr/ADR-016-test-assertion-library.md` |
| Modified | 9 test files (FluentAssertions -> Shouldly syntax) |
| Modified | `agent/src/RansomGuard.Agent.Tests/RansomGuard.Agent.Tests.csproj` (package swap) |

---

## Test Count

| Metric | Before Sprint 1.5 | After Sprint 1.5 |
|--------|-------------------|------------------|
| Total tests | 67 | 79 |
| Passing | 67 | 79 |
| Failing | 0 | 0 |
| New tests added | -- | +12 (3 migration + 9 deduplication) |

---

## Verification Results

### EnsureCreated completely removed from production code
```
grep -r "EnsureCreated" agent/src/ --include="*.cs" (excluding test files)
=> 0 results in production code
=> 4 results in test files only (correct: in-memory SQLite for unit tests)
```

### FluentAssertions package fully removed
```
grep -r "FluentAssertions" agent/src/ --include="*.cs"
=> 0 results

grep "FluentAssertions" agent/src/RansomGuard.Agent.Tests/RansomGuard.Agent.Tests.csproj
=> 0 results
```

### ADR-016 location
`docs/architecture/adr/ADR-016-test-assertion-library.md`

---

## Updated Technical Debt Backlog (P2/P3 Only)

| Priority | Item | Notes |
|----------|------|-------|
| P2 | Pin NuGet versions (remove `8.0.*` wildcards) | Non-reproducible builds |
| P2 | Align Microsoft.Extensions versions to 8.0.x | Mixed 8.0/10.0 versions |
| P3 | Add explicit Serilog sink tests | Covered implicitly via ILogger |
| P3 | Add integration tests (real FSW + real SQLite file) | All current tests are unit tests |

---

## Sprint 2 Readiness Confirmation

All P0 and P1 blockers are resolved:
- Schema can be safely evolved with migrations (SENTINEL entities ready)
- FSW events are deduplicated (SENTINEL entropy analysis will be accurate)
- License compliance secured (Shouldly MIT, no commercial risk)
- 79 tests green, 0 warnings, 0 errors
- Codebase ready for SENTINEL module development
