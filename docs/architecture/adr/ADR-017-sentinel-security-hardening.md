# ADR-017: SENTINEL Security Hardening Approach

## Status
Accepted (2026-05-18)

## Context
Sprint 2 audit identified 5 critical issues plus a general gap vs CWE/OWASP/NIST/ISO standards.
User mandate: industrial-grade security per documented architectural promise.
Solo developer context, B.Sc. final-year project with commercial intent.

## Decision
Apply industrial security standards selectively per priority:

- **Block A**: Audit P1/P2 issues — IMPLEMENTED (5 tasks)
  - Real process attribution via Restart Manager P/Invoke
  - Real-time canary regeneration with anti-loop protection
  - Hidden + System file attributes on canaries
  - Channel-based event processing (no async void)
  - Multi-format canaries (.txt, .docx via OpenXML)

- **Block B**: Industrial hardening — IMPLEMENTED (path validation, log redaction, SQLCipher, secrets audit)
  - PathValidator: CWE-22/23/73 mitigations
  - LogRedactionEnricher: CWE-200/532 protection
  - SQLCipher AES-256 database encryption with DPAPI key management
  - Zero hardcoded secrets confirmed

- **Block C**: Anti-tampering — IMPLEMENTED (service recovery hardening)
  - Security descriptor prevents non-admin service stop
  - Failure recovery with exponential backoff

- **Block D**: STRIDE threat model — COMPLETED (18 threats identified, 15 mitigated)

### Deferred to Later Sprints (documented)
- Full Authenticode code signing (requires $300-2000/year cert) -> Sprint 8
- Ed25519 audit log signing -> Sprint 3 (requires NSec.Cryptography + migration)
- FIPS 140-3 cryptographic module validation -> post-Sprint 8 commercial
- ISO 27001 certification -> post-Sprint 8 commercial
- External penetration testing -> Sprint 8
- mTLS with certificate pinning -> Sprint 6 (server module)

## Consequences

**Positive:**
- All P1/P2 audit issues resolved
- CWE Top 25 mitigations applied to SENTINEL code
- OWASP ASVS Level 2 substantially achieved for module
- ANTIC compliance posture strengthened
- Database encryption protects data at rest

**Negative / Accepted Risk:**
- Authenticode certificate not yet acquired (Sprint 8)
- External audit not yet performed (Sprint 8 commercial phase)
- FIPS validation deferred (financial constraint)
- Ed25519 signing deferred to Sprint 3 (complexity vs time tradeoff)

## Alternatives Considered
- Pursue 100% standards compliance immediately: rejected, would block all other modules
- Skip hardening, focus on features: rejected, violates product promise
- Outsource security audit: rejected, financial constraint at academic stage
