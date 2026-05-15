# RansomGuard-CM — Software Design Document

**Version:** 1.0
**Author:** Jella Lebga (Kuate Abdel Yaniv)
**Affiliation:** ICT University Yaoundé — Faculty of ICT
**Date:** May 2026
**Methodology:** Google-Style Design Doc
**Status:** Draft for Review

---

## 1. TITLE & AUTHORS

**Project:** RansomGuard-CM — Anti-Ransomware Endpoint Protection Solution with Centralized Management for Healthcare Facilities in Francophone Sub-Saharan Africa

**Primary Author:** Jella Lebga (Kuate Abdel Yaniv)
**Affiliation:** ICT University Yaoundé — Faculty of ICT
**Program:** B.Sc. Information Systems and Networking / Cybersecurity Engineering
**Academic Year:** 2025–2026
**Document Created:** May 2026
**Document Status:** Draft v1.0

---

## 2. OVERVIEW

RansomGuard-CM is an anti-ransomware endpoint protection platform specifically designed for healthcare facilities in Francophone Africa. The system combines a lightweight endpoint agent (compatible with Windows 7 SP1+, with primary support for Windows 10/11 Pro/Enterprise), a centrally-managed server deployed locally within the hospital, and a cloud infrastructure for threat intelligence and update distribution.

The architecture is intentionally **offline-first**: protection operates autonomously even without a permanent internet connection — a critical constraint in the African hospital context. The system is natively bilingual (French/English), compliant with Cameroon's Law N°2024/017, and commercialized under an accessible annual license model with Mobile Money payment.

The product fills a documented gap: no cybersecurity solution worldwide has been designed natively for the specific constraints of Francophone African hospitals — French interface, offline operation, locally-adapted pricing, legacy Windows 7 support coupled with medical equipment.

---

## 3. CONTEXT & BACKGROUND

### 3.1 Global Context

The healthcare sector is the most targeted by ransomware attacks worldwide. Sophos reports that 67% of global hospitals were affected in 2024. The BlackSuit attack against South Africa's National Health Laboratory Service in June 2024 paralyzed 265 laboratories and blocked 6.3 million blood tests, directly impacting the fight against HIV, tuberculosis, and mpox.

WHO has elevated this threat to international priority status. In November 2024, Director General Dr Tedros Adhanom Ghebreyesus briefed the UN Security Council on this issue, asking member states to invest in early detection technologies.

### 3.2 African and Cameroonian Context

Sub-Saharan Africa faces 3,575 cyberattacks per week on its healthcare sector in 2025 — a 38% increase compared to the previous year according to Microsoft EMEA. Cameroon is among the top 5 African countries most impacted by ransomware according to the 2024 INTERPOL report, with 1,462 malware detected. ANTIC identified 8,502 vulnerabilities in Cameroonian administrations and businesses since January 2024.

Specifically, Cameroonian hospitals operate in a structurally high-risk environment:
- Windows 7 endpoints still in production because coupled with non-replaceable medical equipment
- Passwords shared between care staff
- USB keys circulating freely between personal and hospital endpoints
- Backups stored on permanently-connected disks (therefore encryptable by ransomware)
- Total absence of cybersecurity experts within facilities
- Frequent internet and electrical outages

### 3.3 Legal Framework Cameroon

Two laws directly govern hospital cybersecurity:
- **Law N°2010/012** of December 21, 2010 on cybersecurity and cybercrime — grants ANTIC authority to audit organization systems
- **Law N°2024/017** of December 23, 2024 on personal data protection — qualifies health data as "sensitive", imposes explicit consent, and provides fines up to 1 billion FCFA and prison sentences for responsible parties. Effective entry into force: June 23, 2026.

### 3.4 Analysis of Existing Solutions

Six major solutions have been analyzed in depth. All present structural limitations blocking for the African context:

