# ADR-023: GRID Server Tech Stack — FastAPI Python

**Status:** Accepted — amended 2026-10-01 (Amendment 1: single MySQL driver) and 2026-10-08 (Amendment 2: PyJWT replaces python-jose)
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

## Amendment 1 — 2026-10-01: a single MySQL driver, aiomysql

**Supersedes:** the "aiomysql+asyncmy" driver choice in *Decision*, and the
first three bullets of *Implementation Notes* (asyncmy in production Docker,
aiomysql as a dev-only workaround, both drivers kept "for dual compatibility").

### What actually happened

This ADR named asyncmy as the production driver and aiomysql as a workaround
for the Python 3.14 development machine, where asyncmy's C extension does not
build. The record shows otherwise: the production `docker-compose.yml` has
selected `mysql+aiomysql://` since its first commit (e503f1f, 2026-05-29). The
only place `mysql+asyncmy://` ever appeared was `grid/.env.example`. The
development workaround was, in fact, the production driver from day one — it
runs the Docker stack and carried the Sprint 8 LOT 1 live alert chain.

asyncmy was therefore installed everywhere and exercised nowhere.

### Decision

aiomysql is the one and only MySQL driver. asyncmy is removed from
`requirements.txt` and from the `[mysql]` extra in `pyproject.toml`;
`.env.example` now shows the `mysql+aiomysql://` URL the stack really uses.

### Why only one

- **One tested path.** Two async drivers differ in type conversion, error
  classes and connection handling. Keeping both meant shipping a code path no
  test, no environment and no customer ever ran.
- **No configuration trap.** The template advertised asyncmy while the stack
  ran aiomysql; `pip install ".[mysql]"` installed a driver the production URL
  does not load. One driver removes that class of mismatch.
- **Smaller dependency surface.** One less package (with a native extension)
  to build, audit and patch.
- **Same driver on every machine.** aiomysql is pure Python, so the Python
  3.14 development machine and the Python 3.12 Docker image run identical
  database code.

### Consequences

- asyncmy's speed advantage (Cython) is given up. The load to size against is
  the central SaaS GRID, not a single hospital: about 4,000 agents, i.e. about
  67 heartbeats per second at the production interval of 60 s
  (`appsettings.Production.json`). This figure is an estimate, **to be verified
  by the server load test**. At that load the database driver is not expected
  to be the bottleneck; if the load test shows otherwise, a driver change is a
  new ADR, not a second driver kept "just in case".
- Any environment with an `mysql+asyncmy://` URL must switch to
  `mysql+aiomysql://`. No tracked deployment file uses asyncmy.

## Amendment 2 — 2026-10-08: PyJWT replaces python-jose

**Supersedes:** "python-jose (JWT)" in *Decision*.

python-jose reached its second advisory without a fixed release:
PYSEC-2026-1325 (its `ecdsa` dependency) and CVE-2026-85394 (algorithm
confusion, a DER public key accepted as an HMAC secret). Neither was reachable
in the GRID, but it is the authentication dependency of the SaaS, and an
unmaintained one cannot stay there.

- The GRID uses **PyJWT**, HS256 only. Every `decode` passes
  `algorithms=["HS256"]` explicitly: the algorithm is never read from the
  token, nor from configuration (`jwt_algorithm` only accepts `"HS256"`).
- python-jose and ecdsa are no longer installed; `pip-audit --strict` runs in
  CI with no ignored advisory.
- Tokens already issued stay valid: same secret, same algorithm, same claims
  (`grid/tests/test_jwt_security.py` decodes a token issued by python-jose).
