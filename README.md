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

The platform protects hospital workstations against ransomware attacks through a multi-layered defense system combining behavioral machine learning, deception technology (canary files), hardware-based backup isolation (IRONCLAD), and centralized management.

**Innovation differentiators:**
- 🧠 **Per-hospital behavioral learning** — each hospital has its own normal-behavior profile
- 🛡️ **IRONCLAD physical air-gap** — automated USB relay isolation of backup disk
- 🔄 **Collective threat intelligence** — attacks detected at one hospital immunize all others
- 🌐 **Offline-first architecture** — full protection without permanent internet
- 🇨🇲 **Native French + Cameroon legal compliance** — Loi 2024/017 + ANTIC ready

---

## 🏗️ Architecture

\\\
┌─────────────────────────────────────────────────────────────────┐
│              CLOUD RANSOMGUARD (AWS af-south-1)                 │
│  • Threat intelligence aggregation                              │
│  • Signed update distribution (Ed25519)                         │
│  • License validation                                           │
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

Full architecture documentation: [docs/architecture/](docs/architecture/)

---

## 🛠️ Tech Stack

| Component | Technology | Justification |
|---|---|---|
| **Agent (Windows endpoint)** | C# .NET 8 LTS | Native Windows APIs (ETW, WMI), Microsoft code signing |
| **Management Server** | Python 3.12 + FastAPI | Productivity, async/await, ML ecosystem |
| **Cloud Backend** | Python 3.12 + FastAPI | Stack consistency |
| **Dashboard** | React 18 + TypeScript + Tailwind | Industry standard, type-safe |
| **Database** | MySQL 8 | Robust, ACID, widely supported |
| **Cache / Queue** | Redis 7 | High performance |
| **Object Storage** | MinIO | S3-compatible, on-premise |
| **ML** | scikit-learn (Isolation Forest + DBSCAN) | Compatible with Windows 7+ legacy |
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