**Wazuh (open source)** — free and complete, but requires an expert Linux administrator for configuration and maintenance, English-only interface, recent critical CVE-2025-24016 with score 9.9. The hidden cost in human expertise is prohibitive for an African hospital without a specialized IT team.

**CrowdStrike Falcon** — global reference, powerful AI, but enterprise pricing ($15,000 to $35,000/year minimum), cloud-first architecture incompatible with internet outages, designed for dedicated SOC nonexistent in African hospital context. The July 19, 2024 incident (8.5 million Windows machines crashed by a defective update) also demonstrated the systemic risk of total dependency on a single publisher's cloud.

**SentinelOne** — advanced rollback capabilities via Volume Shadow Copies, but these copies are precisely the first target of modern ransomware which delete them before attacking. Enterprise pricing similar to CrowdStrike, cloud dependency, English-only.

**Halcyon** — the most advanced specialized anti-ransomware solution on the market, captures encryption keys during the attack, but entirely cloud-dependent, enterprise pricing, designed for American hospitals under HIPAA, zero presence in Africa.

**ManageEngine Ransomware Protection Plus** — supports air-gapped environments and behavioral analysis, but commercial enterprise license, designed for structured IT teams, no French support, no native backup integration, no incident report generation.

**No More Ransom Project** — free INTERPOL/Europol initiative providing decryption keys for known ransomware, but intervenes only after compromise, coverage limited to ransomware families already analyzed, not a prevention or detection tool.

### 3.5 Identified Gap

The gap is not technical. Detection algorithms (entropy, behavioral, canary) have been documented in academic literature for ten years. The gap is in **packaging and contextual localization**. No one has assembled these proven techniques in a product that:
- Operates without permanent internet
- Speaks French natively
- Supports legacy Windows 7
- Costs less than $2000/year
- Generates reports understandable by a non-technical director
- Is specifically compliant with Cameroonian legislation
- Allows Mobile Money payment

This is exactly the space RansomGuard-CM occupies.

---

## 4. GOALS & NON-GOALS

### 4.1 Goals (Objectives)

**G1** — **Detect and neutralize at minimum 87.5% of documented 2025 ransomware attack vectors** without human intervention, within less than 5 seconds from attack initiation.

**G2** — **Guarantee operational continuity of the hospital**: no software functionality should ever paralyze a critical medical workstation (HMS, laboratory equipment, imaging).

**G3** — **Operate offline-first**: full protection must remain operational even after 30 days without internet connection.

**G4** — **Allow deployment by a generalist IT technician** without prior cybersecurity training, in less than 2 hours for a 50-endpoint hospital.

**G5** — **Be compliant with Cameroonian laws** N°2024/017 and N°2010/012 from version 1.0.

**G6** — **Maintain a false positive rate below 1 per endpoint per month** in production conditions.

**G7** — **Generate automatic bilingual incident reports** readable by a medical director without IT training in less than 30 seconds.

**G8** — **Support Windows 7 SP1 and higher** to cover the legacy fleet of African hospitals, with primary focus on Windows 10/11 Pro/Enterprise.

### 4.2 Non-Goals (What the product does NOT do)

**NG1** — The product is NOT a generic antivirus. It does not replace Kaspersky or Bitdefender — it complements them. It does not detect generic viruses, worms, or trojans.

**NG2** — The product does NOT protect against kernel-level BYOVD attacks using Microsoft-signed vulnerable drivers. These attacks require Ring 0 access that even the best global solutions struggle to block.

**NG3** — The product does NOT protect proprietary medical equipment (ultrasounds, laboratory analyzers, MRIs) whose firmware cannot accommodate an agent. It secures only the Windows endpoints that interact with this equipment.

**NG4** — The product does NOT provide 24/7 managed SOC in version 1.0. No human team that monitors remotely. Only automatic detection and alerts.

**NG5** — The product does NOT cover insider threats from an employee with full legitimate access (malicious system administrator). This is out of scope for an anti-ransomware product.

