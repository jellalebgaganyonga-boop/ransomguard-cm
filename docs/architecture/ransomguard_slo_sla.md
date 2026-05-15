# RansomGuard-CM — Service Level Objectives (SLO/SLA/SLI)

**Version:** 1.0
**Author:** Jella Lebga (Kuate Abdel Yaniv)
**Date:** May 2026
**Status:** Production-Ready
**Methodology:** Google SRE Book + ISO/IEC 25010

---

## 1. INTRODUCTION

### 1.1 Definitions

| Term | Definition | Example |
|---|---|---|
| **SLI** (Service Level Indicator) | Quantitative measure of service quality | "Detection time in seconds" |
| **SLO** (Service Level Objective) | Internal target threshold for an SLI | "99% of detections under 5 seconds" |
| **SLA** (Service Level Agreement) | External contractual commitment with consequences | "Uptime 99.5% — else 10% credit" |
| **Error Budget** | Allowed quantity of SLO violations per period | "1% × 30 days = 7.2 hours per month" |

### 1.2 Hierarchy Principle

```
SLA  (what we promise customers)
 ↓
SLO  (slightly tighter than SLA to leave safety margin)
 ↓
SLI  (the actual measurement)
```

**Senior rule:** SLOs are ALWAYS tighter than SLAs. If we commit to 99.5% uptime to customers, our internal SLO is 99.7%. The gap is our safety buffer.

### 1.3 Categories of SLOs for RansomGuard

Five critical categories aligned with the product's value proposition:

1. **Detection** — How fast and accurately we detect attacks
2. **Performance** — Resource consumption and responsiveness
3. **Availability** — System uptime
4. **Security** — Cryptographic and compliance guarantees
5. **Recovery** — Backup and restoration capabilities

---

## 2. SLO CATEGORY 1 — DETECTION

The most critical category — this is what customers buy us for.

### SLO-D1: Detection Latency

**SLI Definition:** Time elapsed between the first malicious file modification on disk and the agent isolating the endpoint.

**Measurement Method:**
```
detection_latency = isolation_timestamp - first_malicious_file_event_timestamp
```

Measured server-side from agent telemetry. Reported in the audit log for every confirmed incident.

**SLO Target:** 99% of confirmed ransomware detections complete isolation within **5 seconds**.

**Measurement Window:** Rolling 30 days.

**Why 5 seconds:** Based on research, modern ransomware can encrypt 100+ files per second on a SSD. At 5 seconds, worst-case file loss is bounded to ~500 files — recoverable from backup. Beyond 10 seconds, the loss curve becomes catastrophic.

**Justification of 99% (not 99.9%):** The 1% margin accounts for legitimate edge cases — Windows 7 with old SSDs where ETW event propagation is slower. Imposing 99.9% would force overly aggressive timeouts causing false positives.

---

### SLO-D2: Detection Coverage Rate

**SLI Definition:** Percentage of known ransomware families that RansomGuard detects in controlled testing.

**Measurement Method:**
```
coverage_rate = (detected_samples / total_tested_samples) × 100
```

Measured monthly against a curated test panel of 25 ransomware samples from MalwareBazaar covering major families (WannaCry, Locky, Conti, LockBit, Akira, BlackSuit, REvil, etc.).

**SLO Target:** ≥ 95% of test panel samples detected within first 5 seconds.

**Measurement Window:** Monthly test campaign.

**Justification:** Industry-leading EDR solutions report 95-98% detection rates in MITRE ATT&CK evaluations. Setting 95% as our target aligns us with the upper bracket while acknowledging that no solution achieves 100%.

---

### SLO-D3: False Positive Rate

**SLI Definition:** Number of false positive alerts triggering automated isolation per endpoint per month.

**Measurement Method:**
```
fp_rate = false_positive_isolations / (total_endpoints × 30 days)
```

A "false positive isolation" is defined as an isolation event subsequently reverted by an administrator marking the incident as `false_positive`.

**SLO Target:** < 0.5 false positive isolations per endpoint per month.

**For medical-critical endpoints:** SLO target is **0 false positive isolations per medical-critical endpoint** — these are protected by the "alert-only mode" + dual-authorization isolation policy.

