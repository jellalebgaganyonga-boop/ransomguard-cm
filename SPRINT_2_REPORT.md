# RansomGuard-CM -- Sprint 2 Report: SENTINEL Module

**Date:** 2026-05-18
**Version:** v0.4.0-sentinel
**Module:** SENTINEL (1/5 innovation modules)

---

## Summary

Sprint 2 delivers the first innovation module of RansomGuard-CM: SENTINEL — Semantic Medical Canary Files. The module deploys realistic synthetic French-language medical records as decoy files in strategic directories. These canary files sort alphabetically before real data (prefix `0001_`) and are monitored in real-time via FileSystemWatcher plus periodic polling. When ransomware enumerates a directory and begins encrypting or renaming files, it hits canaries FIRST, triggering Critical-severity alerts before real patient data is compromised. The module includes 5 medical content templates (patient records, lab analyses, imaging reports, prescriptions, consultation reports) with curated Cameroonian names. All 132 tests pass with 0 warnings and 0 errors.

---

## Files Created

### Detection/Sentinel/ (Core Library)
```
agent/src/RansomGuard.Agent.Core/Detection/Sentinel/
+-- CanaryContentGenerator.cs      (5 medical content templates)
+-- ICanaryFileService.cs           (canary file operations interface)
+-- CanaryFileService.cs            (create/verify/delete/list implementation)
```

### Persistence Entities
```
agent/src/RansomGuard.Agent.Core/Persistence/Entities/
+-- SentinelCanary.cs               (canary file tracking entity)
+-- CanaryAlert.cs                  (high-confidence alert entity)
```

### Repositories
```
agent/src/RansomGuard.Agent.Core/Persistence/Repositories/
+-- ISentinelCanaryRepository.cs    (repository interface)
+-- SentinelCanaryRepository.cs     (SQLite implementation)
```

### Services
```
agent/src/RansomGuard.Agent.Service/
+-- SentinelDeploymentService.cs    (auto-deploy on startup)
+-- SentinelMonitor.cs              (periodic + real-time monitoring)
```

### Migrations
```
agent/src/RansomGuard.Agent.Core/Persistence/Migrations/
+-- 20260518011935_AddSentinelEntities.cs
+-- 20260518011935_AddSentinelEntities.Designer.cs
```

### Tests
```
agent/src/RansomGuard.Agent.Tests/Sentinel/
+-- SentinelConfigurationTests.cs       (8 tests)
+-- SentinelCanaryRepositoryTests.cs    (7 tests)
+-- CanaryContentGeneratorTests.cs      (10 tests)
+-- CanaryFileServiceTests.cs           (8 tests)
+-- SentinelDeploymentTests.cs          (6 tests)
+-- SentinelMonitorTests.cs             (6 tests)
+-- SentinelEndToEndTests.cs            (3 tests)
```

### Documentation
```
docs/modules/sentinel.md               (complete module documentation)
```

---

## Files Modified

| File | Changes |
|------|---------|
| `AgentConfiguration.cs` | Added SentinelOptions record with validation |
| `AgentConfigurationValidator.cs` | Added SentinelOptionsValidator |
| `AgentDbContext.cs` | Added SentinelCanaries + CanaryAlerts DbSets with entity config |
| `Program.cs` | Wired SENTINEL DI: repository, file service, deployment, monitor |
| `appsettings.json` | Added Sentinel configuration section |
| `AgentDbContextModelSnapshot.cs` | Updated by migration |

---

## Migration Applied

**Name:** `AddSentinelEntities` (20260518011935)

**Tables added:**
- `SentinelCanaries` — tracks deployed canary files
- `CanaryAlerts` — high-confidence tampering alerts

**Indexes:**
- `IX_SentinelCanaries_FilePath` (unique)
- `IX_SentinelCanaries_Directory`
- `IX_SentinelCanaries_Status`
- `IX_CanaryAlerts_DetectedAt`
- `IX_CanaryAlerts_CanaryId`

---

## Test Count

| Metric | Before Sprint 2 | After Sprint 2 |
|--------|-----------------|----------------|
| Total tests | 79 | 132 |
| Passing | 79 | 132 |
| Failing | 0 | 0 |
| New tests | -- | +53 |

---

## End-to-End Scenarios

| Scenario | Status |
|----------|--------|
| Ransomware renames all files — canary fires FIRST | Passing |
| Single canary content modification detected | Passing |
| Canary regeneration after deletion | Passing |

---

## Sample Canary Content (dossier_patient template)

```
DOSSIER MEDICAL CONFIDENTIEL
HOPITAL CENTRAL DE YAOUNDE

Patient         : Mballa Jean-Pierre
Date de naissance : 15/03/1978
Numero de dossier : DM-45892-317
Medecin traitant  : Dr. Nkeng Patrice

ANTECEDENTS MEDICAUX
  - Paludisme severe (P. falciparum)
  - Hypertension arterielle stade 2

TRAITEMENT EN COURS
  - Amlodipine 10mg, 1x/jour le matin
  - Metformine 850mg, 2x/jour aux repas

Ce document est confidentiel. Toute divulgation non autorisee
est sanctionnee par la Loi N°2024/017.
```

---

## Known Limitations

1. **Text format only** — canaries use `.txt`. Real `.docx`/`.pdf` generation requires Office interop (planned Sprint 4)
2. **Process attribution best-effort** — needs ETW kernel tracing for production accuracy (planned Sprint 3)
3. **No canary content rotation** — canaries are static after deployment; periodic refresh planned
4. **No network isolation trigger** — alert fires but doesn't yet isolate the machine (planned Sprint 3)
5. **Single-process monitoring** — monitors one directory per watcher; volume-level monitoring via ETW planned

---

## Sprint 3 Prerequisites

The following are ready for Sprint 3 (Shannon Entropy + GENEALOGY):
- SENTINEL alert pipeline feeds into the 3-converging-signals rule
- Detection events and canary alerts share the same audit log chain
- Configuration system extensible for entropy thresholds and process tree rules
- Event deduplicator works across both Worker FSW events and SENTINEL FSW events