**NG6** — The product does NOT do anti-phishing email protection in version 1.0. This is a planned post-MVP extension.

**NG7** — The product does NOT manage identities and access (IAM) beyond dashboard authentication. No Active Directory replacement.

**NG8** — The product does NOT support macOS or Linux on the endpoint side in version 1.0. Windows only.

### 4.3 Success Metrics

**M1 — Adoption**: Minimum 1 Cameroonian pilot hospital using RansomGuard in production by defense date.

**M2 — Detection**: Detection rate ≥ 95% on a panel of 20 ransomware samples tested in sandbox environment.

**M3 — Performance**: Agent footprint ≤ 200 MB RAM and ≤ 5% CPU in normal monitoring.

**M4 — Latency**: Detection-isolation time ≤ 5 seconds for standard ransomware.

**M5 — False positives**: 0 paralysis of critical medical process in pilot environment.

**M6 — Availability**: 99% agent uptime (accepting planned Windows reboots).

---

## 5. PROPOSED SOLUTION

### 5.1 Global Architecture

RansomGuard-CM adopts a **hybrid client-server architecture with distributed intelligence**, structured in three independent but coordinated layers:

**Layer 1 — RansomGuard Cloud** (hosted by publisher, AWS af-south-1 Cape Town)
This layer aggregates global threat intelligence sources (VirusTotal, AlienVault OTX, MITRE ATT&CK, AbuseCH, CISA), trains coordinated ML models, distributes Ed25519-signed updates to hospital servers, and hosts the license validation service.

**Layer 2 — Local Management Server** (deployed in the hospital, Ubuntu 22.04 LTS)
Centralizes supervision of all hospital agents, hosts the web dashboard, manages centralized backups (including the IRONCLAD hardware module), sends email alerts, generates bilingual PDF reports, and synchronizes signatures with the cloud when internet is available.

**Layer 3 — Windows Agents** (deployed on every endpoint)
Execute behavioral detection locally, act in complete autonomy if necessary (network isolation, process termination, emergency backup), communicate alerts to the server via mTLS, and continue protecting even if the server is unreachable.

### 5.2 Fundamental Principle — Defense in Depth via the 5 Rings

Protection is organized in 5 concentric rings. A ransomware must traverse all 5 to cause real damage. No known ransomware traverses all 5.

**Ring 1 — Perimeter**: USB GUARD module intercepts any USB key insertion before Windows mounting. Automatic scan, blocking of executables and scripts, timestamped logging.

**Ring 2 — Behavior**: Combination of canary file monitoring (SENTINEL — realistic medical decoys, GRID — multi-positional distribution), real-time Shannon entropy analysis, mass modification detection, process tree monitoring (GENEALOGY — Word should never spawn PowerShell), and behavioral learning by Isolation Forest over 30 days.

**Ring 3 — Deception**: Fake medically-coherent patient files strategically distributed. A ransomware that avoids them misses real files, a ransomware that touches them reveals itself instantly.

**Ring 4 — Data**: Internal Zero Trust on patient files. No non-whitelisted process can access protected files, even with legitimate Windows rights.

**Ring 5 — Recovery**: Versioned automatic backup with IRONCLAD — automated physical air-gap via GPIO Arduino-controlled USB relay. The backup disk is electrically disconnected permanently, connects only during backup window (5 minutes), then disconnects. No ransomware can reach a disk absent from the bus.

### 5.3 Functional Modules

Eight modules constitute the product. Each covers a distinct responsibility:

**Module 1 — Network Deployment**: Centralized installation from server via WMI/PSExec, license key activation verified online, degraded mode if license server unreachable, blocking after 30-day grace period post-expiration.

**Module 2 — Supervision Dashboard**: Bilingual React interface accessible on LAN, real-time view of endpoints (green/orange/red), alert history, whitelist management, license status, strict RBAC (Admin/Technician/Director roles), OAuth2 authentication with MFA TOTP.

