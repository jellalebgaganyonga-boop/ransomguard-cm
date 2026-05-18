# RansomGuard-CM -- Sprint 2.5-B Report: Deferred Work Completed

**Date:** 2026-05-18
**Version:** v0.4.2-sentinel-complete
**Purpose:** Complete all 4 tasks deferred from Sprint 2.5

---

## All Tasks: DONE (0 deferrals)

| Task | Description | Status | Tests Added |
|------|-------------|--------|-------------|
| B4 | Ed25519 audit log signing (NSec.Cryptography) | DONE | +8 |
| C1 | Self-integrity check (SHA-256 companion file) | DONE | +4 |
| C3 | DACL hardening (DaclEnforcer) | DONE | +6 |
| A5 | Multi-format canaries (.pdf, .jpg, .xlsx) | DONE | +13 |

---

## Test Count Progression

| Sprint | Tests |
|--------|-------|
| Sprint 2 (end) | 132 |
| Sprint 2.5 (end) | 161 |
| Sprint 2.5-B (end) | **192** |

---

## What Was Built

### B4 — Ed25519 Cryptographic Audit Log Signing
- **NSec.Cryptography 24.4.0** (libsodium binding) for Ed25519
- Private key stored via Windows DPAPI (`LocalMachine` scope)
- Public key exported to `%ProgramData%\RansomGuard-CM\keys\audit.pub`
- Canonical signing payload: `Id || Timestamp(ticks BE) || Action || PreviousHash || CurrentHash`
- `AuditLogRepository` auto-signs when signer is injected
- Chain verification validates both hash chain AND Ed25519 signatures
- Migration `AddAuditLogSignature` adds `Signature` column (NULL for pre-existing rows)

### C1 — Self-Integrity Check
- `SelfIntegrityChecker` verifies exe hash against `.sha256` companion file
- `CryptographicOperations.FixedTimeEquals` for timing-attack safety (CWE-208)
- MSBuild target `GenerateSelfHash` runs after `dotnet publish`
- Graceful skip for dev builds (no hash file = no check)

### C3 — DACL Hardening
- `DaclEnforcer` with `EnforceOnFile` and `EnforceOnDirectory`
- Removes all inherited permissions, adds only SYSTEM + Administrators
- Optional admin-read-only mode for keys directory
- Graceful degradation when running without admin privileges

### A5 — Complete Multi-Format Canaries
- **PdfSharpCore 1.3.x** (MIT) for valid PDF with `%PDF` magic bytes
- **SixLabors.ImageSharp 2.1.x** (Apache 2.0) for valid JPEG (800x600, medical imaging style)
- **DocumentFormat.OpenXml** for valid XLSX with 15-row patient registry
- All formats verified by parser tests (OpenXml, ImageSharp, magic bytes)

---

## Bypass Scenario Coverage

| Scenario | Before | After |
|----------|--------|-------|
| Ransomware skips .txt files | VULNERABLE | BLOCKED (.docx/.pdf/.xlsx/.jpg) |
| Reverse-alphabetical encryption | VULNERABLE | MITIGATED (0001_ prefix) |
| Content heuristic detection | VULNERABLE | MITIGATED (diverse formats/sizes) |
| Direct disk write bypassing FSW | VULNERABLE | PARTIALLY (periodic polling fallback) |
| Process kill before encryption | VULNERABLE | MITIGATED (service auto-restart) |

---

## Cryptographic Operations Summary

| Operation | Implementation |
|-----------|---------------|
| Ed25519 signing | NSec.Cryptography, ~0.1ms per sign |
| Ed25519 verify | NSec.Cryptography, ~0.2ms per verify |
| SHA-256 hashing | System.Security.Cryptography, native |
| DPAPI key storage | ProtectedData, LocalMachine scope |
| Byte comparison | CryptographicOperations.FixedTimeEquals (CWE-208) |

---

## Sprint 3 Readiness: YES

All Sprint 2 audit findings are now RESOLVED:
- Process attribution: Real Restart Manager API (was dead code)
- Canary regeneration: Real-time with anti-loop (was startup-only)
- Hidden attributes: Applied (was visible to users)
- Async void: Replaced with bounded channels (was resource leak)
- Multi-format: .txt + .docx + .pdf + .jpg + .xlsx (was .txt only)
- Ed25519 signing: Implemented (was deferred)
- Self-integrity: Implemented (was deferred)
- DACL hardening: Implemented (was deferred)
- Database encryption: SQLCipher AES-256 (was plaintext)
- Path validation: PathValidator CWE-22/23/73 (was none)

**192 tests, 0 warnings, 0 errors. Zero deferrals remaining.**