**Justification:** Industry research shows that EDR products with > 1 FP per endpoint per month see 60%+ disabling rate by operators within 90 days. Our 0.5 target keeps us in the trust zone.

**Why zero for medical-critical:** Medical software disruption can cost lives. We trade theoretical detection optimality for guaranteed continuity of care.

---

### SLO-D4: Alert Processing Latency

**SLI Definition:** Time between alert receipt by the server and alert correlation completion (3-signal evaluation).

**Measurement Method:** Server-side instrumentation around the alert processing pipeline.

**SLO Target:** 99% of alerts processed within 200 ms.

---

## 3. SLO CATEGORY 2 — PERFORMANCE

Resource consumption on customer hardware.

### SLO-P1: Agent Memory Footprint

**SLI Definition:** RSS memory usage of the agent service measured every minute.

**SLO Target:** 99% of measurements show memory usage ≤ 200 MB.

**Justification:** Windows 7 legacy endpoints in Cameroonian hospitals often have only 4 GB RAM, with 2 GB consumed by Windows + medical software. 200 MB represents 5% of total RAM — invisible to users.

---

### SLO-P2: Agent CPU Usage (Idle Monitoring)

**SLI Definition:** Average CPU usage of the agent service during idle monitoring periods (no active threat).

**SLO Target:** 95% of measurements show idle CPU ≤ 5% on a single core.

**Burst tolerance:** During active analysis (signal evaluation), spikes to 30% are acceptable for ≤ 10 seconds.

---

### SLO-P3: Dashboard Response Time

**SLI Definition:** Time to first contentful paint for the dashboard incident list page.

**SLO Target:** 95% of page loads complete within 2 seconds on LAN (50ms RTT).

---

### SLO-P4: API Response Time (p99)

**SLI Definition:** End-to-end latency of API requests, measured server-side.

**SLO Targets:**

| Endpoint Class | p99 Latency Target |
|---|---|
| Heartbeat (POST /agents/heartbeat) | < 100 ms |
| Alert submission (POST /alerts) | < 500 ms |
| Incident list (GET /incidents) | < 1000 ms |
| PDF report generation | < 5000 ms |

---

## 4. SLO CATEGORY 3 — AVAILABILITY

### SLO-A1: Management Server Uptime

**SLI Definition:** Percentage of time the management server's `/health` endpoint returns 200 OK.

**Measurement Method:** External health checks every 1 minute from a separate monitoring node.

**SLO Target:** 99.5% monthly uptime.

**Error budget:** 0.5% × 30 days = **3.6 hours of allowed downtime per month**.

**Justification:** 99.9% would require multi-node clustering, beyond v1.0 scope. 99% is too lax for production healthcare. 99.5% achievable with a single well-maintained server + automated restart procedures.

---

### SLO-A2: Agent Connectivity

**SLI Definition:** Percentage of agents reporting heartbeat within the last 5 minutes.

**SLO Target:** 95% of registered agents online at any given measurement.

**Note:** Reduced from typical 99% because hospital endpoints are frequently powered off (night shifts, weekends, replaced staff workstations). 95% accounts for realistic Cameroonian hospital usage patterns.

---

### SLO-A3: Cloud Update Service Availability

**SLI Definition:** Percentage of time the cloud `/updates/check` endpoint returns valid responses.

**SLO Target:** 99.9% monthly uptime.

**Justification:** Higher target than the on-prem server because:
1. The cloud is shared infrastructure
2. AWS af-south-1 provides multi-AZ deployment
3. Offline-first architecture means agents survive cloud outages

The 99.9% on the cloud + 99.5% on-prem creates a combined availability of 99.4% for end-to-end protection.

---

## 5. SLO CATEGORY 4 — SECURITY

These SLOs are **non-negotiable** — they protect the integrity of the entire platform.

### SLO-S1: Cryptographic Algorithm Compliance

**SLI Definition:** Percentage of data-in-transit using TLS 1.3 with approved cipher suites.

**SLO Target:** 100% compliance. No exceptions.

**Approved suites:** TLS_AES_256_GCM_SHA384, TLS_CHACHA20_POLY1305_SHA256.