**Module 3 — Detection**: Combination of the 7 Rings 1-3 mechanisms (USB GUARD, SENTINEL canaries, GRID multi-position, Shannon entropy, mass detection, GENEALOGY process tree, EXFIL WATCH exfiltration detection).

**Module 4 — Backup (SNAPSHOT)**: Configurable incremental scheduled every 2 hours, 10-snapshot versioning, emergency backup on detection, IRONCLAD hardware air gap, automatic integrity verification, AES-256-GCM encryption before writing.

**Module 5 — Restoration**: Guided snapshot selection, selective or full restoration, double authentication (technician + director) required, immutable action logging.

**Module 6 — Reports**: Automatic bilingual PDF generation on incident, weekly health report, monthly ANTIC compliance report, non-technical language for director.

**Module 7 — Email Alerts**: Email with PDF report attached on attack, daily backup confirmation, license expiration alert 30/7 days before, mandatory TLS (RFC 8314), DKIM + SPF + DMARC, anonymization of sensitive data in body.

**Module 8 — Updates**: Periodic cloud pull, mandatory Ed25519 signature verification before installation, silent application, automatic rollback on failure, offline mode with retained latest signatures.

### 5.4 Technical Stack

| Component | Technology | Justification |
|---|---|---|
| Windows Agent | C# .NET 8 LTS | Native Windows APIs, solo dev productivity |
| Server Backend | Python 3.12 + FastAPI | Productivity, ML ecosystem, native async |
| Cloud Backend | Python 3.12 + FastAPI | Stack consistency, code sharing |
| Dashboard | React 18 + TypeScript + Tailwind | Modern industry standard |
| Main Database | MySQL 8 | Mastered by developer, reliable |
| Cache/Queue | Redis 7 | Industry standard |
| Object Storage | MinIO | S3-compatible, on-premise |
| ML | scikit-learn | Proven Isolation Forest + DBSCAN |
| Containerization | Docker + Compose | Reproducible deployment |
| Reverse Proxy | Nginx | Standard, ultra-stable |
| Cryptography | libsodium + Ed25519 | Modern, audited |
| Code Signing | DigiCert (production) | Microsoft Trust required |

### 5.5 Threat Model — STRIDE Synthesis

The complete STRIDE analysis identifies 30+ threats distributed over 6 components (Agent, Server, Dashboard, Cloud, Communications, Hardware). Critical mitigated threats include: agent spoofing (mandatory code signing), log tampering (append-only hash chain), data leakage (AES-256-GCM encryption), paralyzing false positive (3-converging-signals rule + medical whitelist), privilege elevation (strict principle of least privilege).

Assumed residual risks concern kernel-level BYOVD attacks, insider threats with full legitimate access, medical equipment firmware zero-days, and coordinated nation-state attacks — out of scope for a product targeting SMEs and non-strategic hospitals.

---

## 6. ALTERNATIVES CONSIDERED

### 6.1 Alternative A — Plugin for Wazuh

**Description:** Instead of building a standalone product, develop a module/plugin for Wazuh that adds missing capabilities (French, SMS, bilingual reports, IRONCLAD).

**Advantages:** Reuse of a mature detection engine, open source community, gain of several months development time.

**Rejected — Reasons:**
- Wazuh requires an expert Linux administrator to function — unresolved structural gap
- Wazuh interface remains English and complex, hostile to Cameroonian users
- Recent CVE-2025-24016 score 9.9 vulnerability reveals critical security debt
- No control over product roadmap
- Impossibility to commercially differentiate (customer can install Wazuh alone)
- The license business model becomes ambiguous on GPL codebase

### 6.2 Alternative B — Pure Cloud-First SaaS Architecture

**Description:** Build a SaaS service entirely hosted in the cloud, with lightweight agents communicating only with a cloud backend (CrowdStrike model).

**Advantages:** Attractive recurring economic model, instant update capability, optimal collective intelligence.

