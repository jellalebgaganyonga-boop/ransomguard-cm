# RansomGuard-CM GRID Central Server

## Overview

GRID is the central management server for RansomGuard-CM agents deployed across Cameroon hospitals. It receives security alerts via mTLS-authenticated endpoints, distributes Ed25519-signed threat intelligence packages, provides a JWT-authenticated REST API for the dashboard, and enforces multi-tenant isolation so each hospital's data is invisible to others. Built with FastAPI Python 3.12, MySQL 8.4, and nginx with TLS 1.3.

## Quick Start (Development)

```powershell
# 1. Generate PKI certificates
wsl bash deployment/grid/pki/init-pki.sh "Test Hospital" "CM"

# 2. Create environment file
cp deployment/grid/.env.example deployment/grid/.env
# Edit deployment/grid/.env with your MySQL credentials and JWT secret

# 3. Start the stack
docker compose -f deployment/grid/docker-compose.yml --env-file deployment/grid/.env up -d

# 4. Verify all containers are healthy
docker compose -f deployment/grid/docker-compose.yml --env-file deployment/grid/.env ps

# 5. Test health endpoint
docker exec grid-nginx wget -qO- http://grid-api:8000/api/v1/health
# Expected: {"status":"ok","version":"0.8.0"}
```

For local Python development without Docker:

```powershell
cd grid
python -m venv .venv
.venv\Scripts\activate
pip install -e ".[dev]"
cp .env.example .env  # Edit with local MySQL credentials
alembic upgrade head
uvicorn ransomguard_grid.main:app --reload
```

## Architecture

```
   ┌──────┐   mTLS    ┌─────────┐         ┌──────────┐
   │ Agent├──443──────┤  nginx  ├─8000────┤ grid-api │
   └──────┘           │ (TLS1.3)│         │ (FastAPI)│
                      └────┬────┘         └─────┬────┘
   ┌──────────┐  JWT       │                    │
   │Dashboard ├──8443──────┘                    │
   └──────────┘                   ┌─────────────┼──────────────┐
                                  │                            │
                            ┌─────┴─────┐               ┌─────┴─────┐
                            │  MySQL 8  │               │  Redis 7  │
                            │(22 tables)│               │(rate limit)│
                            └───────────┘               └───────────┘
```

- **nginx**: TLS 1.3 termination, mTLS verification for agents, rate limiting
- **grid-api**: FastAPI application with async SQLAlchemy, Ed25519 verification
- **MySQL 8.4**: 21 application tables + 1 alembic_version, InnoDB with utf8mb4
- **Redis 7**: Rate limiting counters and JWT blacklist (in-memory fallback available)

## Tech Stack

| Component | Technology | Version |
|-----------|-----------|---------|
| API Framework | FastAPI | 0.115+ |
| ORM | SQLAlchemy (async) | 2.0+ |
| Migrations | Alembic | 1.14+ |
| Database | MySQL | 8.4 |
| MySQL Driver | aiomysql / asyncmy | dual compat |
| Cache/Rate Limit | Redis | 7 |
| Logging | structlog | 24+ |
| JWT | python-jose | 3.3+ |
| Ed25519 Signing | PyNaCl + cryptography | 1.5+ / 44+ |
| Reverse Proxy | nginx | 1.27 |
| Runtime | Python | 3.12 (Docker) |

## Multi-Tenant Isolation

19 of 21 database tables have a `tenant_id` column (FK to `tenants.id`). The `BaseRepository[T]` generic class enforces tenant filtering on every query — `get_by_id()`, `list_paginated()`, and `add()` all require and validate `tenant_id`. Cross-tenant access returns HTTP 404 (not 403) to prevent tenant enumeration. Verified by 10 explicit cross-tenant isolation tests in `tests/api/test_tenant_isolation.py`.

## Security Model

| Layer | Mechanism | Purpose |
|-------|-----------|---------|
| Agent auth | mTLS (X.509 client certs) | Identity via cert serial number |
| Dashboard auth | JWT (OAuth2 password flow) | Access (60min) + Refresh (7d) tokens |
| Audit integrity | Ed25519 signatures | Tamper detection on audit log chain |
| Transport | TLS 1.3 only | AEAD ciphers, no CBC/RC4 |
| Rate limiting | nginx zones + in-memory | Per-agent and per-endpoint limits |
| RBAC | 3 roles | tenant_admin, security_analyst, read_only_auditor |
| Privacy | No patient data stored | Only metadata: hashes, IPs, severities |

## Project Structure

```
grid/
├── src/ransomguard_grid/
│   ├── api/v1/              # FastAPI routes + schemas + dependencies
│   │   ├── routes/          # enrollment, alerts, audit_log, heartbeat, threat_intel, auth, dashboard
│   │   ├── schemas/         # Pydantic request/response models
│   │   └── dependencies/    # mTLS auth, JWT auth, rate limiting
│   ├── core/                # config, security, jwt_service, ed25519, logging, rate_limit
│   ├── db/                  # SQLAlchemy models + repositories + session
│   │   ├── models/          # 21 models in 5 groups (tenant_user, agent, alerts, threat_intel, operations)
│   │   └── repositories/    # BaseRepository + 9 concrete repositories
│   ├── services/            # enrollment OTP, signing keys, threat intel sources + aggregator + builder
│   └── workers/             # threat_intel_updater_worker
├── tests/                   # 94 pytest tests
│   ├── api/                 # endpoint tests (enrollment, alerts, auth, tenant isolation, RBAC)
│   ├── db/                  # model + repository tests
│   └── services/            # threat intel source + aggregator + builder tests
├── alembic/                 # 2 migrations (initial schema + ed25519 column)
├── pyproject.toml
├── requirements.txt
└── .env.example
```

## Running Tests

```bash
cd grid
.venv/Scripts/activate  # or source .venv/bin/activate on Linux
pytest --cov=src --cov-report=term -v
```

## Known Issues / Sprint 6.5 Debt

- **asyncmy/aiomysql dual install**: Python 3.14 dev machine needs aiomysql (pure Python); Docker 3.12 can use either
- **Azure IP ranges source**: Not implemented (URL changes weekly, requires manual download)
- **docker-compose version key**: `version: "3.9"` triggers deprecation warning (cosmetic)
- **Worker lifespan tests**: Not added as separate test file (worker tested via service tests)
- **Docker E2E tests**: Manual validation only (docker not available in pytest CI)
- **Redis integration**: Rate limiting uses in-memory fallback; Redis wiring deferred to Sprint 8
