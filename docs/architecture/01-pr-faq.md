# RansomGuard-CM — PR/FAQ (Working Backwards Document)

**Methodology:** Amazon Working Backwards
**Author:** Jella Lebga (Kuate Abdel Yaniv)
**Date:** May 2026
**Status:** Approved v1.0

---

## PART 1 — PRESS RELEASE

> *Written as if the product were already launched. Forces clarity on customer value before any engineering decision.*

---

**FOR IMMEDIATE RELEASE**

**Yaoundé, Cameroon — May 15, 2027**

## RansomGuard-CM: The First Anti-Ransomware Solution Designed for Francophone African Hospitals Is Now Available

**Autonomous, bilingual, and affordable protection against ransomware attacks paralyzing healthcare facilities. No cybersecurity expert required. No permanent internet connection needed.**

---

Yaoundé Central Hospital announced today the deployment of RansomGuard-CM, the first anti-ransomware protection platform specifically designed for hospitals in Francophone Africa. Six months after installation, the hospital reports zero ransomware incidents, despite 47 intrusion attempts blocked automatically.

Cameroon, like the rest of sub-Saharan Africa, faces a wave of cybercriminal attacks targeting the healthcare sector. African hospitals experience an average of 3,575 cyberattacks per week in 2025 — a 38% increase compared to the previous year. Yet no cybersecurity solution had been designed for the specific constraints of these facilities — English-only interface, permanent internet connection required, costs in tens of thousands of dollars, dependency on 24/7 security teams that don't exist locally.

**RansomGuard-CM solves exactly this problem.** Designed by a Cameroonian graduate of ICT University Yaoundé, the software installs in less than two hours in any hospital, operates without permanent internet, and requires no cybersecurity expert within the facility.

> *"Before RansomGuard, we were at the mercy of the first ransomware that came along. We were afraid of every USB key plugged in by staff. Today, the system detects, isolates, backs up, and sends us a report in French — without us needing to intervene. It's exactly what we needed."*
> — Dr Mballa, Director, Yaoundé Central Hospital

**How it works.** A lightweight agent installs on every Windows workstation in the hospital — including older Windows 7 SP1 machines coupled with medical equipment. A Linux server centralizes supervision. When a ransomware attack is detected — even one never seen before — RansomGuard automatically isolates the infected workstation from the network within 3 seconds, triggers an emergency backup to a physically isolated disk (IRONCLAD technology), and generates a bilingual PDF report for the director. The doctor arriving the next morning doesn't discover a disaster — they discover a report.

**The price factor.** Where a solution like CrowdStrike costs USD 35,000 per year for an average hospital, RansomGuard-CM is available starting at FCFA 850,000 per year (approximately USD 1,400) for a 50-workstation hospital. Payable via Mobile Money through MTN MoMo and Orange Money.

**Cameroon legal compliance.** RansomGuard-CM is compliant with Law N°2024/017 on personal data protection and Law N°2010/012 on cybersecurity. The software automatically generates compliance reports required by ANTIC.

**Availability.** RansomGuard-CM is available immediately for Cameroonian hospitals. A 30-day free trial period is offered to public and confessional facilities.

---

## PART 2 — INTERNAL FAQ (30 Questions a Staff Engineer Would Ask)

> *These 30 questions force clarity before any line of code is written.*

---

### Section A — The Customer and the Problem

**Q1 — Who exactly is the customer?**

The primary customer is the **medical director** or **administrative director** of a hospital or clinic in Cameroon. They sign the check. The daily user is the **hospital IT technician** (typically alone, without cybersecurity training).

The invisible end-user is the **doctor** who must be able to access patient records at 3 AM without knowing RansomGuard exists.

---

**Q2 — Why now? Why hasn't this problem been solved before?**

Three reasons converge in 2026-2027:
- Ransomware attacks in Africa increased 38% in one year
- Law 2024/017 takes effect in June 2026 with criminal sanctions
- WHO officially recognized hospital cybersecurity as a priority at UN Security Council in November 2024

The problem hasn't been solved before because global publishers (CrowdStrike, SentinelOne, Halcyon) don't consider the African market profitable. And local publishers don't exist in this niche.

---

**Q3 — What are the current solutions?**

Four sub-optimal options:
1. **Nothing** — majority of Cameroonian hospitals
2. **Pirated generic antivirus** — Kaspersky, Bitdefender installed without license
3. **Wazuh open source** — free but requires an expert hospitals don't have
4. **Enterprise solutions** — CrowdStrike at USD 35,000/year, out of budget

