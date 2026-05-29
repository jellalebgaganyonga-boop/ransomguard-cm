# ADR-023: GRID Server Tech Stack — FastAPI Python

**Status:** Accepted
**Date:** 2026-05-29
**Sprint:** 6

## Context

The GRID server needs to support ~50-200 agents per hospital deployment with mTLS authentication for agents, JWT for dashboard operators, multi-tenant data isolation, background workers (threat intel updater), real-time alert ingestion, and audit log integrity with Ed25519 signatures.

Candidates considered: FastAPI Python, ASP.NET Core 8, Go Gin, Node.js Express.

## Decision

**FastAPI Python 3.12** with SQLAlchemy 2.0 async, Alembic, aiomysql+asyncmy (MySQL drivers), Pydantic 2, python-jose (JWT), PyNaCl (Ed25519), structlog, and httpx.

## Rationale

1. **Async-first**: FastAPI native async/await matches agent concurrent load pattern
2. **Type safety**: Pydantic 2 + mypy strict catches errors at design time
3. **Auto OpenAPI docs**: Critical for Sprint 7 agent SDK generation and React dashboard integration
4. **Developer availability**: Easier to find Cameroonian developers familiar with Python than .NET or Go
5. **Cryptography parity**: PyNaCl Ed25519 byte-compatible with agent's NSec C# (Sprint 2.5)
6. **Lower memory footprint**: ~100 MB vs .NET's ~300 MB minimum, critical for $30/month VPS hosting

## Alternatives Considered

**ASP.NET Core 8**: Excellent performance and type safety, but higher memory footprint (300 MB minimum), smaller developer pool in Cameroon, and no advantage in crypto since both use Ed25519. Would unify the stack with the C# agent but at operational cost.

**Go Gin**: Excellent performance and low memory, but no mature async ORM (GORM is synchronous), manual SQL builds increase bug surface, and smaller developer pool in target deployment region.

**Node.js Express**: Single-threaded event loop harder to debug under load, TypeScript compile step adds development friction, crypto libraries less mature than PyNaCl for Ed25519 operations.

## Consequences

**Positive:**
- 94 tests written in Sprint 6, high development velocity
- FastAPI auto-docs ready for Sprint 7 React Dashboard consumption
- Multi-tenant isolation tested with 10/10 cross-tenant tests passing
- Python 3.12 LTS with security patches until October 2028

**Negative:**
- Python 3.14 dev workaround needed: asyncmy C extension fails to build, replaced with aiomysql (pure Python)
- Slightly slower than Go/C# (~20% higher latency) — acceptable for our 50-200 agent load
- GIL limits CPU-bound workloads (mitigated by asyncio for I/O-bound alert ingestion)
- passlib bcrypt backend broken on Python 3.14 — using bcrypt library directly

## Implementation Notes

- Production Docker uses Python 3.12-slim (asyncmy + passlib work normally)
- Dev machine Python 3.14 uses aiomysql + bcrypt direct as workarounds
- requirements.txt includes both aiomysql and asyncmy for dual compatibility
- Dockerfile uses multi-stage build with non-root user (uid 1000)
