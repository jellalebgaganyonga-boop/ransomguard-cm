# RansomGuard-CM -- Sprint 2 Senior Audit

**Auditor:** Claude Opus 4.6 (1M context)
**Date:** 2026-05-18
**Scope:** SENTINEL module conformance against PR-FAQ and Design Doc
**Verdict:** CONDITIONALLY PASS with 3 items requiring remediation

---

## Task 1 — SPRINT_2_REPORT.md

Full content verified in chat. Report is accurate and complete. Test count (132), commit hashes, migration name, and file tree all match actual codebase state.

---

## Task 2 — Architectural Conformance Check

### R1: Semantic medical canaries — PASS

**Evidence:** `CanaryContentGenerator.cs:62-74` dispatches to 5 dedicated generators. Each produces 1.5-2.5 KB of structured French medical content with:
- Patient demographics (Cameroonian names)
- Medical conditions (malaria, hypertension, sickle cell — real Cameroon epidemiology)
- Treatment protocols (WHO-standard medications)
- Lab values with normal ranges
- Hospital branding ("HOPITAL CENTRAL DE YAOUNDE")

Live deployment confirmed: `0001_dossier_patient_223229b3.txt` (2435 bytes) on actual Desktop.

### R2: French-language native — PASS with caveat

**Evidence:** `CanaryContentGenerator.cs:13-17` contains 20 Cameroonian last names (Mballa, Ngoa, Kamga, Tchamba, Bekolo, Etoundi, Mvondo, etc.). First names are French (Jean-Pierre, Marie-Claire, Francoise).

**No real PHI:** All names are common Cameroonian names, not specific individuals. Content is procedurally generated. Confirmed via code inspection.

**Caveat — Bilingual support NOT implemented:** The PR-FAQ states "French AND English variants for federal hospitals." The current implementation is French-only. No English templates exist. **This is a documented gap.** Severity: P2 (English-speaking hospitals in Northwest/Southwest regions would need this).

### R3: Alphabetically prominent placement — PASS

**Evidence:** `CanaryFileService.cs:36` — `$"{prefix}{template}_{shortId}.txt"` uses configurable prefix. Default is `"0001_"` from `SentinelOptions.CanaryPrefix`.

**Live proof:** `ls Desktop/0001_*` shows canaries sort before any user file.

### R4: Self-regeneration — PARTIAL PASS

**Evidence:** `SentinelDeploymentService.cs:88-96` counts active canaries per directory and deploys missing ones. Deleted canaries (status != Active) are excluded from the count, triggering redeployment.

**Critical caveat — STARTUP ONLY:** Regeneration happens ONLY when the service starts. There is NO runtime regeneration loop. If a canary is deleted while the agent is running, it stays deleted until next restart. The `SentinelMonitor` detects the deletion and fires an alert, but does NOT trigger redeployment.

**Severity: P1.** The spec says "Self-regenerating — if a canary is deleted, a new one is created." Current behavior: alert fires immediately, but regeneration requires restart.

### R5: Process attribution — FAIL (DEAD CODE)

**Evidence:** `SentinelMonitor.cs:195-223` — `TryAttributeProcess` is called on every alert BUT **always returns `(null, null, null)`**. The method body enumerates processes and checks `MainModule?.FileName` but never actually matches anything to the file path. The inner `if` block on line 206 is EMPTY — no matching logic exists.

**This is dead code.** Process attribution is documented as a feature but does not function. Every alert will have `OffendingProcessId = null`, `OffendingProcessName = null`, `OffendingProcessPath = null`.

**Severity: P1.** The documentation and alert schema promise attribution. The code performs no attribution. The test (`Alert_should_store_process_attribution_fields`) only validates that the fields CAN be stored if populated manually — it does not test that the attribution logic works.

**Honest assessment:** Real process-to-file-handle attribution on Windows requires either:
1. ETW (Event Tracing for Windows) kernel provider for file I/O
2. `NtQuerySystemInformation` with `SystemHandleInformation`
3. Restart Manager API (`RmGetList`)

None of these are implemented. This was correctly identified as a limitation in docs/modules/sentinel.md, but the code structure implies it works when it does not.

### R6: Hash-chained audit log — PASS

**Evidence:** `SentinelMonitor.cs:182-188` calls `auditRepo.AppendAsync("CanaryAlert", details, "CanaryAlert", alert.Id)` for every alert. This feeds into the same hash-chained `AuditLogRepository` from Sprint 1.