None are designed for the African context.

---

**Q4 — What is the most critical customer benefit?**

**The doctor can do their job in the morning without surprise.**

Everything else flows from this — no hospital paralysis, no patients dying from a locked system, no director in prison due to a Law 2024/017 violation, no destroyed reputation.

---

**Q5 — How do we know customers want this?**

Three converging signals:
- 67% of global hospitals affected in 2024 per Sophos
- 38% increase in African attacks in 2025 per Microsoft
- WHO explicitly demanding it
- Cameroonian law will require it from June 2026

**Honest limitation:** we have not yet conducted 15 field interviews with Cameroonian hospital directors. This is the first thing to do before development.

---

### Section B — The Solution

**Q6 — Why a separate product and not a module in an existing antivirus?**

Three reasons:
- Existing antivirus solutions don't cover ransomware-specific blind spots (IRONCLAD, semantic canaries, 3-second automatic isolation)
- No existing antivirus is designed offline-first
- None are natively bilingual French/English with ANTIC compliance

RansomGuard is **complementary** to antivirus, not competitive.

---

**Q7 — What is the real innovation, not marketing?**

Five concrete innovations:
- **Per-hospital behavioral learning** — each hospital has its own profile
- **Inter-hospital collective intelligence** — an attack elsewhere immunizes the entire network
- **IRONCLAD** — automated physical air-gap (GPIO USB relay)
- **Autonomous offline mode** — protection even without internet for weeks
- **Automatic bilingual reports** — non-technical director understands in 30 seconds

---

**Q8 — What new capabilities must we establish?**

- Mastery of low-level Windows development (ETW, WMI) for the agent
- Mastery of applied cryptography (Ed25519, AES-256-GCM)
- Hardware integration capability (Arduino GPIO for IRONCLAD)
- Mastery of embedded Machine Learning (Isolation Forest on Windows 7)
- Microsoft-trusted code signing capability

---

**Q9 — What third-party dependencies?**

- VirusTotal API (free up to 500 requests/day)
- AlienVault OTX (free, open threat exchange)
- MITRE ATT&CK (free, public)
- AbuseCH MalwareBazaar (free)
- Code signing certificate (DigiCert or Sectigo, paid in production)
- Cloud hosting (AWS Cape Town af-south-1)
- GSM modem hardware SMS service (local) — optional

---

### Section C — The Business

**Q10 — What is the actual addressable market?**

Cameroon alone:
- ~250 public hospitals + ~600 private clinics + ~1500 health centers = ~2350 potential facilities
- If we capture 5% at 850,000 FCFA/year minimum → ~100M FCFA/year
- Francophone African market (17 countries) → 10x more

**Honest limitation:** these figures are estimated. Field validation required.

---

**Q11 — What is the business model?**

Annual license (Kaspersky model):
- Small hospital (10-30 endpoints): 850,000 FCFA/year
- Medium hospital (30-100 endpoints): 2,500,000 FCFA/year
- Large hospital (100-300 endpoints): 6,000,000 FCFA/year

Payment via Mobile Money or local bank transfer.

---

**Q12 — What unit economics?**

Variable costs per hospital:
- Cloud hosting: ~5 USD/month per hospital
- VirusTotal API: marginal
- Support: 2 hours/month average
- Total: ~10,000 FCFA/month per hospital

Gross margin: >95% on licenses. Classic SaaS-like model.

---

**Q13 — How much initial investment required?**

For student MVP — 70,000 FCFA (hardware only, all software is free).

For commercial version — approximately 500,000 to 1,000,000 FCFA for:
- Code signing certificate
- Production AWS hosting
- Cameroon legal entity
- First paying pilot

---

### Section D — The Risks

**Q14 — What could kill this product?**

Five major identified risks:
- **A single incident where RansomGuard misses an attack** → trust destroyed immediately
- **A single false positive paralyzing an operating room** → lawsuit, reputation destroyed
- **A global publisher cuts prices to target Africa** → price war impossible
- **Law 2024/017 won't be effectively enforced by ANTIC** → no legal pressure
- **Hospitals prefer to pirate Kaspersky** → broken business model

---

**Q15 — What happens if CrowdStrike decides to target Africa?**

Three years of delay for them to understand the context. Competitive advantage:
- Our offline-first solution / their cloud-first → impossible to catch up quickly
- Our local FCFA pricing / their USD pricing → structural difference
- Our native French language / their translation → experience difference
- Our native ANTIC compliance / their HIPAA compliance → legal difference

But long-term, yes it's a real threat.

---

**Q16 — Are there legal or regulatory risks?**