---

### SLO-S2: Authentication Security

**SLI Definition:** Percentage of administrative actions requiring MFA verification.

**SLO Target:** 100% of admin actions logged with MFA verification flag = true.

---

### SLO-S3: Audit Log Integrity

**SLI Definition:** Percentage of weekly audit log integrity verifications that pass (hash chain valid from genesis to latest entry).

**SLO Target:** 100%. Any failure triggers immediate incident response.

**Procedure:** Run `verify_audit_chain()` procedure weekly. If chain breaks → incident response runbook triggered, forensic investigation initiated.

---

### SLO-S4: Backup Integrity

**SLI Definition:** Percentage of backup snapshots whose post-creation integrity check passes (SHA-256 manifest verification).

**SLO Target:** 99.9% of snapshots verified successfully within 10 minutes of creation.

**On failure:** Alert administrator, mark snapshot as `corrupted`, schedule re-backup.

---

### SLO-S5: Update Signature Verification

**SLI Definition:** Percentage of updates installed only after valid dual Ed25519 signature verification.

**SLO Target:** 100%. An unsigned or invalid-signature update must NEVER be applied.

---

## 6. SLO CATEGORY 5 — RECOVERY

### SLO-R1: Recovery Point Objective (RPO)

**Definition:** Maximum data loss tolerable in case of disaster.

**SLO Target:** ≤ 2 hours of data loss for any protected endpoint.

**Implementation:** Scheduled backups every 2 hours + emergency backup on attack detection.

---

### SLO-R2: Recovery Time Objective (RTO)

**Definition:** Maximum time to restore an attacked endpoint to operational state.

**SLO Target:** ≤ 4 hours from approved restoration request to fully operational endpoint.

**Breakdown:**
- IRONCLAD disk connection: 5 seconds
- Decryption + transfer (avg 5 GB): 30-60 minutes
- Verification + reconnection: 5 minutes
- Buffer for human steps (approvals, validation): up to 3.5 hours

---

### SLO-R3: Backup Coverage

**Definition:** Percentage of critical files defined as "protected" that are present in the most recent snapshot.

**SLO Target:** 99% of files in protected directories present in latest snapshot.

**Edge cases (1%):** Files in use during backup (locked), files created within the last 5 minutes.

---

## 7. ERROR BUDGET POLICY

### 7.1 What is an Error Budget

For each SLO, we have a permitted quantity of violations:

```
Error Budget = (1 - SLO Target) × Measurement Window
```

Example for SLO-A1 (99.5% uptime monthly):
```
Error budget = (1 - 0.995) × 30 days × 24 hours = 3.6 hours/month
```

If we consume 3 hours in the first week, we have 0.6 hours remaining for the rest of the month — and our deployment policy changes.

### 7.2 Error Budget Consumption Levels

| Budget Remaining | Status | Action |
|---|---|---|
| > 50% | 🟢 Healthy | Normal operations, feature deployments allowed |
| 25% - 50% | 🟡 Caution | Increased scrutiny on changes, prioritize stability |
| 10% - 25% | 🟠 Critical | Code freeze except critical fixes |
| < 10% | 🔴 Emergency | Full freeze, incident-only deploys, executive review |

### 7.3 Application to Update Deployments

This directly drives the canary deployment policy:

- **Healthy budget:** Canary 1% → 10% → 100% with 15-min intervals
- **Caution budget:** Canary 1% → 5% → 25% → 100% with 1-hour intervals
- **Critical budget:** No new features deployed at all
- **Emergency budget:** Only security patches with executive approval

---

## 8. SERVICE LEVEL AGREEMENTS (SLA) — Customer-Facing

These are the contractual commitments offered to hospital customers.

### 8.1 Standard SLA (Bundled with all plans)

| Metric | Commitment | Measurement | Penalty if violated |
|---|---|---|---|
| Management Server Uptime | 99.5% per month | External monitoring | 10% credit on next invoice |
| Detection Latency | 95% under 10 sec | Audit log analysis | 5% credit on next invoice |
| Backup Success Rate | 99% per month | Server logs | 5% credit on next invoice |
| Critical Bug Fix Time | < 72 hours | Support ticket logs | 10% credit |
| Email Notification Delivery | 95% within 5 min | Email gateway logs | None — best effort |