**Test:** `SentinelMonitorTests.Alert_should_be_audit_logged` verifies the entry is written with correct Action and EntityId. Sprint 1 tests verify chain integrity survives new entries.

### R7: Detection latency < 100ms — PASS (architecture), UNVERIFIED (measurement)

**Evidence:** `SentinelMonitor.cs:53-71` sets up `FileSystemWatcher` per directory with `EnableRaisingEvents = true`. Events are processed in `OnCanaryFileEvent` (async void — see issues below). The deduplicator from Sprint 1.5 is wired at line 92.

**No performance test exists.** The claim "< 100ms" is architecturally plausible (FSW events are near-instant) but has not been measured with instrumentation. **Severity: P2.**

### R8: Multi-directory deployment — PASS

**Evidence:** `SentinelDeploymentService.cs:47-71` iterates `resolvedDirs` array. Live run confirmed: 9 canaries across 3 directories (Desktop, Documents, TestZone).

### R9: Idempotent deployment — PASS

**Evidence:** `SentinelDeploymentService.cs:88-96` — `existingActive` is counted, `needed = CanariesPerDirectory - existingActive.Count`. If `needed <= 0`, returns 0.

**Test:** `SentinelDeploymentTests.Should_be_idempotent_no_duplicate_on_second_run` verifies this.

### R10: Graceful degradation — PARTIAL PASS

**Evidence:** `SentinelDeploymentService.cs:67-70` catches exceptions per directory and logs error, continues to next directory.

**Missing test:** No test for disk-full scenario. The directory-not-exist case is handled (directory is created), but not tested as a failure scenario. **Severity: P3.**

---

## Task 3 — Code Quality Review

| Check | Result |
|-------|--------|
| File-scoped namespaces | PASS — all 8 new production files |
| Sealed classes | PASS — CanaryFileService, SentinelCanary, CanaryAlert, etc. Note: CanaryContentGenerator is `static` (correct for utility) |
| Records for DTOs | PASS — SentinelOptions is a record; entities are sealed classes (correct: entities are not DTOs) |
| Nullable reference types | PASS — `<Nullable>enable</Nullable>` on all projects |
| XML documentation | PASS — all public APIs documented |
| CancellationToken | PASS on all async methods except `OnCanaryFileEvent` (see issues) |
| ILogger structured logging | PASS — no Console.WriteLine |
| No TODO/FIXME/HACK | PASS — `git grep` confirmed clean |
| No magic numbers | PASS — delay `2000` in SentinelMonitor.cs:51 could be named, but is documented inline |
| No hardcoded paths | PASS — all from configuration |

**Issue found:** `SentinelMonitor.cs:90` — `async void OnCanaryFileEvent`. This is an `async void` method which means:
1. Exceptions will crash the process (unobserved)
2. No CancellationToken passed
3. The caller cannot await it

This is a known pattern compromise for event handler delegates (FSW events require `void` return), but the error handling at line 110-113 catches exceptions. **Severity: P2 — acceptable for now but should be refactored to use a channel-based approach like Worker.cs.**

---

## Task 4 — Test Quality Review

### All 53 SENTINEL tests by category:

**Configuration (10 tests)** — `SentinelConfigurationTests.cs`:
- Lines 14,22,31,38,45,52,59,66,73,80 — validate boundaries and rules

**Content Generation (10 tests)** — `CanaryContentGeneratorTests.cs`:
- 5 template tests, keyword checks, uniqueness, hash determinism, law reference, fallback

**Repository (7 tests)** — `SentinelCanaryRepositoryTests.cs`:
- CRUD, path lookup, active filter, directory filter, status update, alert persist

**File Service (8 tests)** — `CanaryFileServiceTests.cs`:
- Create on disk, persist to DB, verify intact, verify modified, verify missing, delete, list active, uniqueness

**Deployment (6 tests)** — `SentinelDeploymentTests.cs`:
- Count, idempotency, directory creation, re-deploy after delete, template diversity, prefix

**Monitor (6 tests)** — `SentinelMonitorTests.cs`:
- Modification detection, deletion detection, rename detection, audit logging, integrity pass, attribution fields

**E2E (3 tests)** — `SentinelEndToEndTests.cs`:
- Ransomware simulation (alphabetical ordering), content modification, regeneration after deletion

### Test weakness assessment:

