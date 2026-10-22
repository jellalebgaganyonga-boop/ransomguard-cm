# RansomGuard-CM

> **Anti-Ransomware Endpoint Protection Platform for Healthcare Facilities in Francophone Sub-Saharan Africa**

[![License: Proprietary](https://img.shields.io/badge/License-Proprietary-red.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-8.0_LTS-512BD4?logo=dotnet)](https://dotnet.microsoft.com/)
[![Python](https://img.shields.io/badge/Python-3.12-3776AB?logo=python&logoColor=white)](https://www.python.org/)
[![React](https://img.shields.io/badge/React-18-61DAFB?logo=react&logoColor=white)](https://react.dev/)
[![MySQL](https://img.shields.io/badge/MySQL-8.0-4479A1?logo=mysql&logoColor=white)](https://www.mysql.com/)
[![Status](https://img.shields.io/badge/Status-Active_Development-yellow)]()

---

## 📋 Overview

RansomGuard-CM is the first cybersecurity solution designed natively for healthcare facilities in Francophone Sub-Saharan Africa. It addresses a documented gap: no existing solution combines offline-first operation, native French language, Windows legacy support, affordable pricing, and compliance with Cameroon's Law N°2024/017.

> **What this README promises and what exists (2026-10-22).** The agent (SENTINEL canaries, ENTROPY, GENEALOGY, USB GUARD, EXFIL WATCH, the suspension of a process that wrote a canary), the GRID (FastAPI, MySQL, Redis) and the console (React) exist and are tested. The lines marked below are **not implemented** or **not verified**; each carries its debt identifier, listed in `docs/JOURNAL_DES_ACTIONS.md` § 3.4. Nothing here is deleted: the promise stays visible next to its status.

The platform protects hospital workstations against ransomware attacks through a multi-layered defense system combining behavioral machine learning (**Not implemented as of 2026-10-22** — no model, no training data, no ML dependency in the GRID or the agent; debt PROD-003), deception technology (canary files), hardware-based backup isolation (IRONCLAD — **Not implemented as of 2026-10-22** as a backup air-gap: IRONCLAD exists only as the cut of a USB port through a mock Arduino server, there is no backup disk and no backup; debt PROD-001), and centralized management.

**Innovation differentiators:**
- 🧠 **Per-hospital behavioral learning** — each hospital has its own normal-behavior profile (**Not implemented as of 2026-10-22** — the only baselines are ENTROPY's per-file entropy and EXFIL WATCH's 7-day volume baseline on one workstation; debt PROD-003)
- 🛡️ **IRONCLAD physical air-gap** — automated USB relay isolation of backup disk
- 🔄 **Collective threat intelligence** — attacks detected at one hospital immunize all others (**Not implemented as of 2026-10-22** — the GRID pulls public feeds (ThreatFox, Tor exit nodes) and signs them for its own agents; nothing goes from one hospital to another, `cloud/` holds a README only; debt PROD-004)
- 🌐 **Offline-first architecture** — full protection without permanent internet (**Partly held, not verified as of 2026-10-22** — the GRID runs on the LAN and the agent keeps detecting and queues its alerts while the GRID is unreachable (sequence 4.8 of the journal); the threat-intelligence feeds need Internet, and no measurement of "full protection" without Internet exists; debt PROD-007)
- 🇨🇲 **Native French + Cameroon legal compliance** — Loi 2024/017 + ANTIC ready

---

## 🏗️ Architecture

\\\
┌─────────────────────────────────────────────────────────────────┐
│              CLOUD RANSOMGUARD (AWS af-south-1)                 │
│  • Threat intelligence aggregation      (not implemented)       │
│  • Signed update distribution (Ed25519) (not implemented)       │
│  • License validation                   (not implemented)       │
└──────────────────────────┬──────────────────────────────────────┘
                           │ HTTPS + Ed25519 signed
┌──────────────────────────▼──────────────────────────────────────┐
│         HOSPITAL — Management Server (Ubuntu 22.04 LTS)         │
│  • FastAPI backend  • MySQL 8  • Redis  • MinIO                 │
│  • IRONCLAD hardware controller  • Email gateway                │
└──────────────────────────┬──────────────────────────────────────┘
                           │ mTLS 1.3
        ┌──────────────────┼──────────────────┐
        │                  │                  │
   ┌────▼────┐        ┌────▼────┐       ┌────▼────┐
   │ AGENT 1 │        │ AGENT 2 │  ...  │AGENT N  │
   │Win 10/11│        │Win 10/11│       │Win 10/11│
   └─────────┘        └─────────┘       └─────────┘
\\\

The cloud tier of this diagram does not exist as of 2026-10-22: `cloud/` holds a README, no service (debts PROD-004, PROD-005). The agent-to-GRID link is HTTPS with the GRID's own CA and an `agent_id` mode, not mTLS (see `docs/console/INVENTAIRE_CONSOLE.md`, § 2, "Authentification des agents").

Full architecture documentation: [docs/architecture/](docs/architecture/)

---

## 🛠️ Tech Stack

| Component | Technology | Justification |
|---|---|---|
| **Agent (Windows endpoint)** | C# .NET 8 LTS | Native Windows APIs (ETW, WMI), Microsoft code signing |
| **Management Server** | Python 3.12 + FastAPI | Productivity, async/await, ML ecosystem |
| **Cloud Backend** | Python 3.12 + FastAPI | Stack consistency (**Not implemented as of 2026-10-22**; PROD-004) |
| **Dashboard** | React 18 + TypeScript + Tailwind | Industry standard, type-safe |
| **Database** | MySQL 8 | Robust, ACID, widely supported |
| **Cache / Queue** | Redis 7 | High performance |
| **Object Storage** | MinIO | S3-compatible, on-premise (**Not implemented as of 2026-10-22** — not deployed, not a dependency; it was meant for the backups, PROD-001) |
| **ML** | scikit-learn (Isolation Forest + DBSCAN) | Compatible with Windows 7+ legacy (**Not implemented as of 2026-10-22** — not a dependency, no model; PROD-003. Windows 7 is not supported either: the agent runs on .NET 8) |
| **Crypto** | libsodium + Ed25519 | Modern, audited cryptography |
| **Containerization** | Docker + Docker Compose | Reproducible deployment |

---

## 📂 Repository Structure

\\\
RansomGuard-CM/
├── agent/              # Windows endpoint agent (C# .NET 8)
├── server/             # Management server (Python FastAPI)
├── cloud/              # Cloud backend (Python FastAPI)
├── dashboard/          # Web dashboard (React + TypeScript)
├── shared/             # Shared protocols and schemas (OpenAPI)
├── docs/               # Technical documentation
│   ├── architecture/   # C4 diagrams, design decisions
│   ├── api/            # OpenAPI specifications
│   ├── deployment/     # Deployment runbooks
│   └── user-guide/     # End-user documentation (FR/EN)
├── scripts/            # Development and deployment scripts
└── tests/              # Integration, E2E, and security tests
\\\

---

## 🚀 Quick Start

> ⚠️ Project is in active development — not yet ready for production deployment.

### Prerequisites

- Windows 10/11 (development machine)
- .NET 8 SDK
- Python 3.12
- Node.js 20+
- Docker Desktop
- Git

### Local Development Setup

\\\powershell
# Clone repository
git clone https://github.com/jellalebga/ransomguard-cm.git
cd ransomguard-cm

# Setup will be documented in CONTRIBUTING.md once the project structure stabilizes
\\\

---

## 📚 Documentation

| Document | Description |
|---|---|
| [Design Document](docs/architecture/design-doc.md) | Google-style design document |
| [C4 Diagrams](docs/architecture/) | Context, Container, Component views |
| [Database Schema](docs/architecture/database-schema.md) | ER diagram and DDL |
| [API Specification](docs/api/openapi.yaml) | OpenAPI 3.0 specification |
| [SLO/SLA](docs/architecture/slo-sla.md) | Service level objectives |
| [Incident Runbook](docs/deployment/runbook.md) | Production incident procedures |

---

## 🔒 Security Standards

This project implements the following industry standards:

- **OWASP API Security Top 10**
- **MITRE ATT&CK Framework**
- **NIST SP 800-53** (Security Controls)
- **NIST SP 800-61** (Incident Response)
- **ISO 27001 / ISO 27035**
- **RFC 8446** (TLS 1.3)
- **RFC 6749** (OAuth 2.0)
- **Loi N°2024/017 du Cameroun** (Data Protection)
- **Loi N°2010/012 du Cameroun** (Cybersecurity)

---

## 📊 Project Status

\\\
✅ Phase 1 — Discovery & Research
✅ Phase 2 — Strategy & Vision
✅ Phase 3 — Product Design
✅ Phase 4 — Technical Architecture
🟡 Phase 5 — Proof of Concept (POC)         ← Current
⬜ Phase 6 — Minimum Viable Product (MVP)
⬜ Phase 7 — Pilot Deployment
⬜ Phase 8 — Production
\\\

---

## 👤 Author

**Jella Lebga** (Kuate Abdel Yaniv)
B.Sc. Information Systems and Networking / Cybersecurity Engineering
**ICT University Yaoundé, Cameroon** — Faculty of ICT
📧 jellalebga.ganyonga@ictuniversity.edu.cm

---

## 📄 License

**Proprietary — All Rights Reserved**

Copyright (c) 2026 Jella Lebga. Unauthorized copying, modification, distribution, or use of this software is strictly prohibited.

See [LICENSE](LICENSE) for full terms.

---

## 🙏 Acknowledgments

This project is the senior year academic project at ICT University Yaoundé, conducted as part of the B.Sc. requirements in Information Systems and Networking / Cybersecurity Engineering.

Special recognition to the open-source community whose tools and standards (OWASP, MITRE, NIST, Wazuh, scikit-learn, FastAPI) make projects like this possible.