### 8.2 Enterprise SLA (Premium tier)

Available for hospitals > 200 endpoints, additional fee.

| Metric | Commitment | Penalty if violated |
|---|---|---|
| Management Server Uptime | 99.9% per month | 25% credit |
| Detection Latency | 99% under 5 sec | 15% credit |
| 24/7 Support Response | < 1 hour for P1, < 4 hours for P2 | 15% credit |
| Quarterly Compliance Reports | Auto-generated | 5% credit if missed |

### 8.3 Exclusions

The SLA does NOT cover:
- Internet outages between hospital and cloud (force majeure)
- Customer-caused incidents (modification of agent, disabling protection)
- Power outages affecting the management server
- Acts of regulatory authority (ANTIC takeover, government shutdown)
- Zero-day kernel-level attacks (BYOVD) — out of scope as documented

### 8.4 SLA Reporting

Monthly SLA reports auto-generated and delivered to the customer's director email within 5 business days of month-end. Reports include:
- Achieved vs target for each metric
- Incidents that consumed error budget
- Credits applied to next invoice (if any)
- Trending analysis

---

## 9. MONITORING AND ALERTING

### 9.1 Internal Dashboards

Three Grafana dashboards for the RansomGuard team:

**Dashboard 1 — Real-Time Operations**
- All agents heartbeat status
- Active incidents counter
- Detection latency p50/p95/p99
- API request rate and error rate

**Dashboard 2 — SLO Compliance**
- Current SLO status (green/yellow/red) per category
- Error budget remaining per SLO
- Trending of budget consumption

**Dashboard 3 — Customer Health**
- Per-hospital SLA compliance
- Customers approaching SLA breach (proactive outreach)
- Backup success rates by hospital

### 9.2 Alert Severity Levels

| Severity | Definition | Response Time | Example |
|---|---|---|---|
| **P0 — Critical** | Customer-facing outage or security breach | < 15 min | Management server down, audit log integrity failure |
| **P1 — High** | SLO degraded, customer impact likely | < 1 hour | Detection latency p99 above 10 sec |
| **P2 — Medium** | SLO at risk but no immediate impact | < 4 hours | Error budget below 25% |
| **P3 — Low** | Informational, no urgent action | Next business day | Single failed backup retried successfully |

### 9.3 On-Call Rotation (For Production)

In v1.0 (student project), the author is the sole on-call.

Future production setup:
- Primary on-call: 1 week rotation
- Secondary on-call: backup if primary unreachable
- Escalation: support manager → CTO

---

## 10. SLO REVIEW CADENCE

| Frequency | Activity | Participants |
|---|---|---|
| Weekly | Review SLI dashboards, identify trends | Engineering |
| Monthly | SLO compliance review, error budget consumption analysis | Engineering + Product |
| Quarterly | SLA report compilation, customer-facing reports | Engineering + Sales |
| Annually | SLO target reassessment based on customer needs | Leadership |

---

## 11. EVOLUTION AND VERSIONING

This SLO document is a **living document**. Targets will be refined as we collect operational data.

**v1.0 (current):** Initial targets based on industry benchmarks and design intent.

**v1.1 (after 3 months in pilot):** Calibrated targets based on real operational data from first hospital.

**v2.0 (after 12 months):** Targets based on aggregated data from 10+ hospitals, with stratification by hospital size and infrastructure quality.

---

## 12. SENIOR ENGINEERING PRINCIPLES APPLIED

This SLO document follows the principles laid out in:
- **Google SRE Book** (Chapters 3-4: Service Level Objectives)
- **Site Reliability Workbook** (Chapter 2: Implementing SLOs)
- **ISO/IEC 25010** — Software product quality model
- **NIST SP 800-53** — Security control framework

The core insight from SRE: **100% reliability is the wrong target.** An SLO of 100% is unachievable, expensive, and provides no marginal benefit to users who can't perceive the difference. We aim for "just reliable enough" — and use the error budget to deliver new features.

---

## END OF SLO/SLA DOCUMENT