**Strong tests:** `CanaryFileServiceTests.VerifyCanaryIntegrityAsync_should_return_false_for_modified` (real file tampering), `SentinelEndToEndTests.Scenario1_ransomware_renames_all_files_canary_fires_first` (real alphabetical sorting proof).

**Weak test:** `SentinelMonitorTests.Alert_should_store_process_attribution_fields` — manually constructs a CanaryAlert with PID/Name/Path and verifies they persist. This does NOT test that `TryAttributeProcess` actually works. It tests database storage, not detection.

**Missing tests:** No test for the real-time FSW event path in SentinelMonitor (the `OnCanaryFileEvent` method). All monitor tests exercise the periodic check logic or manual alert creation. The FSW → database pipeline is untested.

---

## Task 5 — Live Sanity Check

**Build:** `dotnet build --no-incremental` — 0 warnings, 0 errors
**Tests:** 132 passed, 0 failed
**Live run:** 9 canaries deployed across 3 directories in < 1 second
**Database:** Created at `C:\ProgramData\RansomGuard-CM\data\agent.db` (4096 bytes)
**Canary content:** Real French medical record visible on Desktop
**Cleanup:** All canaries removed

---

## Task 6 — Documentation Review (docs/modules/sentinel.md)

| Section | Present | Accurate |
|---------|---------|----------|
| What SENTINEL does | Yes | Yes |
| Configuration reference | Yes | Yes, all 6 options |
| Deployment behavior | Yes | Incomplete — does not mention startup-only regeneration |
| Alert types and severities | Yes | Yes |
| Process attribution capabilities | Yes | Yes |
| Process attribution limitations | Yes | Honest about ETW requirement |
| Sample content | No | **MISSING** — report has sample but docs/modules/sentinel.md does not |
| Performance characteristics | Yes | Unverified claims |
| Known limitations | Yes | 4 items listed |
| Roadmap items | No | **MISSING** — no explicit roadmap section |

---

## BRUTAL HONESTY

### 1. Does SENTINEL actually do what the PR-FAQ promised?

**Mostly yes, with 2 significant gaps:**
- Process attribution is DEAD CODE — always returns null
- English bilingual variants are NOT implemented (French-only)

Everything else works as documented: semantic canaries, alphabetical placement, hash verification, real-time monitoring, multi-directory deployment, audit logging.

### 2. What was implemented as "good enough" but not production-grade?

| Item | Severity | Issue |
|------|----------|-------|
| Process attribution | P1 | Dead code. Returns null always. |
| Runtime canary regeneration | P1 | Only on startup. No runtime re-deploy loop. |
| `async void` event handler | P2 | SentinelMonitor.OnCanaryFileEvent — exceptions in async void crash the process |
| .txt format only | P2 | Not convincing to ransomware that uses file extension heuristics |
| No FSW pipeline test | P2 | Real-time detection path is untested |
| `Task.Delay(2000)` hardcoded | P3 | Monitor waits 2 seconds for deployment — should be configurable or use coordination |

### 3. What was promised but NOT implemented?

- **English bilingual canary content** — PR-FAQ says "French AND English." Code is French-only.
- **Working process attribution** — Code structure and schema exist but logic is a no-op.
- **Performance benchmark script** — Sprint 2 spec mentioned `scripts/benchmark-sentinel.ps1`. Not created.

### 4. What ransomware scenarios could BYPASS SENTINEL?

1. **Ransomware that skips .txt files** — Many modern ransomware families target only high-value extensions (.docx, .xlsx, .pdf, .mdb). Our canaries are .txt files. A ransomware that ignores .txt would bypass SENTINEL entirely.

2. **Ransomware that encrypts in reverse alphabetical order (Z→A)** — Our canaries sort FIRST. If ransomware starts from the end, real files are hit before canaries.

3. **Ransomware that reads file content before encrypting** — If it detects the file is a decoy (e.g., all files are ~2KB .txt with similar structure), it could skip them. Our canaries are similar size and format, making pattern detection possible.

4. **Ransomware that uses Volume Shadow Copy deletion + direct disk write** — Bypasses FileSystemWatcher entirely. No file system event is generated.

5. **Ransomware that kills the RansomGuard process first** — Without self-protection (kernel driver, PPL), the agent can be terminated before encryption begins.

### 5. Is test coverage meaningful?

**Mostly yes.** Content generation tests verify real French keywords. File service tests create and tamper with real files on disk. E2E Scenario 1 proves alphabetical ordering.

