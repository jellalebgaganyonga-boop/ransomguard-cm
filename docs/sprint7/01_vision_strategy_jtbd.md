# RansomGuard-CM — Discovery Phase, Day 1

**Document Type**: Product Vision + Strategy + Jobs-To-Be-Done Canvas
**Phase**: Discovery (Master Plan Phase 1)
**Status**: Authoritative
**Author Role**: Staff Product Manager (solo)
**Methodology Sources**:
- Marty Cagan (SVPG) — *Inspired* (2017), *Empowered* (2020)
- Tony Ulwick — *Jobs To Be Done: Theory to Practice* (2016)
- Clayton Christensen — *Competing Against Luck* (2016)
- Teresa Torres — *Continuous Discovery Habits* (2021)

---

## Table of Contents

1. [Product Vision (1-Page)](#1-product-vision-1-page)
2. [Strategy on a Page](#2-strategy-on-a-page)
3. [JTBD Canvas — Ulwick Outcome-Driven Innovation](#3-jtbd-canvas--ulwick-outcome-driven-innovation)
4. [Validation Notes & Limitations](#4-validation-notes--limitations)

---

## 1. Product Vision (1-Page)

### Vision Statement

> **By 2028, RansomGuard-CM will be the de facto anti-ransomware EDR platform deployed in at least 30 francophone Sub-Saharan African hospitals, protecting patient records and clinical operations against the modern ransomware threat landscape, with offline-first capability, full bilingual French/English UX, and native compliance with Loi 2024/017 and ANTIC regulatory frameworks.**

### Problem Statement (Crue)

Francophone Sub-Saharan African hospitals face an asymmetric ransomware threat:

1. **Threat reality** — Documented incidents in Cameroon (CNPS / Spacebears 2024, Ascoma Cameroun / Akira March 2025 with 1.5TB exfiltrated including the SANTE folder, Chanas Assurances).
2. **Solution gap** — Existing EDR platforms (CrowdStrike Falcon, Microsoft Defender XDR, SentinelOne Singularity, Kaspersky Endpoint Security Cloud) are designed for English-primary, always-connected, well-funded Western SOCs. None are packaged for:
   - Francophone-primary user base
   - Offline-first / intermittent connectivity
   - Windows 7 legacy endpoints (still prevalent in African district hospitals)
   - Hospital IT staff with generalist skill profiles (not specialized SOC analysts)
   - Local regulatory compliance (Loi 2024/017, ANTIC)
   - Budget constraints (USD 500K-2M annual hospital IT budget, vs Western multi-million dollar SOC budgets)
3. **Patient impact** — A ransomware-disabled hospital cannot deliver care: drug dispensing freezes, patient records become inaccessible, imaging systems halt. In African contexts with limited fallback infrastructure, this directly endangers lives.

### Target Customer (Crue)

**Primary customer:** Mid-size francophone Sub-Saharan African hospitals (50–500 beds), with:
- Established IT department (1–5 staff)
- At least basic network infrastructure (intermittent internet)
- Regulatory pressure (ANTIC inspections, ministerial oversight)
- Past incident exposure or high awareness of the threat

**Secondary customer:** Regional health authorities, national ministries of health, NGO healthcare networks (MSF, Caritas, Catholic Relief Services hospitals).

### Value Proposition (Crue)

| Promise | Mechanism |
|---|---|
| Detect ransomware before encryption | 6 detection modules (SENTINEL canaries, ENTROPY analysis, GENEALOGY process trees, USB GUARD, EXFIL WATCH, IRONCLAD backup isolation) |
| Operate without internet | Offline-first agent + local threat intel cache + bilingual offline UI |
| Comply with local regulations | ANTIC-formatted reports, Loi 2024/017 audit trail, hash-chained logs |
| Affordable for African budgets | 10–20% the licensing cost of Western EDRs |
| Speak the user's language | French-default UI, Cameroonian Camfranglais-aware help content |

### Key Differentiators (vs CrowdStrike / Defender / SentinelOne / Kaspersky)

1. **Offline-first architecture** — competitor agents require online cloud control plane; RansomGuard-CM agent operates standalone for ≥7 days.
2. **Francophone-primary UX** — competitors offer French as secondary; we are French-first.
3. **Hospital-specific detection content** — competitors offer generic Windows EDR; we ship pre-tuned policies for HIS/EMR/PACS environments.
4. **Local regulatory native** — competitors require manual mapping to local frameworks; ANTIC-compliant reports ship out of the box.
5. **Hardware backup isolation (IRONCLAD)** — competitors offer software backup; we offer physical USB-relay isolation (planned hardware, software-only in Sprint 7).

### Non-Goals (Sprint 7 Scope)

- Not a SIEM (we ingest endpoint telemetry only, not network/cloud logs).
- Not an MDR service (no human SOC on-call provided).
- Not a multi-platform agent in v1 (Windows-only; Linux/macOS in roadmap).
- Not a SaaS multi-tenant cloud in v1 (on-premise per-hospital deployment).

---

## 2. Strategy on a Page

### One-Page Strategy Canvas

```
┌─────────────────────────────────────────────────────────────────────────┐
│  RANSOMGUARD-CM — STRATEGY ON A PAGE                                     │
├─────────────────────────────────────────────────────────────────────────┤
│                                                                           │
│  OUR INSIGHT                                                              │
│  Existing EDRs are too expensive, too complex, and too English-          │
│  centric for francophone African hospitals — leaving them defenseless    │
│  against a ransomware threat that is documented and active in the        │
│  region.                                                                  │
│                                                                           │
│  OUR APPROACH                                                             │
│  Build an offline-first, francophone-native, hospital-specific EDR        │
│  with native ANTIC/Loi 2024/017 compliance, deployable on Windows 7+      │
│  endpoints, at 10–20% the cost of Western competitors.                   │
│                                                                           │
│  WHO WE'RE BUILDING FOR (3 RBAC Roles)                                    │
│  - Hospital Administrator (tenant_admin) → Dr. Amani                     │
│  - Security Analyst (security_analyst) → Marc Tchoumi                    │
│  - Read-Only Auditor (read_only_auditor) → Sister Jeanne                 │
│                                                                           │
│  WHAT MAKES US DIFFERENT                                                  │
│  1. Offline-first agent (7+ days standalone)                              │
│  2. French-default UI with Camfranglais help                              │
│  3. Hospital-tuned detection (HIS/EMR/PACS)                               │
│  4. ANTIC reports ship native                                             │
│  5. Hardware backup isolation (IRONCLAD)                                  │
│                                                                           │
│  HOW WE WIN                                                               │
│  - Academic credibility via this dissertation                             │
│  - Pilot deployments in 1–3 Cameroonian hospitals                         │
│  - Government endorsement (ANTIC, Ministère Santé)                       │
│  - Word-of-mouth in regional CIO networks                                 │
│  - Open documentation + closed binaries (freemium-style trust)            │
│                                                                           │
│  KEY METRICS (HEART Framework — Google)                                   │
│  - Happiness: SUS score ≥ 75 (vs industry ≥ 68)                          │
│  - Engagement: DAU/MAU not relevant (enterprise tool)                     │
│  - Adoption: 30 hospitals by 2028                                         │
│  - Retention: License renewal ≥ 85% Year 2                                │
│  - Task Success: Incident acknowledge < 1 min median                     │
│                                                                           │
│  KEY RISKS                                                                │
│  R1: Cannot validate with real hospital users in bachelor timeframe       │
│  R2: ANTIC regulatory clarifications may shift                            │
│  R3: Threat landscape shifts faster than detection content updates        │
│  R4: Competitor (Kaspersky) launches localized version                    │
│  R5: Infrastructure realities worse than assumed (no internet, etc.)      │
│                                                                           │
└─────────────────────────────────────────────────────────────────────────┘
```

### Strategic Choices (Cagan SVPG)

| Question | Answer |
|---|---|
| What value risk are we taking? | Will hospitals actually buy / deploy a thesis-grade product? |
| What usability risk? | Can hospital IT staff use a security console with no prior SOC training? |
| What feasibility risk? | Can we deliver Windows 7 + offline-first + bilingual in solo-dev timeframe? |
| What business viability risk? | Is the African EDR market large enough to sustain a product? |

### Bets (Cagan terminology)

1. **Big Bet:** Offline-first will be the killer differentiator.
2. **Medium Bet:** ANTIC compliance pre-packaging will accelerate procurement.
3. **Small Bet:** Bilingual fr/en UX will be enough; Camfranglais help is bonus.

---

## 3. JTBD Canvas — Ulwick Outcome-Driven Innovation

### Methodology Notes (Crue)

Per Tony Ulwick (*Jobs To Be Done: Theory to Practice*, 2016):
- **A job is the progress a customer is trying to make**, not a task or solution.
- **Desired outcomes are measurable** with the form: `<direction> the <unit of measure> of <object of control> [<contextual clarifier>]`.
- **Opportunity Score** = `Importance + max(Importance − Satisfaction, 0)` on a 1–10 scale.
- An outcome with Opportunity Score ≥ 12 = highly underserved (opportunity).
- An outcome with Opportunity Score 10–12 = underserved.
- An outcome with Opportunity Score < 10 = served or over-served.

### Core Job-To-Be-Done

**Job statement:**

> *When a ransomware threat actor begins targeting our hospital, help us detect, contain, and recover before patient care is disrupted, so that we protect lives, preserve regulatory compliance, and maintain institutional trust.*

### Job Map (8 Universal Steps — Ulwick)

| Step | Hospital IT Context |
|---|---|
| 1. Define | Define what counts as a credible ransomware threat |
| 2. Locate | Locate vulnerable endpoints in the hospital network |
| 3. Prepare | Prepare detection, response, and backup capabilities |
| 4. Confirm | Confirm a detection event is a true positive |
| 5. Execute | Execute containment (isolate endpoint, kill process) |
| 6. Monitor | Monitor the response and watch for lateral movement |
| 7. Modify | Modify policies based on lessons learned |
| 8. Conclude | Conclude with audit trail and compliance report |

### Desired Outcome Statements (Per Role)

Below: outcome statements in Ulwick format with hypothesized Importance (I) and current Satisfaction (S) ratings on 1–10 scale. Opportunity Score (O) = I + max(I − S, 0).

**⚠️ Critical limitation:** These ratings are hypothesized from secondary research (CrowdStrike case studies, Microsoft Defender adoption surveys, NN/g enterprise UX studies, Cameroonian incident reports). They are NOT validated with primary user research. The Master Plan recommends 5 interviews per round per Nielsen & Landauer; we document this as a Chapter 5 limitation.

---

#### Role 1 — tenant_admin (Hospital Administrator)

| # | Desired Outcome | I | S | O | Tier |
|---|---|:-:|:-:|:-:|---|
| 1 | Minimize the time it takes to understand the hospital's current security posture | 9 | 3 | 15 | 🔴 Highly underserved |
| 2 | Minimize the likelihood that a ransomware incident disrupts patient care | 10 | 4 | 16 | 🔴 Highly underserved |
| 3 | Minimize the time it takes to produce a compliance report for ANTIC inspection | 9 | 2 | 16 | 🔴 Highly underserved |
| 4 | Maximize confidence that the hospital is compliant with Loi 2024/017 | 9 | 3 | 15 | 🔴 Highly underserved |
| 5 | Minimize the effort required to onboard new IT staff onto the security platform | 7 | 5 | 9 | ⚪ Served |
| 6 | Minimize the budget required to maintain endpoint security | 8 | 4 | 12 | 🟠 Underserved |
| 7 | Maximize the executive board's trust in IT security capability | 8 | 4 | 12 | 🟠 Underserved |
| 8 | Minimize the time it takes to make a containment decision when a critical incident is reported | 9 | 4 | 14 | 🔴 Highly underserved |
| 9 | Minimize the dependency on external cybersecurity consultants for daily operations | 7 | 3 | 11 | 🟠 Underserved |
| 10 | Maximize the visibility into security spending ROI | 6 | 4 | 8 | ⚪ Served |

**Top 3 priorities for tenant_admin (highest O):**
1. **Outcome #2** (O=16): Minimize ransomware disruption to patient care → drives detection accuracy + speed.
2. **Outcome #3** (O=16): Minimize time to produce ANTIC report → drives compliance feature.
3. **Outcome #1** (O=15): Minimize time to understand security posture → drives executive dashboard design.

---

#### Role 2 — security_analyst (Hospital IT / Security Analyst)

| # | Desired Outcome | I | S | O | Tier |
|---|---|:-:|:-:|:-:|---|
| 1 | Minimize the time it takes to triage a new alert | 10 | 4 | 16 | 🔴 Highly underserved |
| 2 | Minimize the false-positive rate of detections | 10 | 3 | 17 | 🔴 Highly underserved |
| 3 | Minimize the time it takes to isolate a compromised endpoint | 9 | 3 | 15 | 🔴 Highly underserved |
| 4 | Maximize the clarity of the forensic narrative for a confirmed incident | 8 | 4 | 12 | 🟠 Underserved |
| 5 | Minimize the cognitive load when investigating multi-stage attacks | 9 | 4 | 14 | 🔴 Highly underserved |
| 6 | Minimize the time it takes to deploy the agent on a new endpoint | 7 | 5 | 9 | ⚪ Served |
| 7 | Maximize the agent's resilience against process kill / driver disablement | 9 | 5 | 13 | 🟠 Underserved |
| 8 | Minimize the time it takes to update detection policies in response to a new TTP | 8 | 3 | 13 | 🟠 Underserved |
| 9 | Maximize confidence that an alert is actionable before escalating | 9 | 4 | 14 | 🔴 Highly underserved |
| 10 | Minimize the disruption caused by isolating an endpoint that turns out to be benign | 8 | 5 | 11 | 🟠 Underserved |

**Top 3 priorities for security_analyst (highest O):**
1. **Outcome #2** (O=17): Minimize false-positive rate → drives detection tuning + confidence scoring.
2. **Outcome #1** (O=16): Minimize triage time → drives Incident Queue UX (Defender XDR pattern).
3. **Outcome #3** (O=15): Minimize isolation time → drives 1-click isolate workflow with strong confirmation.

---

#### Role 3 — read_only_auditor (External / Internal Auditor)

| # | Desired Outcome | I | S | O | Tier |
|---|---|:-:|:-:|:-:|---|
| 1 | Minimize the time it takes to verify hospital compliance status | 9 | 3 | 15 | 🔴 Highly underserved |
| 2 | Maximize confidence that audit logs are tamper-evident | 10 | 4 | 16 | 🔴 Highly underserved |
| 3 | Minimize the effort required to produce a quarterly audit report | 8 | 3 | 13 | 🟠 Underserved |
| 4 | Maximize the ability to compare security posture across multiple hospitals | 7 | 2 | 12 | 🟠 Underserved |
| 5 | Minimize the dependency on hospital IT staff for audit data extraction | 8 | 3 | 13 | 🟠 Underserved |
| 6 | Maximize confidence that the audit trail is complete (no gaps) | 9 | 4 | 14 | 🔴 Highly underserved |
| 7 | Minimize the time it takes to find evidence of a specific incident | 8 | 4 | 12 | 🟠 Underserved |
| 8 | Maximize the legal admissibility of audit logs in case of regulatory action | 9 | 3 | 15 | 🔴 Highly underserved |

**Top 3 priorities for read_only_auditor (highest O):**
1. **Outcome #2** (O=16): Tamper-evident audit logs → drives Ed25519 signed hash-chained log architecture (already implemented backend).
2. **Outcome #1** (O=15): Minimize compliance verification time → drives Dashboard-only mode (Kaspersky pattern).
3. **Outcome #8** (O=15): Legal admissibility → drives compliance report digital signing.

---

### Opportunity Synthesis Across All Roles

**Top 5 opportunities across all roles (highest aggregate Opportunity Scores):**

| Rank | Opportunity | Driving Outcomes | Sprint 7 Impact |
|---|---|---|---|
| 1 | **Reduce false-positive rate of detections** | analyst#2 (O=17) | Drives confidence scoring UI in alert detail |
| 2 | **Reduce time to triage an alert** | analyst#1 (O=16), admin#8 (O=14) | Drives Incident Queue with Defender-style priority score |
| 3 | **Guarantee tamper-evident audit logs** | auditor#2 (O=16), auditor#8 (O=15) | Drives hash-chain verification UI |
| 4 | **Reduce time to produce compliance reports** | admin#3 (O=16) | Drives one-click ANTIC PDF export |
| 5 | **Reduce ransomware disruption to patient care** | admin#2 (O=16) | Drives end-to-end detection→containment KPI on executive dashboard |

---

## 4. Validation Notes & Limitations

### What's Real

- The Cameroonian incident data (CNPS, Ascoma, Chanas) is documented.
- The role taxonomy (tenant_admin, security_analyst, read_only_auditor) is implemented in the backend with 6/6 RBAC tests passing.
- The detection capabilities (6 modules) are implemented in the agent.
- The compliance backbone (Ed25519 signed logs, multi-tenant isolation) is implemented.

### What's Hypothesized

- The Importance × Satisfaction ratings in the JTBD canvas are NOT empirically validated. They are senior-analyst estimates informed by:
  - CrowdStrike, SentinelOne, Defender case studies (English-language Western SOCs)
  - NN/g enterprise UX research
  - Cameroonian hospital incident reports (public)
  - The Heilmeier Catechism applied to each opportunity
- The personas (Dr. Amani, Marc Tchoumi, Sister Jeanne) are archetypes, not validated profiles from real interviews.

### Why We Proceed Anyway

Per Marty Cagan (*Inspired*, Ch. 27): *"Discovery is about answering the riskiest assumptions, not all assumptions. You start with the hypothesis you have and improve it as you learn."* For a bachelor thesis without access to live hospital users, we:

1. Document this limitation honestly in Chapter 5.
2. Apply the senior methodology rigorously to what we *can* control.
3. Position empirical validation as a recommendation for future work.
4. Use the JTBD canvas as a *prioritization* device — even hypothetical ratings force us to choose what matters most, which is the point of the exercise.

### Recommendation for Future Empirical Validation

Post-soutenance, the recommended next step is a 6-week empirical validation cycle:
- Week 1–2: Recruit 5 hospital IT staff + 2 ANTIC inspectors via the FICT alumni network.
- Week 3–4: Conduct semi-structured interviews per Teresa Torres' opportunity-mapping protocol.
- Week 5: Update JTBD canvas with real I/S ratings.
- Week 6: Re-prioritize roadmap.

---

**End of Discovery Day 1 — Product Vision + Strategy + JTBD Canvas**

**Next**: Discovery Day 2 — 3 Personas + Empathy Maps + Journey Map.
