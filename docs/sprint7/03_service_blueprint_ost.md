# RansomGuard-CM — Discovery Phase, Day 3

**Document Type**: Service Blueprint + Opportunity Solution Tree
**Phase**: Discovery (Master Plan Phase 1)
**Status**: Authoritative — Closes Discovery Phase
**Author Role**: Staff PM + UX Researcher (solo)
**Methodology Sources**:
- Lynn Shostack — "Designing Services That Deliver" (HBR, 1984), original Service Blueprint
- Nielsen Norman Group — *Service Blueprints: Definition* (Gibbons, 2017)
- Teresa Torres — *Continuous Discovery Habits* (2021), Opportunity Solution Trees
- Marty Cagan — *Empowered* (2020), Discovery practices

---

## Table of Contents

1. [Service Blueprint — End-to-End Incident Response](#1-service-blueprint--end-to-end-incident-response)
2. [Opportunity Solution Tree (OST)](#2-opportunity-solution-tree-ost)
3. [Discovery Phase — Closure & Handoff](#3-discovery-phase--closure--handoff)

---

## 1. Service Blueprint — End-to-End Incident Response

### Methodology Notes

Per Shostack (1984) and Gibbons (NN/g 2017), a Service Blueprint extends a journey map by exposing the **backstage**, **support processes**, and **physical evidence** that enable the frontstage experience. Four canonical lanes:

1. **Customer Actions** (frontstage, visible to user)
2. **Frontstage Actions** (employees / system actions visible to customer)
3. **Backstage Actions** (employees / system actions invisible to customer)
4. **Support Processes** (third parties / systems that enable backstage)

Plus **two lines**:
- **Line of Interaction** (customer ↔ frontstage)
- **Line of Visibility** (frontstage ↔ backstage)
- **Line of Internal Interaction** (backstage ↔ support)

### Scope

Same scenario as Day 2 journey map: USB-borne ransomware detected → contained → audited.

### Blueprint Diagram (Textual Representation)

```
═══════════════════════════════════════════════════════════════════════════════
                  SERVICE BLUEPRINT — INCIDENT TRIAGE & RESPONSE
═══════════════════════════════════════════════════════════════════════════════

┌──────────────────┬─────────────┬─────────────┬─────────────┬─────────────┐
│  PHASE           │  DETECT     │  TRIAGE     │  CONTAIN    │  REPORT     │
│                  │  (T+0-8s)   │  (T+8-90s)  │  (T+90s-5m) │  (T+30m-2h) │
├══════════════════┴═════════════┴═════════════┴═════════════┴═════════════┤
│                                                                            │
│  PHYSICAL EVIDENCE                                                         │
│  ─────────────────                                                         │
│  - SMS notification  - Login screen    - Process tree     - PDF report     │
│  - Email alert       - Dashboard       - Map of network   - Signed export  │
│  - Phone vibration   - Alert detail    - Isolation badge  - Audit log UI   │
│  - Console badge     - Severity colors - Heartbeat status - Compliance     │
│                      - Action buttons  - Real-time state    progress bar   │
│                                                                            │
├────────────────────────────────────────────────────────────────────────────┤
│  ▼ LINE OF INTERACTION ▼                                                   │
├────────────────────────────────────────────────────────────────────────────┤
│                                                                            │
│  CUSTOMER ACTIONS (Marc Tchoumi — security_analyst)                        │
│  ───────────────────────────────────────────────                           │
│  -               - Reads SMS         - Clicks Isolate    - Writes notes    │
│  -               - Opens laptop      - Confirms          - Closes incident │
│  -               - Logs in           - Watches status    - Exports PDF     │
│  -               - Clicks alert      - Calls Amani       - Archives        │
│  -               - Reads narrative   - Reviews forensics                   │
│                                                                            │
│  CUSTOMER ACTIONS (Dr. Amani Nkomo — tenant_admin)                         │
│  ──────────────────────────────────────────────                            │
│  -                                   - Receives call     - Reviews report  │
│  -                                   - Opens dashboard   - Signs ANTIC     │
│  -                                   - Asks questions    - Briefs Board    │
│  -                                   - Approves action                     │
│                                                                            │
├────────────────────────────────────────────────────────────────────────────┤
│  ▼ LINE OF INTERACTION ▼                                                   │
├────────────────────────────────────────────────────────────────────────────┤
│                                                                            │
│  FRONTSTAGE ACTIONS (RansomGuard Console UI — visible)                     │
│  ─────────────────────────────────────────                                 │
│  - Push SMS      - Render dashboard - Show modal       - Generate PDF      │
│  - Send email    - Render alert     - Update status    - Stream download   │
│  - Console badge - Render forensics - Stream events    - Audit timeline    │
│  - Severity      - Highlight        - Real-time refresh                    │
│    color-code      critical                                                │
│                                                                            │
├────────────────────────────────────────────────────────────────────────────┤
│  ▼ LINE OF VISIBILITY ▼                                                    │
├────────────────────────────────────────────────────────────────────────────┤
│                                                                            │
│  BACKSTAGE ACTIONS (GRID Server — invisible)                               │
│  ───────────────────────────────────────                                   │
│  - Receive alert  - Validate JWT    - Queue command    - Aggregate logs   │
│    from agent       Rate limit        for agent          for report        │
│  - Correlate     - Authorize role   - Update DB        - Render PDF        │
│    alerts          (RBAC check)       state              (ReportLab)       │
│  - Compute       - Filter by        - Log audit        - Sign with         │
│    priority        tenant_id          entry (Ed25519)    Ed25519           │
│    score                                                                    │
│  - Persist to    - Serialize JSON   - Notify           - Hash-chain        │
│    MySQL           response           dashboard          continuity         │
│                                                                            │
├────────────────────────────────────────────────────────────────────────────┤
│  ▼ LINE OF INTERNAL INTERACTION ▼                                          │
├────────────────────────────────────────────────────────────────────────────┤
│                                                                            │
│  SUPPORT PROCESSES (External / Infrastructure)                             │
│  ──────────────────────────────────────────                                │
│  - Agent on PC   - nginx mTLS       - Agent heartbeat  - Encrypted backup  │
│  - USB GUARD     - Redis cache      - Command pickup   - PKI Ed25519       │
│  - SENTINEL      - JWT signing      - Network ACL      - MySQL replication │
│  - GENEALOGY     - PKI validation   - Containment      - Threat intel feed │
│  - ENTROPY       - Threat intel       script execute   - SMTP service      │
│  - ETW kernel    - Multi-tenant     - Local SQLite     - SMS gateway       │
│    hooks           isolation          on endpoint                          │
│                                                                            │
└────────────────────────────────────────────────────────────────────────────┘
```

### Critical Failure Points & Mitigations

**Per Shostack's "Fail Points" analysis, each transition between lanes is a potential failure:**

| Failure Point | Cause | Impact | Mitigation in Sprint 7 |
|---|---|---|---|
| FP1 — SMS not delivered | SMS gateway down | Analyst not notified | Email backup channel + console badge |
| FP2 — Login slow (>5s) | Backend overload | Triage delayed | Health check + skeleton screens |
| FP3 — Alert detail timeout | Heavy correlation query | Cognitive break | Query optimization + caching |
| FP4 — Command queue delay (heartbeat 30s) | Agent on slow polling | Containment delayed | Heartbeat to 10s for critical commands (Sprint 8: WebSocket) |
| FP5 — Status not updated in UI | TanStack Query stale | Marc thinks isolation failed | Mutation invalidation + optimistic UI |
| FP6 — Cross-tenant data leak | Backend bug | Catastrophic compliance failure | E2E tests reuse the 10/10 isolation tests |
| FP7 — PDF generation timeout | ReportLab slow on large data | Compliance workflow blocked | Async generation + email when ready |
| FP8 — Hash-chain break | Bug in Ed25519 signing | Legal admissibility lost | Continuous chain verification cron |

### Service Standards (Per Phase)

| Phase | Standard | Source |
|---|---|---|
| Detection | Time from on-endpoint event to GRID alert ≤ 5s | Industry: CrowdStrike claims 1s |
| Triage | Median time to acknowledge ≤ 60s | NN/g enterprise UX |
| Containment | Median time to isolation effective ≤ 60s | MITRE ATT&CK mitigation guides |
| Report generation | Quarterly ANTIC report ≤ 5 min | Stretch goal from journey map |

---

## 2. Opportunity Solution Tree (OST)

### Methodology Notes

Per Teresa Torres (*Continuous Discovery Habits*, 2021):
- **Root** = a single, measurable business outcome (NOT a feature).
- **Opportunity nodes** = customer needs, pain points, desires (sourced from research).
- **Solution nodes** = experiments to address opportunities (multiple per opportunity).
- **Assumption Tests** = leaves; each solution has assumptions that must be validated.
- The tree is **continuously updated** with new research; not a one-time artifact.

### The Root — Business Outcome

**Outcome:**

> *"Reduce the time it takes a francophone hospital IT team to detect, contain, and audit a ransomware incident from a baseline of HOURS (current state without RansomGuard-CM) to MINUTES (target state), measurably and reproducibly, within 3 pilot deployments by end of 2027."*

**Measurable target metric (Hospital SOC KPI):**
> **Mean Time To Containment (MTTC) ≤ 5 minutes p50, ≤ 15 minutes p95 across pilot deployments.**

This metric is chosen because:
- It directly maps to patient safety (every minute of unchecked ransomware = more encrypted patient records).
- It is observable from system telemetry (no need for user reporting).
- It aligns with industry benchmarks: CrowdStrike claims median 1h 24min MTTC for managed customers (CrowdStrike 2024 Global Threat Report); SANS reports African hospital baselines of 4-12 hours.

### OST Tree (Textual Representation)

```
                    ┌─────────────────────────────────────────┐
                    │ OUTCOME                                  │
                    │ Reduce MTTC from HOURS to MINUTES        │
                    │ Target: ≤5 min p50, ≤15 min p95          │
                    └─────────────────┬───────────────────────┘
                                      │
        ┌─────────────────────────────┼─────────────────────────────┐
        │                             │                             │
   ┌────▼──────────────┐    ┌─────────▼──────────┐     ┌────────────▼─────┐
   │ OPPORTUNITY A     │    │ OPPORTUNITY B      │     │ OPPORTUNITY C    │
   │ Analysts triage   │    │ Containment action │     │ Admins approve   │
   │ alerts faster     │    │ executed faster    │     │ critical actions │
   │ (from Day 2 OST   │    │ (from Day 2 OST    │     │ faster (from     │
   │ analyst#1 O=16)   │    │ analyst#3 O=15)    │     │ admin#8 O=14)    │
   └─┬─────────────────┘    └─┬──────────────────┘     └─┬────────────────┘
     │                        │                          │
     ├─ SOL A1: Priority      ├─ SOL B1: 1-click         ├─ SOL C1: Real-time
     │  Score 0-100           │  Isolate with auto-        push to admin
     │  (Defender XDR         │  filled reason             on critical
     │  pattern)              │                            │
     │  AT: Will analysts     ├─ SOL B2: WebSocket         ├─ SOL C2: Auto-
     │  trust score?          │  for sub-10s heartbeat     generated exec
     │  → Test: A/B prototype │  (Sprint 8)                summary
     │  task-time             │                            │
     │                        ├─ SOL B3: Optimistic        ├─ SOL C3: In-app
     ├─ SOL A2: Inverted-     │  UI on action submit       approve workflow
     │  pyramid alert         │                            │
     │  narrative             ├─ SOL B4: Confirmation      ├─ SOL C4: Severity-
     │  AT: Will analysts     │  modal streamlined         to-impact translator
     │  read summary first?   │  (1 question instead of    (LLM-generated)
     │  → Test: heatmap       │  3)
     │  with 5 users          │
     │                        └─ SOL B5: Bulk isolate
     ├─ SOL A3: Filter by        across endpoint group
     │  module (SENTINEL,
     │  USB GUARD, etc.)
     │
     └─ SOL A4: Keyboard
        shortcuts (Ctrl+K
        command palette)


                    ┌─────────────────────────────────────────┐
                    │ OUTCOME                                  │
                    │ (Same as above)                          │
                    └─────────────────┬───────────────────────┘
                                      │
              ┌───────────────────────┴────────────────────────┐
              │                                                │
   ┌──────────▼──────────────┐                     ┌───────────▼───────────┐
   │ OPPORTUNITY D           │                     │ OPPORTUNITY E         │
   │ False-positive rate     │                     │ Compliance documenta- │
   │ reduced (from Day 2     │                     │ tion auto-generated   │
   │ OST analyst#2 O=17)     │                     │ (from Day 2 OST       │
   │                         │                     │ admin#3 O=16)         │
   └─┬───────────────────────┘                     └─┬─────────────────────┘
     │                                                │
     ├─ SOL D1: Confidence                            ├─ SOL E1: One-click
     │  score visible per                             │  PDF export
     │  alert
     │                                                ├─ SOL E2: Hash-chain
     ├─ SOL D2: Suppress                              │  verification UI for
     │  similar alerts                                │  inspectors
     │  (deduplication)
     │                                                ├─ SOL E3: ANTIC-
     ├─ SOL D3: Allowlist                             │  format template
     │  for known-good                                │  pre-mapped
     │  processes                                     │
     │                                                ├─ SOL E4: Audit log
     └─ SOL D4: ML-based                              │  search + export
        false-positive
        scoring (Sprint 9+)                          └─ SOL E5: Cross-
                                                        hospital aggregation
                                                        for inspectors
                                                        (Sprint 8+)
```

### Sprint 7 Solution Selection

Per Torres' framework, in any given cycle we **commit to a small set of solutions** and explicitly test their assumptions. For Sprint 7 we commit to:

| Solution ID | Description | Sprint 7 In? | Sprint 8+ Defer |
|---|---|:---:|:---:|
| **SOL A1** | Priority Score 0-100 (Defender pattern) | ✅ YES | — |
| **SOL A2** | Inverted-pyramid alert narrative | ✅ YES | — |
| **SOL A3** | Filter by module + severity + status | ✅ YES | — |
| **SOL A4** | Keyboard shortcuts (Ctrl+K palette) | 🟡 STRETCH | — |
| **SOL B1** | 1-click Isolate with auto-filled reason | ✅ YES | — |
| **SOL B2** | WebSocket sub-10s heartbeat | ❌ NO | Sprint 8 |
| **SOL B3** | Optimistic UI on action submit | ✅ YES | — |
| **SOL B4** | Streamlined confirmation modal | ✅ YES | — |
| **SOL B5** | Bulk isolate | ❌ NO | Sprint 8 |
| **SOL C1** | Real-time push to admin on critical | 🟡 STRETCH | — |
| **SOL C2** | Auto-generated exec summary | ❌ NO | Sprint 8 (LLM-based) |
| **SOL C3** | In-app approve workflow | ❌ NO | Sprint 8 |
| **SOL C4** | Severity-to-impact translator | ❌ NO | Sprint 8 |
| **SOL D1** | Confidence score per alert | ✅ YES | — |
| **SOL D2** | Alert deduplication / suppression | 🟡 STRETCH | — |
| **SOL D3** | Allowlist for known-good processes | ❌ NO | Sprint 8 |
| **SOL D4** | ML-based FP scoring | ❌ NO | Sprint 9+ |
| **SOL E1** | One-click PDF export | ❌ NO | Sprint 8 (model gap) |
| **SOL E2** | Hash-chain verification UI | 🟡 STRETCH | — |
| **SOL E3** | ANTIC template pre-mapped | ❌ NO | Sprint 8 |
| **SOL E4** | Audit log search + CSV export | ✅ YES | — |
| **SOL E5** | Cross-hospital aggregation | ❌ NO | Sprint 9+ |

### Assumption Tests (per Torres) for Sprint 7 Solutions

For each YES-committed solution, what assumption are we testing, and how?

| Solution | Assumption | Test (lightweight) |
|---|---|---|
| SOL A1 (Priority 0-100) | Analysts will trust and use a numeric score over a categorical severity | Internal heuristic eval against 3 reviewers; usability test with prototype if access to ≥3 hospital IT staff is feasible |
| SOL A2 (Inverted-pyramid narrative) | Analysts read top-of-narrative first, scroll only when needed | A/B compare in prototype testing: original-format vs inverted-pyramid task completion time |
| SOL A3 (Filters) | Multi-axis filtering reduces triage time | Measure task time in prototype; target: ≤30s to filter to "critical USB-related alerts last 24h" |
| SOL B1 (1-click isolate with auto-reason) | Analysts trust auto-filled reasons enough to confirm without rewriting | Default reason is editable; track in telemetry % of times default is kept vs edited (Sprint 8 instrumentation) |
| SOL B3 (Optimistic UI) | Users tolerate brief reverts on rare failures | Test with simulated API failures during usability testing |
| SOL B4 (Streamlined confirm) | One question (Reason) is enough; checkbox is optional polish | Test against original 3-question modal in prototype |
| SOL D1 (Confidence score) | Confidence score reduces false-positive investigation time | Measure prototype task time on alerts with confidence < 70% (analyst should de-prioritize) |
| SOL E4 (Audit log search) | Inspectors find what they need without IT staff help | Test with one ANTIC inspector role-play; target: find a specific past incident in ≤2 min |

### Stretch Solutions (capacity-permitting)

Stretch items move from NO → YES if Sprint 7 mid-sprint review shows ≥1 day of buffer. They are deliberately small and de-risked.

---

## 3. Discovery Phase — Closure & Handoff

### Discovery Phase Deliverables Checklist (per Master Plan)

| Artefact | Status | File |
|---|:---:|---|
| Product Vision Doc (1-page) | ✅ Done | 01_vision_strategy_jtbd.md §1 |
| Strategy on a Page | ✅ Done | 01_vision_strategy_jtbd.md §2 |
| JTBD Canvas (Ulwick) | ✅ Done | 01_vision_strategy_jtbd.md §3 |
| 3 Personas (formal) | ✅ Done | 02_personas_empathy_journey.md §1-3 |
| Empathy Maps (XPLANE v2) | ✅ Done | 02_personas_empathy_journey.md §4 |
| User Journey Map | ✅ Done | 02_personas_empathy_journey.md §5 |
| Service Blueprint | ✅ Done | 03_service_blueprint_ost.md §1 |
| Opportunity Solution Tree | ✅ Done | 03_service_blueprint_ost.md §2 |
| Limitations & Future Research note | ✅ Done | 01_vision_strategy_jtbd.md §4 + 02 §1-3 caveats |

### Discovery Insights — Top 10 Actionable Inputs for Definition Phase

1. **Three roles map cleanly to Defender XDR / Kaspersky / SentinelOne patterns** — no need to invent novel UI conventions.
2. **Marc's triage workflow is the dominant Sprint 7 success metric** — every UX decision in the alerts module must reduce his triage time.
3. **Amani's executive view must include a "what does this mean for patients" non-technical summary** — even if mock-generated in Sprint 7, expandable to LLM-based in Sprint 8.
4. **Jeanne's read-only mode mirrors Kaspersky Security Center's Dashboard-only mode** — implement that pattern verbatim.
5. **Priority Score 0-100 with three color bands** is the single most impactful pattern to adopt from Defender XDR.
6. **Status workflow must be New / In progress / Resolved** (Defender mapping to our existing `new / acknowledged / closed` works with relabeling in UI).
7. **Severity hierarchy must be Critical / High / Medium / Low / Informational** (matches Defender) — verify backend AlertStatus enum supports this.
8. **Tamper-evident audit logs are a top-priority opportunity for Jeanne** — UI must surface hash-chain verification prominently.
9. **Confirmation modals must be streamlined to 1 question** for critical actions (not the 3-field modal in PRD v2 currently).
10. **Compliance reports are deferred to Sprint 8** but the audit log foundation must be Sprint-7-correct (already implemented backend).

### Risks Identified During Discovery

| ID | Risk | Mitigation in Definition Phase |
|---|---|---|
| RD1 | Personas are hypothetical, not validated | Document as Chapter 5 limitation; design for the 3 archetypal roles found in all benchmarks (Defender, Kaspersky, SentinelOne) |
| RD2 | Cameroonian context assumptions may not hold | Validate via 1-2 informal calls with FICT alumni in hospital IT, even if not formal UXR |
| RD3 | Backend AlertStatus enum may not match Defender taxonomy | Audit enum.py Day 1 of Definition phase |
| RD4 | ANTIC report format unknown | Defer compliance reports to Sprint 8; design UI in Sprint 7 as placeholder ("Generate compliance report — coming Sprint 8") |
| RD5 | Hospital connectivity unknown | Assume intermittent; design for graceful offline state from Day 1 frontend (banner + cached data) |

### Handoff to Definition Phase

The Definition Phase (Days 4-5) will consume the following from Discovery:

| Definition Input | From Discovery |
|---|---|
| Information Architecture | Personas + journey map (drives navigation taxonomy) |
| User flows + task flows | Journey map phases |
| Sitemap | Service blueprint + journey |
| STRIDE web tier update | Service blueprint backstage actions |
| Privacy DPIA | Service blueprint support processes + Loi 2024/017 mapping |
| Data inventory | Service blueprint backstage |

### Discovery Phase — Final Statement

The Discovery Phase has produced 9 senior-grade artefacts establishing the strategic, contextual, and prioritization foundation for RansomGuard-CM Sprint 7. All three RBAC roles have been formalized, their needs mapped via JTBD with Ulwick scoring, their experience traced end-to-end via journey map and service blueprint, and a single measurable business outcome (MTTC ≤5 min p50) anchors a 5-opportunity, 19-solution Opportunity Solution Tree. The 8 highest-priority solutions are committed to Sprint 7; the remainder are mapped to Sprint 8+ with explicit reasoning.

**Discovery Phase — CLOSED.**

**Next: Definition Phase (Days 4-5) — Information Architecture, User Flows, STRIDE Web Tier Update, Privacy DPIA.**