**Weak spots:**
- Process attribution test is a tautology (tests storage, not detection)
- No test for the real-time FSW → alert pipeline in SentinelMonitor
- No test for concurrent canary access (two processes touching canaries simultaneously)

### 6. If deployed in a Cameroonian hospital tomorrow, what breaks first?

**The canary files would be visible to hospital staff.** Files named `0001_dossier_patient_223229b3.txt` appearing on every user's Desktop would cause confusion and support tickets. Hospital IT would either delete them (triggering false alerts) or ask us to hide them. We need:
- Hidden file attribute set on canaries (`File.SetAttributes(path, FileAttributes.Hidden)`)
- Deployment to less visible directories (not Desktop directly)
- User education or documentation for IT technicians

### 7. Technical debt created in Sprint 2

| Priority | Debt |
|----------|------|
| P1 | Process attribution is dead code — must implement or remove |
| P1 | No runtime canary regeneration — only on startup |
| P2 | `async void` in SentinelMonitor.OnCanaryFileEvent |
| P2 | Canary files not hidden (FileAttributes.Hidden) |
| P2 | .txt format defeats ransomware that filters by extension |
| P2 | No benchmark script as promised |
| P2 | SentinelMonitor uses `Task.Delay(2000)` for startup coordination |
| P3 | Sample content missing from docs/modules/sentinel.md |
| P3 | No explicit roadmap section in docs |

### 8. Race conditions, thread safety, or resource leaks?

1. **`async void OnCanaryFileEvent`** — If an exception escapes the catch block (unlikely but possible with `OutOfMemoryException`), it will terminate the process. No cancellation token means the async operation can outlive the service.

2. **Multiple DbContext scopes in concurrent FSW events** — If two canary files are touched simultaneously, two `OnCanaryFileEvent` calls fire concurrently, each creating a separate DI scope and DbContext. SQLite with concurrent writes could throw `SqliteException: database is locked`. The retry logic does not exist.

3. **FileSystemWatcher disposal** — `SentinelMonitor` disposes watchers in the cancellation path, but if `ExecuteAsync` throws before reaching the disposal loop, watchers leak. Should use `try/finally`.

4. **No `Process.Dispose()` in `TryAttributeProcess`** — `Process.GetProcesses()` returns `Process[]` objects that implement `IDisposable`. They are never disposed. This leaks handles on every periodic check. **This is a real resource leak** (though the method currently does nothing useful, so it's called on every periodic check).

### 9. Microsoft Defender team quality or competent junior quality?

**Competent mid-level engineer quality.** The architecture is sound (separation of concerns, DI, async patterns, repository pattern). But a Microsoft Defender Principal Engineer would flag:

- Dead code shipped as a "feature" (process attribution) — unacceptable at Microsoft
- `async void` without justification document
- Resource leaks in process enumeration
- No integration tests for the real-time detection pipeline
- Hardcoded `Task.Delay(2000)` instead of proper service coordination (e.g., `TaskCompletionSource` or `IHostedService` ordering)

It's above junior level (the architecture works, tests are meaningful, code is clean), but below senior level (the gaps above would block a code review at MSFT).

### 10. What MUST be done before Sprint 3?

1. **Fix or remove `TryAttributeProcess`** — Either implement real attribution via Restart Manager API or remove the dead code and set attribution fields to null explicitly with a log message. Do NOT ship dead code pretending to be a feature.

2. **Fix the `Process.Dispose()` leak** — Add proper disposal of `Process` objects in `TryAttributeProcess`.

3. **Add `FileAttributes.Hidden`** to deployed canaries — Hospital staff will delete visible canaries, generating false alerts that destroy trust in the product.

4. **Refactor `async void` to channel-based approach** — Match the pattern used in `Worker.cs` with bounded channels.

5. **Add FSW pipeline integration test** — The most critical detection path (real-time) is completely untested.

---

## Audit Verdict

**CONDITIONALLY PASS** — SENTINEL is architecturally sound and functionally operational for its core mission (canary deployment, hash verification, alert generation). The 3 conditions for full pass are:

1. Fix process attribution dead code (remove or implement)
2. Add `FileAttributes.Hidden` to canaries
3. Fix `Process.Dispose()` resource leak

These can be addressed in a Sprint 2.5 debt cleanup (1-2 hours of work) before Sprint 3 begins.