Three risk zones:
- **Law 2024/017** — we process data indirectly linked to patients. Must be compliant.
- **Civil liability** — if RansomGuard misses an attack, can we be sued? Requires clear liability limitation clause in contract.
- **Cryptography export** — solution uses AES-256. No problem in Cameroon, but to verify for export.

---

### Section E — The Technical

**Q17 — Why not a cloud-first approach like everyone else?**

Because Cameroonian hospitals have:
- Unstable internet (daily outages)
- Limited bandwidth (often 2-10 Mbps shared)
- Legal restrictions on patient data export (Law 2024/017)

Cloud-first = dead product in Africa.

---

**Q18 — Why Windows and not Linux?**

Because 95% of Cameroonian hospital endpoints run on Windows, with a significant portion on Windows 10/11 Pro/Enterprise (and some legacy Windows 7 coupled with non-replaceable medical equipment).

Linux server-side only.

---

**Q19 — Why C# for the agent and not C++ or Rust?**

Three reasons:
- Maximum productivity for a solo developer
- Native access to critical Windows APIs (ETW, WMI)
- Trivial Microsoft code signing

C++ and Rust would have taken 6 additional months of development for the same thing.

---

**Q20 — How to ensure we don't become the next SolarWinds?**

Three protections:
- Mandatory Ed25519 signed updates
- Canary deployment (1% → 10% → 50% → 100%)
- Reproducible build published (a hospital can verify the binary matches the source code)

No one does this in Africa. It's a sales argument.

---

**Q21 — How many false positives maximum is acceptable?**

Strict target: **less than 1 false positive per endpoint per month.**

Beyond that, the IT technician disables RansomGuard for peace. That's the silent death of the product.

That's why the 3-converging-signals rule before isolation is non-negotiable.

---

**Q22 — How to handle a false positive on the main HMS software?**

Whitelist of critical medical processes hard-coded by default. HMS can never be automatically isolated. Only alert to technician.

It's less secure in theory, more useful in practice. Assumed choice.

---

**Q23 — What happens if our cloud is down for 24h?**

Nothing. Agents continue protecting with their latest signatures. Local server keeps running. No loss of protection.

That's the offline-first architecture advantage.

---

### Section F — The Go-to-Market

**Q24 — How to sell to a hospital that has never paid for cybersecurity?**

Four angles:
- **Mandatory legal compliance from June 2026** (Law 2024/017)
- **Cost comparison**: 850,000 FCFA/year vs 30M FCFA in case of attack
- **Free 30-day trial period** to remove purchase friction
- **First pilot hospital testimonial** for social validation

---

**Q25 — Who is the first customer to win?**

A medium private hospital (50-100 endpoints) with a young, tech-savvy medical director. Not a large public hospital (18-month purchase cycle). Not a small rural center (budget too tight).

Likely targets in Yaoundé: Hôpital de la Caisse, Polyclinique Bonneko, Clinique de l'Aéroport.

---

**Q26 — How many hospitals to break-even?**

With monthly operating cost of 200,000 FCFA (hosting + certificates + marginal support), approximately 5 medium hospitals on license to reach operational profitability.

10 hospitals to generate a founder salary.

---

### Section G — The Long Term

**Q27 — What is the 3-year vision?**

Become the de facto cybersecurity standard for hospitals in Francophone Africa. 100+ customer hospitals, formal partnership with ANTIC and Cameroon Ministry of Health, expansion to Côte d'Ivoire, Senegal, Gabon, DRC.

---

**Q28 — How to evolve beyond ransomware?**

Three natural extensions after stabilizing the core product:
- Email anti-phishing module (reuses existing stack)
- Medical IoT security module (connected equipment)
- Managed SOC service for large hospitals

---

**Q29 — What is the possible exit scenario?**

Three scenarios:
- **Acquisition by a global publisher** wanting to enter Francophone Africa
- **Acquisition by an African healthtech player** (Helium Health, Sobrus, etc.)
- **Organic growth** toward a profitable SME

No VC fundraising necessary in organic scenario.

---

**Q30 — What is the most important thing to protect for product success?**

**The reputation of the first customer.**

If the pilot Hospital has a single incident where RansomGuard misses an attack, the product is dead in the entire Cameroonian medical community (which is small, everyone knows each other).

Hence the absolute obligation to over-invest in quality before growth.

---

## END OF PR/FAQ DOCUMENT

This document forced clarity from the customer perspective before any architectural decision. If any of these 30 questions remained unsatisfactorily answered, no line of code would have been written.
