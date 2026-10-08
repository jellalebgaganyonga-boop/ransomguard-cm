# RansomGuard-CM — Discovery Phase, Day 2

**Document Type**: User Personas + Empathy Maps + User Journey Map
**Phase**: Discovery (Master Plan Phase 1)
**Status**: Authoritative
**Author Role**: UX Researcher (solo)
**Methodology Sources**:
- Alan Cooper — *The Inmates Are Running the Asylum* (1999), persona archetype
- Nielsen Norman Group — *Personas Make Users Memorable for Product Team Members* (Harley, 2015)
- Dave Gray — *Gamestorming* (2010), Empathy Map original
- XPLANE / Dave Gray — Empathy Map Canvas v2 (2017)
- Jeff Patton — *User Story Mapping* (2014)

---

## Critical Engineering Rule (Crue)

> **The personas below — "Dr. Amani Nkomo", "Marc Tchoumi", "Sister Jeanne Bilo'o" — are DESIGN ARTEFACTS ONLY. They MUST NEVER appear in source code, database queries, API responses, UI strings, comments, or git commit messages. The system stores only ROLE CODES (`tenant_admin`, `security_analyst`, `read_only_auditor`).**

---

## Table of Contents

1. [Persona 1 — Dr. Amani Nkomo (tenant_admin)](#1-persona-1--dr-amani-nkomo-tenant_admin)
2. [Persona 2 — Marc Tchoumi (security_analyst)](#2-persona-2--marc-tchoumi-security_analyst)
3. [Persona 3 — Sister Jeanne Bilo'o (read_only_auditor)](#3-persona-3--sister-jeanne-biloo-read_only_auditor)
4. [Empathy Maps (XPLANE Canvas v2)](#4-empathy-maps-xplane-canvas-v2)
5. [User Journey Map — Incident Triage End-to-End](#5-user-journey-map--incident-triage-end-to-end)

---

## 1. Persona 1 — Dr. Amani Nkomo (tenant_admin)

### Identity Card

```
┌──────────────────────────────────────────────────────────────┐
│  DR. AMANI NKOMO                                              │
│  Directrice Médicale Adjointe et Responsable IT               │
│  Hôpital Régional de Yaoundé                                  │
├──────────────────────────────────────────────────────────────┤
│  Age                   : 48 ans                                │
│  Formation             : Doctorat Médecine + MBA Santé        │
│                          Publique (CESAG Dakar, 2014)         │
│  Expérience IT         : 7/10 (utilisatrice avancée non-tech) │
│  Cybersécurité         : 4/10 (concepts, pas opérationnel)    │
│  Langue principale     : Français                              │
│  Anglais               : Lecture professionnelle              │
│  Connexion typique     : 2–4 fois/semaine, 5–15 min/session   │
│  Device principal      : Laptop Dell Latitude (Chrome)        │
│  Device secondaire     : Smartphone Samsung (alertes SMS)     │
│  Mobilité              : Réunions cliniques + comité direction │
│  Style décisionnel     : Données → Discussion → Décision      │
└──────────────────────────────────────────────────────────────┘
```

### Background Narrative

Amani a rejoint l'Hôpital Régional de Yaoundé en 2016 après 8 ans de pratique en pédiatrie au CHU de Yaoundé. Sa double formation médecine + management l'a propulsée au Comité de Direction en 2019, où le Directeur Général lui a confié la supervision de l'informatique hospitalière en plus de ses responsabilités cliniques.

Elle n'est pas une informaticienne. Elle est une **médecin-gestionnaire** qui doit prendre des décisions de cybersécurité sans avoir le temps ni la formation pour entrer dans les détails techniques. Quand un incident se produit, elle veut une réponse claire en moins de 30 secondes : *"Est-ce que les soins aux patients sont menacés ? Oui ou non ? Quelles actions immédiates ?"*

Depuis l'incident Ascoma (mars 2025, où 1.5 TB ont été exfiltrés y compris le dossier SANTE), elle est sous pression du Conseil d'Administration pour produire trimestriellement un rapport démontrant la conformité ANTIC. Avant RansomGuard-CM, ce rapport prenait 2 semaines de mobilisation de son équipe IT. Elle veut le réduire à 2 heures.

### Goals (Hierarchical, per Cooper)

**End Goals (what success looks like for her):**
1. Protéger les patients en garantissant la disponibilité des systèmes cliniques 24/7.
2. Préserver la réputation de l'hôpital (pas de fuite médiatique d'incident).
3. Survivre à l'inspection ANTIC trimestrielle sans non-conformité.

**Experience Goals (how she wants to feel):**
1. Confiante dans sa capacité à parler de cybersécurité au Conseil d'Administration.
2. Sereine la nuit (sans crainte d'un appel d'urgence à 3h du matin).
3. Respectée par ses pairs médicaux comme une dirigeante moderne.

**Life Goals (long-term aspirations):**
1. Devenir Directrice Générale d'un grand établissement hospitalier régional ou national.
2. Contribuer à la modernisation du système de santé camerounais.

### Frustrations

1. *"Mon équipe IT me parle en jargon technique. Je veux des décisions, pas des acronymes."*
2. *"Le rapport ANTIC trimestriel mobilise 3 personnes pendant 10 jours. C'est inacceptable."*
3. *"Je n'ai aucune visibilité claire sur l'état de notre sécurité informatique. Je découvre les problèmes quand ils explosent."*
4. *"Les fournisseurs IT internationaux nous facturent en USD et leurs interfaces sont en anglais. Mes équipes ne s'en servent pas."*

### Behaviors & Workflows

- Consulte les alertes critiques par **email institutionnel** (jamais d'app dédiée si évitable).
- Délègue 90% des tâches IT à son équipe technique (3 personnes).
- Garde son **smartphone à portée 24/7** pour les urgences cliniques ET cybersécurité.
- Lit les newsletters santé-publique mais pas les blogs cybersécurité.
- Préfère les graphes simples et les big numbers aux tableaux denses.

### Technology Profile

- Suite Microsoft Office 365 (Word, Excel, Outlook, Teams).
- WhatsApp pour communication informelle inter-direction.
- Système d'Information Hospitalier (SIH) — utilisation occasionnelle pour stats globales.
- Navigateur Chrome principal, parfois Edge.
- Pas de connaissance Linux ni terminal.

### Quote

> *"Je ne suis pas une informaticienne. Je suis responsable de patients. Donnez-moi un outil qui me dit ce que je dois savoir, pas tout ce qu'il y a à savoir."*

### Use of RansomGuard-CM

| Action | Frequency | Time per session |
|---|---|---|
| Consult executive dashboard | 2-4x/week | 3-5 min |
| Sign quarterly ANTIC compliance report | 4x/year | 15-30 min |
| Manage user accounts (add/disable IT staff) | 5-10x/year | 5-10 min |
| Review critical incident detail | On alert | 5-15 min |
| Approve high-impact response action | Rare (1-2x/year) | 30+ min |

---

## 2. Persona 2 — Marc Tchoumi (security_analyst)

### Identity Card

```
┌──────────────────────────────────────────────────────────────┐
│  MARC TCHOUMI                                                 │
│  Technicien Supérieur Informatique — Sécurité & Réseau         │
│  Hôpital Régional de Yaoundé                                   │
├──────────────────────────────────────────────────────────────┤
│  Age                   : 31 ans                                │
│  Formation             : Licence Pro Réseaux & Télécoms       │
│                          (IUT de Douala, 2017)                │
│                          + Certifications CCNA, Sec+ (en cours)│
│  Expérience IT         : 9/10 (généraliste senior)             │
│  Cybersécurité         : 6/10 (autodidacte intensif)           │
│  Langue principale     : Français + Camfranglais               │
│  Anglais               : Documentation technique seulement     │
│  Connexion typique     : 8h/jour ouvré (poste principal)       │
│  Device principal      : PC HP ProDesk (Chrome + Edge)         │
│  Device secondaire     : Laptop personnel pour on-call         │
│  Mobilité              : Bureau IT central + tournée services  │
│  Style décisionnel     : Investiguer → Agir → Documenter       │
└──────────────────────────────────────────────────────────────┘
```

### Background Narrative

Marc est l'un des trois techniciens IT de l'hôpital. Recruté en 2019 à 25 ans, il s'est rapidement spécialisé en sécurité réseau parce que **personne d'autre dans l'équipe ne voulait s'y mettre**. Il a passé ses week-ends à apprendre via OpenClassroom, YouTube (Network Chuck, John Hammond), et la documentation officielle.

Aujourd'hui il est de facto **le SOC analyst de l'hôpital** — sauf qu'il n'a aucune équipe SOC derrière lui. Il est aussi celui qui dépanne les imprimantes, configure les VLANs, déploie les postes de travail dans les services, et répond aux questions des médecins sur leurs problèmes Outlook.

Quand un ransomware frappe l'hôpital, **c'est lui qui sera en première ligne**. Il le sait. C'est pour ça qu'il a poussé pour qu'on déploie RansomGuard-CM. Il veut un outil qui :
- Lui donne une visibilité opérationnelle en temps réel.
- Lui permet d'agir en quelques clics (isoler, terminer un processus).
- Documente automatiquement ses actions pour le rapport ANTIC.

Il parle français au quotidien avec un fort mélange Camfranglais entre collègues. Pour la documentation technique, il bascule sur l'anglais sans difficulté. Mais l'**interface utilisateur quotidienne doit être en français**.

### Goals (Hierarchical)

**End Goals:**
1. Détecter un incident avant qu'il ne touche les systèmes cliniques.
2. Contenir un incident en moins de 5 minutes.
3. Construire un dossier forensique solide pour chaque incident traité.

**Experience Goals:**
1. Se sentir équipé pour faire face à n'importe quelle attaque.
2. Être reconnu professionnellement comme un expert sécurité (pas juste "le gars qui dépanne").
3. Apprendre continuellement (chaque incident = montée en compétence).

**Life Goals:**
1. Devenir CISO d'une grande organisation ou consultant indépendant.
2. Passer l'OSCP d'ici 2 ans.
3. Lancer une chaîne YouTube/blog cybersécurité francophone Afrique.

### Frustrations

1. *"Je passe 60% de mon temps à dépanner des conneries bureautiques au lieu de faire de la sécurité."*
2. *"Les outils EDR du marché sont conçus pour des SOC à 20 personnes. Je suis seul."*
3. *"Quand je trouve un incident, je dois écrire le rapport à la main parce qu'aucun outil ne le génère correctement pour l'ANTIC."*
4. *"Les fournisseurs internationaux ne répondent jamais à mes tickets en moins de 48h. Et leur support est en anglais."*

### Behaviors & Workflows

- **Première chose le matin :** ouvre la console SOC, scrolle les alertes overnight, trie par sévérité.
- **Triage instinct :** lit les 5 premières alertes critiques, décide en 30 secondes si c'est un vrai positif.
- **Réponse incident :** isole d'abord, investigue ensuite, documente toujours.
- **Veille technique :** RSS feeds (BleepingComputer, The DFIR Report), Twitter (suit ~50 chercheurs sécurité), Telegram (groupes francophones).
- **Communication :** WhatsApp pour les urgences (groupe IT-Direction), email pour les rapports formels, Teams pour réunions.

### Technology Profile

- Console RansomGuard-CM (à venir Sprint 7).
- Wireshark, nmap, tcpdump, Sysinternals Suite.
- Excel pour la gestion d'inventaire endpoint.
- VS Code avec extensions PowerShell et Python.
- Git CLI (utilise GitHub pour ses scripts personnels).
- Ligne de commande Windows + Bash (WSL).
- Connait Docker, débute en Kubernetes.

### Quote

> *"J'ai pas besoin qu'on me prenne la main. J'ai besoin d'un outil qui me donne les bonnes informations au bon moment, et qui me laisse faire mon travail."*

### Use of RansomGuard-CM

| Action | Frequency | Time per session |
|---|---|---|
| Monitor alert queue | Continuous (8h/day) | All day |
| Triage individual alert | 5-20x/day | 2-10 min each |
| Isolate endpoint (containment) | 0-5x/week | 1-3 min |
| Investigate process tree forensics | 1-5x/week | 15-60 min |
| Deploy agent on new endpoint | 1-3x/week | 5-10 min |
| Update detection policies | 1-2x/month | 30-60 min |

---

## 3. Persona 3 — Sister Jeanne Bilo'o (read_only_auditor)

### Identity Card

```
┌──────────────────────────────────────────────────────────────┐
│  SISTER JEANNE BILO'O                                         │
│  Inspectrice Régionale Santé Publique                          │
│  Délégation Régionale Santé — Centre, Yaoundé                  │
├──────────────────────────────────────────────────────────────┤
│  Age                   : 54 ans                                │
│  Formation             : Doctorat Médecine + Master Santé Pub. │
│                          (Université de Yaoundé I, 2002)       │
│  Expérience IT         : 4/10 (utilisatrice basique)            │
│  Cybersécurité         : 2/10 (terminologie seulement)          │
│  Langue principale     : Français                              │
│  Anglais               : Très limité                            │
│  Connexion typique     : 1-2x/mois (audits planifiés)           │
│  Device principal      : Laptop Lenovo gouvernemental (Edge)    │
│  Mobilité              : Bureau Délégation + visites terrain    │
│  Style décisionnel     : Méthodique, processus formels         │
└──────────────────────────────────────────────────────────────┘
```

### Background Narrative

Sister Jeanne (Sœur Jeanne dans l'usage local — elle vient d'un fond catholique mais c'est aussi un titre de respect dans le milieu médical camerounais pour une consœur senior) supervise les 8 hôpitaux publics et confessionnels de la région Centre. Elle est inspectrice depuis 12 ans, après une carrière de chef de service en médecine générale.

Son rôle dans RansomGuard-CM : **consulter les rapports de conformité, vérifier l'intégrité des journaux d'audit, et rédiger son rapport mensuel pour le Ministre de la Santé Publique** (et trimestriellement pour ANTIC).

Elle n'a aucune compétence technique. Elle a besoin de :
- PDFs **clairs et formels**, en français impeccable.
- Données **fiables et vérifiables** (signature cryptographique = elle ne sait pas ce que c'est techniquement, mais elle sait que c'est important).
- Format **compatible Word / Outlook** pour pouvoir copier-coller dans ses rapports ministériels.

Elle visite physiquement chaque hôpital tous les 3 mois. Lors de ces visites, elle veut pouvoir :
- Demander au Directeur Médical (Dr. Amani) le rapport trimestriel.
- Vérifier que les chiffres correspondent.
- Sauvegarder une copie sur son laptop pour son archive.

### Goals (Hierarchical)

**End Goals:**
1. Garantir la conformité réglementaire des hôpitaux supervisés.
2. Produire des rapports fiables et incontestables au Ministre.
3. Identifier les hôpitaux à risque avant qu'un incident majeur ne survienne.

**Experience Goals:**
1. Se sentir efficace malgré ses limitations techniques.
2. Maintenir la dignité et l'autorité de sa fonction d'inspectrice.
3. Ne pas dépendre du personnel IT de l'hôpital pour faire son travail.

**Life Goals:**
1. Servir l'État jusqu'à la retraite avec un bilan respecté.
2. Voir le système de santé camerounais se moderniser tout en restant accessible.

### Frustrations

1. *"Les systèmes informatiques modernes sont compliqués. J'ai besoin de quelque chose qui marche en 3 clics."*
2. *"Quand je demande un rapport à un hôpital, je dois attendre des jours. Je veux pouvoir le télécharger moi-même."*
3. *"Les rapports que je reçois ne sont pas standards. Chaque hôpital a son format. C'est impossible à comparer."*
4. *"On me parle de 'cybersécurité', 'EDR', 'ransomware'. Je veux savoir : est-ce que les patients sont en sécurité, oui ou non ?"*

### Behaviors & Workflows

- Planifie ses audits 3 semaines à l'avance par email officiel.
- Arrive avec son laptop, demande accès Wi-Fi temporaire, télécharge les rapports.
- Vérifie quelques métriques clés (nombre d'incidents, statut compliance, dernière mise à jour).
- Rédige son rapport ministériel sur Word (template officiel ministère).
- Archive tout sur clé USB chiffrée + Google Drive personnel.

### Technology Profile

- Suite Microsoft Office (Word, Excel, Outlook).
- Edge comme navigateur (imposé par la DG informatique de l'État).
- Pas d'usage cloud public (à part Drive personnel).
- Pas de connaissance Linux ni terminal.
- Smartphone Android gouvernemental.

### Quote

> *"Donnez-moi un PDF clair, en français, daté et signé. Je n'ai pas besoin de comprendre comment c'est fait. Je dois pouvoir le présenter au Ministre sans avoir peur qu'on me le conteste."*

### Use of RansomGuard-CM

| Action | Frequency | Time per session |
|---|---|---|
| Download compliance report | 1-2x/month per hospital | 2-5 min |
| Compare metrics across hospitals (future) | Quarterly | 15-30 min |
| Verify hash-chain integrity (future) | On suspicion | 5-10 min |
| Read individual incident summary | Rare (on escalation) | 5-15 min |

---

## 4. Empathy Maps (XPLANE Canvas v2)

Per Dave Gray's XPLANE v2 Empathy Map Canvas, each persona is mapped across 7 quadrants:
1. **GOAL** (top center) — Who is the user? What do they need to do?
2. **SEE** (right-upper) — What do they see in their environment?
3. **SAY** (right-middle) — What might they say?
4. **DO** (right-lower) — What might they do?
5. **HEAR** (left-upper) — What do they hear from others?
6. **THINK & FEEL** (bottom) — Pains (left) + Gains (right).

### Empathy Map 1 — Dr. Amani Nkomo (tenant_admin)

```
┌─────────────────────────────────────────────────────────────────────┐
│                              GOAL                                    │
│  Who: Hospital Director / IT Sponsor                                 │
│  Need to do: Govern security without becoming a technician           │
└─────────────────────────────────────────────────────────────────────┘
┌──────────────────────────┬──────────────────────────────────────────┐
│  HEAR                    │  SEE                                      │
│  - Board demanding ROI    │  - Other hospitals on TV (incidents)      │
│  - "We were attacked"     │  - ANTIC inspectors                        │
│    (other hospitals)      │  - Her IT team busy/stressed              │
│  - Ministry compliance    │  - Dashboard alerts on her laptop         │
│    requirements           │  - News of CNPS/Ascoma attacks            │
│  - IT team escalations    │                                            │
├──────────────────────────┼──────────────────────────────────────────┤
│  SAY                     │  DO                                        │
│  "Are we protected?"      │  - Reads dashboard 2-4x/week              │
│  "How does this compare   │  - Signs ANTIC report quarterly           │
│   to last quarter?"       │  - Attends Board meeting monthly          │
│  "Can we afford this?"    │  - Manages users (add/disable) 5-10x/yr   │
│  "Who has access to this  │  - Forwards critical alerts to DG         │
│   data?"                  │  - Delegates 90% of IT to her team        │
├──────────────────────────┴──────────────────────────────────────────┤
│  THINK & FEEL                                                         │
│  ─ PAINS ──────────────────  │  ─ GAINS ─────────────────────────    │
│  - Anxiety about being       │  - Confidence at Board                 │
│    blamed for an incident    │  - Career advancement (DG track)        │
│  - Frustration with jargon   │  - Professional respect                 │
│  - Time pressure during      │  - Sleep without fear of 3am calls      │
│    ANTIC inspections         │  - Tangible ROI to defend budget        │
│  - Helplessness when an      │  - Be a modern healthcare leader        │
│    incident hits and she     │  - Patient safety (her core mission)    │
│    doesn't know what to do   │                                          │
└─────────────────────────────────────────────────────────────────────┘
```

### Empathy Map 2 — Marc Tchoumi (security_analyst)

```
┌─────────────────────────────────────────────────────────────────────┐
│                              GOAL                                    │
│  Who: De facto SOC Analyst (solo)                                    │
│  Need to do: Detect, triage, contain — fast and documented           │
└─────────────────────────────────────────────────────────────────────┘
┌──────────────────────────┬──────────────────────────────────────────┐
│  HEAR                    │  SEE                                      │
│  - Doctors complaining    │  - 24/7 alert console                      │
│    about slow Wi-Fi       │  - Endpoint inventory dashboards          │
│  - Direction demanding    │  - Process trees & forensic graphs        │
│    incident reports        │  - SSH terminals, log files               │
│  - Twitter chatter on     │  - Twitter threads on new TTPs            │
│    new ransomware variants│  - YouTube tutorials (Network Chuck)      │
│  - Help-desk tickets       │  - GitHub Issues (his own scripts)        │
├──────────────────────────┼──────────────────────────────────────────┤
│  SAY                     │  DO                                        │
│  "Pourquoi cette alerte   │  - Triages 5-20 alerts/day                │
│   est mauvaise ?"         │  - Isolates 0-5 endpoints/week            │
│  "Y a moyen de filtrer    │  - Investigates 1-5 incidents/week        │
│   par sévérité ?"         │  - Deploys agents on new PCs              │
│  "Comment l'exporter ?"   │  - Writes Python automation scripts       │
│  "C'est documenté où ?"   │  - Studies for OSCP at evenings           │
├──────────────────────────┴──────────────────────────────────────────┤
│  THINK & FEEL                                                         │
│  ─ PAINS ──────────────────  │  ─ GAINS ─────────────────────────    │
│  - 60% of time on bureauti-  │  - Career growth (CISO ambition)        │
│    cs, not security          │  - Recognition as security expert       │
│  - Alone facing potential    │  - OSCP / OSWE certifications           │
│    APT-level attackers       │  - Tools that actually work for solo    │
│  - Fear of missing a real    │  - Auto-generated forensic reports      │
│    incident in the noise     │  - Bilingual UX (Camfranglais native)   │
│  - Frustration with bad UX   │  - Keyboard-driven, fast workflows      │
│  - 48h+ vendor support       │  - Local community / mentorship         │
└─────────────────────────────────────────────────────────────────────┘
```

### Empathy Map 3 — Sister Jeanne Bilo'o (read_only_auditor)

```
┌─────────────────────────────────────────────────────────────────────┐
│                              GOAL                                    │
│  Who: Regional Health Inspector                                      │
│  Need to do: Verify compliance, produce ministerial reports          │
└─────────────────────────────────────────────────────────────────────┘
┌──────────────────────────┬──────────────────────────────────────────┐
│  HEAR                    │  SEE                                      │
│  - Ministry directives    │  - Word templates from Ministry            │
│  - Hospital directors      │  - PDF reports from each hospital         │
│    explaining their setup │  - Email inbox (Outlook)                  │
│  - Pressure to identify   │  - Excel comparison spreadsheets           │
│    at-risk facilities      │  - Government Edge browser                 │
│  - Stories from other     │  - Compliance frameworks (Loi 2024/017)    │
│    inspectors              │                                            │
├──────────────────────────┼──────────────────────────────────────────┤
│  SAY                     │  DO                                        │
│  "Pouvez-vous me donner   │  - Plans audit 3 weeks ahead              │
│   le rapport trimestriel ?"│  - Visits each hospital quarterly         │
│  "Est-ce signé ?"         │  - Downloads PDF reports                  │
│  "Est-ce comparable au    │  - Compares across hospitals on Excel     │
│   trimestre dernier ?"    │  - Writes monthly Ministry report         │
│  "Comment puis-je vérifier│  - Archives on encrypted USB              │
│   que les chiffres sont   │  - Reports to Ministry quarterly          │
│   exacts ?"               │                                            │
├──────────────────────────┴──────────────────────────────────────────┤
│  THINK & FEEL                                                         │
│  ─ PAINS ──────────────────  │  ─ GAINS ─────────────────────────    │
│  - Inconsistent report       │  - Standardized PDFs across hospitals   │
│    formats from hospitals    │  - Self-service download (no dependency)│
│  - Dependency on IT staff    │  - Tamper-evident audit trail           │
│    for data extraction       │  - Trust in the numbers                  │
│  - Technical jargon she      │  - Respect from Ministry                 │
│    cannot evaluate           │  - Professional dignity preserved         │
│  - Fear of signing a report  │  - Career legacy (modernizing oversight) │
│    that turns out wrong      │                                          │
│  - Long wait times for data  │                                          │
└─────────────────────────────────────────────────────────────────────┘
```

---

## 5. User Journey Map — Incident Triage End-to-End

### Scope

This journey map follows a **single ransomware-related incident** from the moment the agent detects suspicious behavior to the moment the audit log is reviewed by the inspector. It traces actions, emotions, touchpoints, pain points, and opportunities **across all three personas**.

Per Jeff Patton (*User Story Mapping*, 2014): a journey map reveals where the user's experience breaks across persona boundaries — exactly where senior PM/UX work creates the most value.

### Scenario

> **Tuesday, 14:23 — A nurse in the Pediatrics ward inserts a USB stick into a shared workstation to print a discharge form. The USB contains a ransomware payload (LockBit variant). The RansomGuard-CM agent's USB GUARD module detects suspicious file enumeration. SENTINEL fires when the first canary file is touched. GENEALOGY traces the parent process to `cmd.exe` → `powershell.exe` (encoded payload). ENTROPY climbs above 7.8 on 12 files in 4 seconds.**

---

### Phase 1 — Detection (T+0s to T+8s)

| Touchpoint | Actor | Action | Emotion | Pain Point | Opportunity |
|---|---|---|---|---|---|
| Agent USB GUARD | System (no human) | USB enumeration logged | — | — | — |
| Agent SENTINEL | System | Canary file touched, severity HIGH | — | — | — |
| Agent GENEALOGY | System | Parent process tree captured | — | — | — |
| Agent ENTROPY | System | 12 files flagged as high entropy | — | — | — |
| GRID Server | System | Alert correlated, severity CRITICAL | — | — | — |
| Notification fire | System | SMS + Email + Dashboard push | — | Latency to reach analyst | Push notification UX |

---

### Phase 2 — Triage (T+8s to T+90s)

| Touchpoint | Actor | Action | Emotion | Pain Point | Opportunity |
|---|---|---|---|---|---|
| SMS arrives | **Marc Tchoumi** | Sees alert on phone | 😰 Anxiety spike | Format unclear on small screen | Mobile-optimized SMS template |
| Marc opens laptop | Marc | Logs into console | 😟 Concentrated | Login takes 4-6 sec | Single-sign-on with badge |
| Marc lands on dashboard | Marc | Sees 1 critical, status red | 😬 Alert | Visual hierarchy unclear | Priority score (Defender-style) |
| Marc clicks alert | Marc | Opens incident detail | 😐 Focused | Loading > 2s | Skeleton screens |
| Marc reads narrative | Marc | Understands TTP (USB + powershell + entropy) | 🤔 Decisive | Narrative dense | Inverted pyramid structure |
| Marc checks endpoint | Marc | Identifies workstation `PEDIATRIE-02` | 😐 Methodical | Hostname-only, no service mapping | Endpoint enrichment |
| Marc decides: ISOLATE | Marc | Clicks Isolate | 😤 Resolved | Confirmation modal too verbose | Streamlined confirm (1 question) |
| Confirmation modal | Marc | Types reason | 😐 Procedural | Mandatory note slows him down | Auto-fill from incident summary |
| Action submitted | Marc | Sees "Command queued" | 😌 Relief partial | Pending state ambiguous | Real-time agent acknowledgment |

---

### Phase 3 — Containment (T+90s to T+5min)

| Touchpoint | Actor | Action | Emotion | Pain Point | Opportunity |
|---|---|---|---|---|---|
| Agent receives command | System (next heartbeat) | Isolation policy applied | — | Heartbeat interval (30s) | WebSocket push |
| Endpoint isolated | System | Network blocked except mgmt | — | — | — |
| Status updates | Marc | Sees status "Isolated" | 😅 Relieved | Auto-refresh not real-time | Live status badge |
| Marc calls Pediatrie | Marc | Tells nurse to stop touching | 😐 Communicating | No in-app notify-nurse capability | Workflow integration |
| Marc opens forensics | Marc | Reviews process tree | 🤔 Curious | Tree dense, no zoom | Interactive visualization (D3) |
| Marc identifies payload | Marc | Names "LockBit variant" | 😎 Confident | No IOC lookup | Threat intel enrichment |

---

### Phase 4 — Escalation (T+5min to T+30min)

| Touchpoint | Actor | Action | Emotion | Pain Point | Opportunity |
|---|---|---|---|---|---|
| Marc decides escalate | Marc | Calls Dr. Amani | 😐 Professional | Phone-only escalation | In-app escalate-to-admin |
| Dr. Amani receives call | **Dr. Amani** | Listens, asks questions | 😟 Concerned | She has no visibility yet | Real-time push to admin |
| Amani opens dashboard | Amani | Sees executive view | 😬 Tense | KPIs not contextualized to event | Event-aware dashboard |
| Amani reads narrative | Amani | Tries to understand impact | 😕 Confused | Technical narrative for executive | Executive summary mode |
| Amani asks: "What does this mean for patients?" | Amani | Calls back Marc | 😟 Anxious | No "business impact" translator | LLM-generated executive brief |
| Marc explains | Marc | Speaks plain French | 😐 Patient | Translation effort | Bilingual technical-to-executive |
| Amani decides | Amani | Approves containment | 😌 Decisive | Approval requires phone call | In-app approval workflow |
| Amani notifies DG | Amani | Calls Director General | 😟 Formal | Manual notification | Auto-notify per playbook |

---

### Phase 5 — Documentation (T+30min to T+2h)

| Touchpoint | Actor | Action | Emotion | Pain Point | Opportunity |
|---|---|---|---|---|---|
| Marc updates incident | Marc | Adds resolution notes | 😐 Methodical | Form fields not pre-filled | Smart defaults from incident data |
| Marc closes incident | Marc | Status → "Closed - True positive contained" | 😌 Accomplished | — | — |
| Audit log auto-created | System | Ed25519 signed entry | — | — | — |
| Marc generates report | Marc | Exports PDF | 😐 Patient | PDF generation 8-15s | Async generation + notification |
| PDF downloaded | Marc | Saves to NAS archive | 😌 Done | No auto-archive | Automatic compliance archive |
| End of shift | Marc | Hands off to evening staff | 😴 Tired | Verbal handoff only | Daily incident digest |

---

### Phase 6 — Compliance Review (T+1 month)

| Touchpoint | Actor | Action | Emotion | Pain Point | Opportunity |
|---|---|---|---|---|---|
| Inspector schedules audit | **Sister Jeanne** | Sends email to hospital | 😐 Routine | 3-week lead time | Self-service inspector portal |
| Inspector arrives | Jeanne | Connects to Wi-Fi guest | 😐 Professional | Wi-Fi onboarding friction | Inspector-mode SSO |
| Jeanne logs in | Jeanne | Lands on read-only dashboard | 😌 Reassured | — (Kaspersky pattern) | — |
| Jeanne downloads report | Jeanne | Quarterly compliance PDF | 😌 Confident | Report format consistent | — |
| Jeanne verifies hash | Jeanne | Clicks "Verify integrity" | 🤔 Curious | Crypto jargon | Plain-language verification |
| Jeanne signs report (sealed) | Jeanne | Adds her signature in Word | 😐 Routine | Manual signature | Digital signature workflow |
| Jeanne archives | Jeanne | USB + Drive | 😌 Done | — | — |
| Jeanne reports to Ministry | Jeanne | Submits monthly report | 😐 Professional | Manual compile from 8 hospitals | Cross-hospital aggregation view |

---

### Journey Synthesis — Critical Insights

#### Top 5 friction points (across the journey)

1. **Phase 2** — Triage cognitive load: Marc must mentally synthesize 6+ data points to decide isolation. Defender XDR's 0-100 priority score addresses this.
2. **Phase 4** — Executive-technician translation: Amani needs a non-technical summary; Marc speaks technical. The system should auto-translate.
3. **Phase 3** — Real-time visibility: 30-second heartbeat lag during active containment is dangerous. WebSocket push is justified.
4. **Phase 5** — Documentation overhead: Marc writes reports manually after intense incidents. Auto-population would save him 15-30 min per incident.
5. **Phase 6** — Cross-hospital aggregation: Jeanne compiles 8 hospital reports manually. Multi-tenant inspector view is a Sprint 8+ priority.

#### Top 5 emotional inflection points

1. **T+8s (Marc reads SMS)**: Anxiety spike → UX must reduce time-to-understanding.
2. **T+90s (Marc confirms isolation)**: Anxiety → Relief. Must NOT lose this transition to ambiguous pending states.
3. **T+5min (Amani receives call)**: Anxiety → Confused. Must provide a clear "what does this mean for patients" answer.
4. **T+30min (Amani approves)**: Confused → Decisive. Must surface confidence-building data.
5. **T+1month (Jeanne verifies hash)**: Routine → Curious. Translate cryptography into trust-building language.

#### Top 5 design opportunities (Sprint 7 priority)

1. **Defender-style 0-100 priority score** (drives Marc's triage speed).
2. **Executive-mode narrative summary** (auto-generated, drives Amani's confidence).
3. **Real-time status updates** via polling (WebSocket deferred to Sprint 8).
4. **Confirmation modal streamlining** (one question, default-filled reason).
5. **Plain-language compliance verification** (Jeanne's trust building).

---

**End of Discovery Day 2 — 3 Personas Formels + Empathy Maps + User Journey Map**

**Next**: Discovery Day 3 — Service Blueprint + Opportunity Solution Tree (OST).