**Rejected — Reasons:**
- Unstable internet in Cameroonian hospitals (documented daily outages)
- Law N°2024/017 requires prior authorization from data protection authority for any cross-border transfer — authority not yet operational (presidential decree expected)
- Limited hospital bandwidth (2-10 Mbps shared) doesn't support telemetry volume
- Cloud hosting cost per hospital becomes significant at scale
- A hospital losing internet for 3 days is without protection

### 6.3 Alternative C — Open Source Distribution

**Description:** Publish RansomGuard as open source (Apache 2.0 or GPL license), monetize through paid support and managed services.

**Advantages:** Rapid adoption, community validation, ethical alignment with the non-profit hospital sector.

**Rejected — Reasons:**
- Fragile economic model for a solo student entrepreneur
- Open source attracts competitive forks without added value
- The African hospital sector doesn't have the technical maturity to exploit open source alone
- Difficulty enforcing code signing and canary deployment without editorial control
- Not the right model for a defense product with post-studies monetization

### 6.4 Alternative D — Peer-to-Peer Architecture

**Description:** P2P architecture where each agent communicates directly with its peers on the LAN, without centralized server.

**Advantages:** No SPOF server, native resilience, initial deployment simplicity.

**Rejected — Reasons:**
- Impossible to centralize logs for ANTIC compliance
- No coherent dashboard for IT technician
- License management nightmarish
- IRONCLAD hardware impossible to distribute
- No P2P model exists successfully in enterprise cybersecurity

### 6.5 Synthesis

The hybrid client-server architecture with distributed intelligence was retained because it combines the structural advantages of centralization (supervision, compliance, IRONCLAD) with the autonomy of decentralization (agent resilience even if server down). It's the model proven by Kaspersky, ESET, and Bitdefender for 25 years.

---

## 7. RISKS & MITIGATIONS

### 7.1 Technical Risks

**R1 — Agent footprint too heavy on Windows 7 legacy**
*Impact:* Critical — incompatibility with target fleet.
*Mitigation:* Continuous benchmarking during development. If 200 MB RAM threshold exceeded, fallback on lightweight agent without local ML (signatures only).

**R2 — False positive paralyzing critical medical process**
*Impact:* Catastrophic — vital danger for patient.
*Mitigation:* Hard-coded medical whitelist by default (HMS, known equipment). 3-converging-signals rule before isolation. "Medical emergency" mode: alert without automatic isolation.

**R3 — IRONCLAD hardware module not functional in time**
*Impact:* Moderate — loss of a differentiating innovation.
*Mitigation:* Plan B in strict logical isolation (logically mounted/unmounted disk with audit). Product remains functional without IRONCLAD.

**R4 — Compromise of cloud signing infrastructure**
*Impact:* Catastrophic — SolarWinds scenario.
*Mitigation:* Mandatory dual signature, canary deployment 1%→10%→50%→100% with automatic monitoring, publishable reproducible build.

### 7.2 Legal Risks

**R5 — Non-compliance with Law N°2024/017 from June 2026**
*Impact:* Critical — legally unsellable.
*Mitigation:* Data-stays-on-prem architecture by default. No patient data transfer outside Cameroon without explicit consent. Automatic DPIA generation.

**R6 — Civil prosecution if RansomGuard misses an attack**
*Impact:* Moderate — reputational and financial risk.
*Mitigation:* Clear contractual liability limitation clauses. Professional insurance considered for commercial version.

### 7.3 Business Risks

**R7 — Slow adoption due to lack of notoriety**
*Impact:* Moderate — technically successful but commercially dead product.
*Mitigation:* 30-day free trial period. First pilot testimonial. Planned ANTIC partnership.

**R8 — Price war initiated by a global publisher**
*Impact:* High — margin erosion.
*Mitigation:* Structural differentiation (offline, French, local compliance) impossible to catch up quickly by a global player. Lock-in through context-specific features.

### 7.4 Project Risks (Solo Developer)

**R9 — Burnout on 16-week sprint at 16h/day**
*Impact:* Critical — project halted.
*Mitigation:* Non-negotiable Sunday-off rule. Daily standup with self to track weak signals.

**R10 — Underestimation of Windows kernel-level complexity**
*Impact:* High — delay of several weeks.
*Mitigation:* 1-week buffer in Sprint 1. Plan B on kernel features if blocking.

**R11 — Absence of pilot hospital for validation**
*Impact:* High — less convincing defense.
*Mitigation:* Identification of 3 candidates from Week 4. Approach private clinics (shorter decision cycle than public).

---

## 8. ROLLOUT & SUCCESS METRICS

### 8.1 Development Plan (16 Weeks)

**Phase 1 — Conception (Weeks 1-2):** Threat Model STRIDE, ADRs (15 decisions), Design Doc (this document), C4 diagrams, database schema.

**Phase 2 — Setup (Week 3):** Git repository, Docker environment, project structure, first end-to-end health check.

**Phase 3 — Agent Sprint (Weeks 4-7):** Canary file detection, USB GUARD, mTLS communication, response engine, watchdog.

**Phase 4 — Server Sprint (Weeks 8-10):** FastAPI API, business services, MySQL persistence, backup orchestration.

**Phase 5 — Dashboard Sprint (Week 11):** React, main views, real-time WebSocket alerts.

**Phase 6 — AI Sprint (Week 12):** Local Isolation Forest, threat intel integration, signed update system.

**Phase 7 — Hardening Sprint (Week 13):** Complete CIA implementation, IRONCLAD hardware, PDF reports.

**Phase 8 — Test Sprint (Week 14):** 70% coverage, real ransomware tests, performance tests, bug fixing.

**Phase 9 — Pilot + Documentation (Week 15):** Pilot hospital deployment, thesis chapters 1-3 writing.

**Phase 10 — Defense (Week 16):** Complete thesis, plagiarism check, slides, rehearsals.

### 8.2 Critical Milestones

| Milestone | Success Criterion |
|---|---|
| End Week 2 | Complete Design Doc validated |
| End Week 7 | Agent detects test attack on Windows 10 |
| End Week 10 | Server end-to-end functional |
| End Week 12 | Complete system in demo |
| End Week 14 | 0 critical bug, green tests |
| End Week 15 | 1+ pilot hospital validated |
| End Week 16 | Thesis 20k+ words, plagiarism <12% |

### 8.3 Quantitative Success Metrics

| Metric | Minimum Target | Excellence Target |
|---|---|---|
| Ransomware detection rate | 85% | 95% |
| Detection-isolation time | < 10 seconds | < 5 seconds |
| Agent RAM footprint | < 300 MB | < 200 MB |
| Agent CPU monitoring footprint | < 10% | < 5% |
| False positives / endpoint / month | < 3 | < 1 |
| Agent uptime | 95% | 99% |
| Pilot hospitals | 1 | 3+ |

### 8.4 Post-Defense Plan

**Short term (6 months after defense):** OAPI filing for intellectual property. Cameroon legal entity formation. First paying customers (3-5 hospitals).

**Medium term (1-2 years):** Expansion to 20+ Cameroonian hospitals. Formal partnership with ANTIC. First seed fundraising or bootstrap.

**Long term (3-5 years):** Francophone Africa expansion (Côte d'Ivoire, Senegal, Gabon, DRC). 100+ customer hospitals. Anti-phishing module and medical IoT security.

---

## VALIDATION

This Design Document constitutes the strategic and technical reference for the entire development of RansomGuard-CM. Any significant deviation from the proposed design must be subject to a formal Architecture Decision Record (ADR) added to project documentation.

**Status:** Draft v1.0
**Required reviewers:** ICT University Project Supervisor
**Author approval:** Jella Lebga, May 15, 2026

---

## END OF DESIGN DOCUMENT
