# RansomGuard-CM — Design Phase, Days 9-10

**Document Type**: Hi-Fi Mockups Specification + Clickable Prototype Spec + Frontend Architecture Decision Records (ADRs)
**Phase**: Design (Master Plan Phase 3) — Closes Design Phase
**Status**: Authoritative
**Author Role**: UI Designer + Frontend Architect (solo)
**Methodology Sources**:
- Michael Nygard — *Documenting Architecture Decisions* (2011), original ADR concept
- Joel Parker Henderson — ADR templates (github.com/joelparkerhenderson/architecture-decision-record)
- Nielsen Norman Group — *Prototyping for Usability Testing*
- Material 3 — Motion specifications (m3.material.io/styles/motion/overview)
- Refactoring UI by Adam Wathan & Steve Schoger (2018)

---

## Table of Contents

1. [Hi-Fi Mockup 1 — Operational Dashboard (security_analyst)](#1-hi-fi-mockup-1--operational-dashboard-security_analyst)
2. [Hi-Fi Mockup 2 — Alert Detail with Inverted-Pyramid Narrative](#2-hi-fi-mockup-2--alert-detail-with-inverted-pyramid-narrative)
3. [Hi-Fi Mockup 3 — Endpoint Isolation Confirmation Flow](#3-hi-fi-mockup-3--endpoint-isolation-confirmation-flow)
4. [Clickable Prototype Specification](#4-clickable-prototype-specification)
5. [Architecture Decision Records (ADRs)](#5-architecture-decision-records-adrs)
6. [Design Phase — Closure & Handoff](#6-design-phase--closure--handoff)

---

## 1. Hi-Fi Mockup 1 — Operational Dashboard (security_analyst)

### Why This Mockup Is Hi-Fi Critical

Per the Discovery Phase OST, the most-impact opportunity is **"reduce analyst triage time"** (analyst#1 O=16). The Operational Dashboard is Marc Tchoumi's primary surface — he spends 8 hours/day here. Getting this screen right with full visual fidelity drives:
- Time-to-meaningful-info < 2s (NFR target)
- Defender XDR priority-score adoption (key copy from industry leader)
- Cognitive load reduction (per Alhamadi et al. 2025 ACM TiiS)

### Hi-Fi Specification

#### Layout Grid

- Total viewport: 1440 × 900 (target desktop)
- Sidebar: 260px fixed left
- Header: 64px fixed top
- Content area: padding `sys.spacing.inset.lg` (24px)
- Content max-width: 1280px centered
- Section gap: `sys.spacing.stack.lg` (24px)

#### Section 1 — Header Bar (64px)

```
Background: sys.color.background.canvas (#FFFFFF)
Border-bottom: 1px solid sys.color.border.subtle (#E4E6EA)
Shadow: ref.shadow.sm
Height: 64px
Padding-X: 24px

LEFT CLUSTER (left-aligned):
├── Logo "🛡 RansomGuard-CM"
│   ├── Font: Inter Semibold 18px
│   ├── Color: sys.color.text.primary
│   └── Icon: 24×24 primary color
└── Tenant Pill (8px gap from logo)
    ├── Background: sys.color.primary.subtle (#ECF2FB)
    ├── Padding: 4px 12px
    ├── Border-radius: ref.radius.full
    ├── Text: "Hôpital Régional de Yaoundé"
    └── Font: sys.typography.caption + medium weight

RIGHT CLUSTER (right-aligned, 16px gaps):
├── Notification Bell
│   ├── Icon: 24×24 sys.color.text.secondary
│   ├── Count badge: 3 (top-right corner)
│   │   ├── Background: sys.color.severity.critical (#D63838)
│   │   ├── Color: white
│   │   ├── Size: 18×18 circle
│   │   └── Font: 11px bold
│   └── Hover: cursor pointer, bg becomes sys.color.interactive.hover.bg
├── Language Switcher
│   ├── Text: "🌐 FR" with chevron down
│   ├── Font: sys.typography.label
│   └── Click: dropdown with "Français | English"
└── User Menu
    ├── Avatar: 32×32 circle
    │   ├── Background: sys.color.primary
    │   ├── Initials "MT" in white
    │   └── Font: 12px bold
    ├── Text: "Marc Tchoumi" + chevron down
    └── Click: dropdown (Profil / Se déconnecter)
```

#### Section 2 — Sidebar (260px)

```
Background: sys.color.surface.default (#FAFBFC)
Border-right: 1px solid sys.color.border.subtle
Padding-Y: 16px
Padding-X: 8px

NAVIGATION ITEMS:
Each item:
├── Height: 40px
├── Padding-X: 16px
├── Padding-Y: 8px
├── Border-radius: ref.radius.md (6px)
├── Gap (icon to text): 12px
├── Font: sys.typography.body.default
└── Color (default): sys.color.text.secondary

ACTIVE ITEM (e.g., "Alertes" when on /alerts):
├── Background: sys.color.interactive.selected.bg (#ECF2FB)
├── Color: sys.color.primary
├── Icon color: sys.color.primary
├── Left border indicator: 3px solid sys.color.primary
└── Font weight: medium

ITEM WITH COUNT BADGE (e.g., "Alertes 3"):
├── Layout: icon + text (left) + badge (right)
├── Badge: same red as notification
└── Badge: positioned 8px from right edge

HOVER STATE:
├── Background: sys.color.interactive.hover.bg
└── Cursor: pointer

DISABLED (RBAC-filtered):
└── Items hidden entirely (NOT shown grayed out)
```

#### Section 3 — Page Header (40px)

```
H1: "Console opérationnelle"
├── Font: sys.typography.heading.1 (31px semibold)
└── Color: sys.color.text.primary

Subtitle line:
├── Text: "⌘K pour la palette de commandes"
├── Font: sys.typography.body.small
└── Color: sys.color.text.tertiary
└── kbd: 4px padding + 1px border + rounded corners
```

#### Section 4 — Agent Health Strip (Card)

```
Background: white card
Border: 1px solid sys.color.border.subtle
Border-radius: ref.radius.lg (8px)
Padding: sys.spacing.inset.lg (24px)
Margin-bottom: sys.spacing.stack.lg (24px)
Shadow: ref.shadow.sm

Layout: 3-column horizontal
├── Online (left)
│   ├── 🟢 Filled green circle (16px)
│   ├── Number: 42 (sys.typography.heading.2 + bold)
│   ├── Label: "actifs" (caption + secondary color)
│   └── Last 1h: ⬆ +2 (trend indicator)
├── Stale (center) — orange
│   ├── 🟡 Filled yellow circle
│   ├── Number: 3
│   └── Label: "obsolètes"
└── Offline (right) — red
    ├── 🔴 Filled red circle
    ├── Number: 2
    └── Label: "hors ligne"
```

#### Section 5 — Real-Time Alert Feed (Card)

```
Card spec same as above.
Internal layout:

Header row:
├── Title left: "FLUX D'ALERTES TEMPS RÉEL"
│   ├── Font: sys.typography.heading.3 (20px semibold)
│   └── Color: sys.color.text.primary
└── Right side: "Actualisation toutes les 15s"
    ├── Subtle spinner icon (8px green dot pulsing)
    ├── Font: caption + tertiary

Filter chips row (below title, 12px gap):
├── [Tous ▼] · [Sévérité ▼] · [Période ▼] · [Module ▼]
├── Each chip: cmp.badge with chevron-down
└── Active filter chip: filled background, removable ×

Divider: 1px solid sys.color.border.subtle

Alert rows (each):
├── Padding: 16px horizontal, 12px vertical
├── Border-bottom: 1px solid sys.color.border.subtle (except last)
├── Hover background: sys.color.interactive.hover.bg
├── Layout (grid):
│   ┌──────┬──────┬──────────────┬──────┬─────────┐
│   │Score │ Sev. │ Host + Module │ Time │ Actions │
│   └──────┴──────┴──────────────┴──────┴─────────┘
└── Cursor pointer

Score column (60px wide):
├── PriorityScore component (40px min-width)
├── Red [92] for critical, orange [74], yellow [38], blue [22]

Severity column (110px wide):
├── SeverityBadge component with icon + label

Body column (flex 1):
├── Line 1: Hostname (mono font 14px) + bullet + Module (caption uppercase)
│   └── Color: sys.color.text.primary
└── Line 2: Summary text (body small)
    └── Color: sys.color.text.secondary

Time column (120px wide):
├── "il y a 14 min" (caption)
└── Color: sys.color.text.tertiary

Actions column (180px wide, right-aligned):
├── [Prendre en charge] secondary button (compact)
└── [Voir détails →] ghost button
```

#### Section 6 — Agent Quick View Card (below feed)

```
Card spec.
Header: "AGENTS — APERÇU RAPIDE"
Right: "Voir l'inventaire →" link

Table (sortable):
├── Columns: Hôte | OS | Heartbeat | État
├── Each cell: sys.typography.body.default
├── Hostname: mono font + bold
├── Status indicator: colored dot (12px) + label
└── Row click: navigate to /agents/:id

Pagination: not shown (just top 7)
Empty state if no agents: not applicable
```

#### Section 7 — Color Application Validation

| Element | Color | Contrast Validation |
|---|---|:-:|
| Page background | white | — |
| Cards | white on gray.99 | ✅ subtle separation |
| Primary text | gray.10 on white | ✅ 21:1 (AAA) |
| Secondary text | gray.40 on white | ✅ 10.4:1 (AAA) |
| Tertiary text | gray.60 on white | ✅ 4.5:1 (AA) |
| Primary button | white on blue.40 | ✅ 5.6:1 (AA) |
| Critical badge | red.50 on red.95 | ✅ 5.1:1 (AA) |
| Score 92 white text | white on red.50 | ✅ 5.0:1 (AA) |
| Active sidebar item | blue.40 on blue.95 | ✅ 5.4:1 (AA) |
| Focus ring | focus on white | ✅ 3.4:1 (AA non-text) |

### Mockup Behavior Annotations

**On load:**
1. Skeleton screens for KPIs (200ms)
2. Skeleton rows in alert feed (200ms)
3. TanStack Query fetches `/dashboard/metrics/summary`
4. TanStack Query fetches `/dashboard/alerts?page_size=20`
5. Replace skeletons with data (no flash)

**On new alert pushed (15s polling):**
1. Refetch `/dashboard/alerts`
2. If new top-priority alert: fade-in row at top (200ms ease-in-out)
3. Update count badge in notification bell (with subtle pulse animation 600ms)
4. If sound on: play subtle "ding" (configurable Sprint 8)

**On row click:**
1. Visual feedback (background flash 100ms)
2. Navigate to `/alerts/:id` (React Router push)
3. Page transition: fade-out 100ms → fade-in 200ms

**On hover row:**
1. Background change to `sys.color.interactive.hover.bg` (100ms transition)
2. Cursor: pointer
3. "Voir détails →" link increases underline opacity

---

## 2. Hi-Fi Mockup 2 — Alert Detail with Inverted-Pyramid Narrative

### Why This Mockup Is Hi-Fi Critical

Per OST SOL A2, the inverted-pyramid narrative is **central to triage speed**. Marc must understand the alert in < 5 seconds of reading. The Alert Detail page is the highest-information surface in the entire application.

### Layout Specification

#### Breadcrumb Row (40px)

```
"← Retour aux alertes"
├── Font: sys.typography.body.small
├── Color: sys.color.text.link
├── Icon: 16×16 arrow-left
└── Click: history.back() OR navigate /alerts
```

#### Header Section (Card, 24px padding)

```
TOP ROW:
├── Alert ID: "Alerte #a1f3b8e2-c3f4-5d6e-7f8a-9b0c1d2e3f4a"
│   ├── Font: sys.typography.code (14px)
│   ├── Color: sys.color.text.tertiary
│   └── ⧉ Copy button (16×16) on right, copies UUID to clipboard

BADGE ROW (8px below ID):
├── PriorityScore [92] (40×24 chip)
├── SeverityBadge "🔴 CRITIQUE" (with icon)
├── StatusBadge "Nouveau" (red background)
└── Right-aligned: "Détecté il y a 14 min" (caption + tertiary)

METADATA ROW (24px below badges):
├── Layout: 2-column grid
├── Left column:
│   ├── Label: "AGENT" (sys.typography.label)
│   └── Value: "PEDIATRIE-02" + bullet + "Service Pédiatrie"
└── Right column:
    ├── Label: "MODULE"
    └── Value: "SENTINEL" (uppercase badge style)
```

#### Action Buttons Section (Card)

```
Background: sys.color.feedback.warning.subtle
Border-left: 4px solid sys.color.feedback.warning
Border-radius: ref.radius.md
Padding: sys.spacing.inset.lg (24px)

Inside:
LEFT CLUSTER:
├── [Prendre en charge] primary button (large)
└── [Marquer FP] secondary button

DIVIDER (small subtle)

CRITICAL ACTION CALL-OUT:
├── ⚠️ icon (24×24, warning color)
├── Text: "Action immédiate suggérée :"
└── [Isoler PEDIATRIE-02 du réseau] danger button (large)
    ├── Background: cmp.button.danger.bg
    ├── Text: white
    ├── Icon: shield-off (left of text)
    └── Pulse animation on shadow (2s loop) to draw attention
```

#### Tabs Bar (48px)

```
TabsList horizontal:
├── Active: "Résumé" (default)
│   ├── Background: white
│   ├── Color: sys.color.primary
│   ├── Bottom border: 2px sys.color.primary
│   └── Font: sys.typography.body.default + medium
├── "Chronologie"
├── "Artefacts" (with count badge "12")
└── "Actions"

Inactive tabs:
├── Background: transparent
├── Color: sys.color.text.secondary
└── Hover: color → sys.color.text.primary

ARIA: role="tablist" with role="tab" + aria-selected
```

#### Tab Content — Résumé (Card, 24px padding)

```
INVERTED PYRAMID SECTION 1 — "QUE S'EST-IL PASSÉ ?"

H3: "QUE S'EST-IL PASSÉ ?"
├── Font: sys.typography.heading.3 (20px semibold)
├── Color: sys.color.text.primary
└── Letter spacing: ref.letter.spacing.wide (uppercase appearance)

Paragraph:
├── Font: sys.typography.body.default + line-height: relaxed
├── Color: sys.color.text.primary
└── Width: max 720px (readable line length per typographic best practice)

Content:
"Le module SENTINEL a détecté qu'un fichier canary a été modifié
par powershell.exe (PID 4892) sur le poste PEDIATRIE-02. L'analyse
d'entropie a relevé une valeur de 7.92 sur 12 fichiers en 4 secondes —
un indicateur fort de chiffrement actif par ransomware."

CONFIDENCE BAR (below paragraph):
├── Label: "Confiance de détection : 94%" (body small)
└── Progress bar:
    ├── Width: 240px
    ├── Height: 8px
    ├── Background: sys.color.surface.muted
    ├── Fill: sys.color.feedback.success (green if > 70%)
    ├── Border-radius: ref.radius.sm
    └── Animated fill on render (0 → 94% over 600ms)

DIVIDER (24px vertical gap)

INVERTED PYRAMID SECTION 2 — "POURQUOI EST-CE IMPORTANT ?"

H3: "POURQUOI EST-CE IMPORTANT ?"
Paragraph (with hospital context):
"Ce poste est dans le service de Pédiatrie et a accès au dossier
patient. Une infection active pourrait compromettre les données
médicales et propager le ransomware au réseau hospitalier."

DIVIDER

INVERTED PYRAMID SECTION 3 — "ACTIONS RECOMMANDÉES"

H3: "ACTIONS RECOMMANDÉES"
Ordered list:
1. Isoler immédiatement le poste du réseau (LinkAction → opens isolate modal)
2. Identifier l'utilisateur connecté
3. Investiguer l'origine du processus PowerShell
4. Vérifier les sauvegardes du service

Each item:
├── Number circle: 24×24 circle, gray.95 bg, gray.40 text
├── Text: body.default
└── Action items 1 is a Link styled as a recommended action button

DIVIDER

COLLAPSIBLE — Détails techniques

[▼ Détails techniques] (button-styled, click to expand)
When expanded:
├── Process info: cmd + args (mono font)
├── File paths: list of 12 affected files (mono font, truncated middle)
├── Entropy chart: small Recharts sparkline
└── Network connections (if any)
```

#### RBAC Variants

**For read_only_auditor:**
- Action buttons cluster: replaced by info banner "Mode lecture seule"
- No "Prendre en charge", "Marquer FP", "Isoler" buttons
- Tabs still accessible (read-only)

**For acknowledged alert:**
- "Prendre en charge" replaced by "Marquer comme fermé"
- Assignee shown in header: "Assigné à Marc Tchoumi"

**For closed alert:**
- All action buttons hidden
- Resolution prominently shown in Section 1 header area:
  - Background: sys.color.feedback.success.subtle
  - Resolution category: "Vrai positif — Contenu"
  - Resolution notes (formatted)
  - Closed by + at

---

## 3. Hi-Fi Mockup 3 — Endpoint Isolation Confirmation Flow

### Why This Mockup Is Hi-Fi Critical

Per OST SOL B1, the 1-click isolate with confirmation is the **highest-stakes action** in the application. A wrong click could disconnect a critical hospital workstation. The confirmation modal must:
- Convey gravity (visual emphasis)
- Require deliberate confirmation (typed reason + checkbox)
- Provide clear cancel path
- Be keyboard-accessible

### Modal Specification

```
SCRIM:
├── Background: sys.color.background.scrim (rgba(0,0,0,0.4))
├── Position: fixed top:0 left:0 right:0 bottom:0
├── z-index: ref.zindex.modal (300)
└── Click outside: closes modal (with confirmation if form dirty — Sprint 8)

MODAL:
├── Centered: position fixed, transform translate(-50%, -50%)
├── Max-width: 560px
├── Width: calc(100% - 32px) on mobile
├── Background: white
├── Border-radius: ref.radius.xl (12px)
├── Box-shadow: sys.elevation.modal (xl shadow)
├── Padding: 0 (sections handle their own)
└── Animation:
    ├── Enter: opacity 0 → 1 + scale 0.95 → 1 (200ms ease-out)
    └── Exit: opacity 1 → 0 + scale 1 → 0.95 (150ms ease-in)

ARIA:
├── role="dialog"
├── aria-modal="true"
├── aria-labelledby="modal-title"
├── aria-describedby="modal-description"
└── Focus trap: first focusable on open, restore on close
```

#### Modal Section 1 — Header (Critical Visual Tone)

```
Background: sys.color.feedback.error.subtle (#FDECEC)
Border-bottom: 1px solid sys.color.feedback.error.border
Padding: 24px

ICON (top-left):
├── 48×48 circle
├── Background: sys.color.feedback.error
├── Icon: shield-off (white, 24×24, centered)

TITLE (right of icon, 16px gap):
├── Font: sys.typography.heading.2 (25px semibold)
├── Color: sys.color.text.primary
└── Text: "Isoler PEDIATRIE-02 du réseau ?"

CLOSE BUTTON (top-right):
├── 32×32 icon button
├── Icon: x (16×16)
├── Color: sys.color.text.secondary
├── Hover: bg sys.color.interactive.hover.bg
└── ARIA: aria-label="Fermer"
```

#### Modal Section 2 — Body (Padding 24px)

```
WARNING TEXT:
├── Font: sys.typography.body.default
├── Color: sys.color.text.primary
└── Text:
"Cette action va déconnecter immédiatement PEDIATRIE-02 de tous les
services réseau, à l'exception de la console de gestion RansomGuard.

L'utilisateur connecté perdra l'accès aux applications cliniques
(SIH, EMR, PACS) et au partage de fichiers. Cette mesure est
nécessaire pour empêcher la propagation du ransomware."

DIVIDER (16px gap)

REASON FIELD:
├── Label: "Raison de l'isolation" + required asterisk
├── Helper text: "Minimum 20 caractères, maximum 500"
├── Textarea:
│   ├── Min-height: 80px
│   ├── Max-height: 200px (auto-grow)
│   ├── Font: sys.typography.body.default
│   ├── Border: 1px solid sys.color.border.default
│   ├── Border on focus: 2px solid sys.color.border.focus
│   └── Placeholder: "Ex: Activité ransomware confirmée par SENTINEL,
│                     isolation pour confinement immédiat"
├── Character counter (bottom right):
│   ├── "127 / 500"
│   ├── Color: tertiary when in range
│   ├── Color: warning when < 20 chars (red)
│   └── Color: critical when > 500 chars
└── Live validation: hide error until first blur

CHECKBOX (16px gap below textarea):
├── Required checkbox + label
├── Label: "Je comprends que cela va déconnecter le poste du réseau."
├── Font: sys.typography.body.small
└── Color: sys.color.text.primary
```

#### Modal Section 3 — Footer (Padding 16px 24px)

```
Background: sys.color.surface.subtle
Border-top: 1px solid sys.color.border.subtle

LAYOUT:
├── Justify: flex-end (right-aligned buttons)
└── Gap: 12px

BUTTONS:
├── [Annuler] secondary button
│   ├── Click: close modal
│   ├── Color: sys.color.text.primary
│   └── Border: 1px solid sys.color.border.default
└── [Isoler le poste] danger button
    ├── Background: sys.color.feedback.error
    ├── Color: white
    ├── Icon: shield-off (16×16) left of text
    ├── DISABLED if:
    │   ├── Reason < 20 chars OR > 500 chars
    │   └── Checkbox not checked
    ├── Loading state (when clicked):
    │   ├── Replace text with "Envoi..."
    │   └── Disable button
    └── Submit
```

#### Post-Submit States

**On 202 Accepted:**
1. Modal closes (200ms fade)
2. Toast appears (top-right):
   - Color: sys.color.feedback.warning (orange — pending)
   - Icon: ⏱️
   - Text: "Commande envoyée. L'agent sera contacté au prochain heartbeat."
   - Auto-dismiss: 6 seconds
3. Agent detail page status: "Isolation en cours" (yellow border + spinner)

**On error (4xx/5xx):**
1. Modal stays open
2. Inline error banner appears at top of body:
   - Background: sys.color.feedback.error.subtle
   - Border: sys.color.feedback.error.border
   - Icon: alert-circle
   - Text: "Erreur lors de l'envoi de la commande. Veuillez réessayer."
3. Button returns to enabled state (form not reset)

**On polling — status changes:**
1. After ~30s next heartbeat, agent status changes to "Isolé"
2. Page updates with:
   - Red border around card
   - Status badge "Isolé" (with shield-off icon)
   - "Restaurer l'accès réseau" button appears (primary action now)
   - Audit timeline shows new entry

---

## 4. Clickable Prototype Specification

### Tool & Scope

- **Tool:** Figma Prototype mode (or equivalent — Maze, Adobe XD)
- **Scope:** 4 critical flows from Discovery user flows
- **Purpose:** Usability testing readiness, stakeholder review

### Prototype Flows

#### Flow A — Login → Operational Dashboard (security_analyst)

Connected screens:
1. Login screen → on Submit click → loading state (200ms) → Operational Dashboard
2. Hover sidebar items → tooltip show
3. Click language switcher → dropdown shows → click English → page re-renders in English

**Interactions defined:**
- Email input focused on page load
- Tab key cycles: Email → Password → Submit
- Enter key on Password submits

#### Flow B — Alert Triage → Acknowledge

Connected screens:
1. Operational Dashboard → click alert row → Alert Detail (Résumé tab)
2. Read narrative → click "Prendre en charge" → Modal appears
3. (Optional) Type note → click "Confirmer"
4. Modal closes → Status badge in header animates from "Nouveau" to "En cours"
5. Action buttons cluster updates (Acknowledge → "Fermer" / "Marquer FP")

#### Flow C — Endpoint Isolation Confirmation

Connected screens:
1. Alert Detail (acknowledged) → click "Isoler PEDIATRIE-02 du réseau"
2. Confirmation Modal appears
3. Type reason (test with < 20 chars first → button disabled)
4. Type reason (≥ 20 chars) + check checkbox → button enabled
5. Click "Isoler le poste" → modal closes
6. Toast appears top-right
7. Agent status (when navigating to /agents/:id) shows "Isolation en cours"

#### Flow D — Auditor Compliance Review

Connected screens:
1. Login as Sister Jeanne → Read-Only Dashboard
2. Click "Consulter le journal d'audit complet" → /audit
3. Apply date filter "Last quarter" → filtered list
4. Type search "report_signed" → filtered to 4 results
5. Click "Exporter CSV" → file download (simulated)

### Prototype Validation Plan (Post-Soutenance)

Per Nielsen & Landauer 1993 (5 users find 85% of problems):
- Recruit 5 testers (FICT alumni in healthcare IT or proxy users)
- Tasks per session:
  - Task 1: Log in and find the highest-priority alert (target time < 30s)
  - Task 2: Acknowledge an alert and document the rationale (target < 90s)
  - Task 3: Isolate an endpoint with appropriate justification (target < 60s)
  - Task 4 (auditor variant): Find and export audit logs for last month (target < 2min)
- Measure:
  - Task completion rate (target ≥ 80%)
  - Time on task (target meets above)
  - Error rate (target < 1 critical error per task)
  - SUS (System Usability Scale, target ≥ 75)
  - Post-test interview (qualitative feedback)

---

## 5. Architecture Decision Records (ADRs)

### ADR Format (Per Nygard 2011)

Each ADR follows:
- **Title** — short noun phrase
- **Status** — Proposed / Accepted / Deprecated / Superseded
- **Context** — what is the issue
- **Decision** — what we will do
- **Consequences** — positive / negative / neutral results

### ADR-FE-001: React 18 + TypeScript + Vite

- **Status:** Accepted
- **Date:** 2026-06-07
- **Context:** Need to choose a frontend framework, build tool, and language for Sprint 7 production dashboard. The application must support a multi-page SPA, strict type safety for a security product, sub-second HMR for developer experience, and ES modules.
- **Decision:** Use React 18.3, TypeScript 5.5 in strict mode, and Vite 5.4 as the build tool. Reject Create React App (deprecated by React team in 2023), Next.js (overkill for SPA without SSR needs), and Webpack (slower HMR than Vite).
- **Consequences:**
  - **+** Sub-second HMR (Vite)
  - **+** Mature ecosystem (React 18)
  - **+** Industry-standard skills transferable
  - **+** TypeScript strict mode catches errors at compile time (critical for security product)
  - **-** Vite ESM-only requires modern browsers (acceptable — Sprint 7 targets Chrome/Firefox/Edge ≥ 110)
  - **0** TanStack Query + Zustand integrate well

### ADR-FE-002: shadcn/ui + TailwindCSS + Radix Primitives

- **Status:** Accepted
- **Context:** Need a UI component library that is accessible, customizable, owned (no opaque dependency), and integrates with our design token pipeline.
- **Decision:** Use shadcn/ui as the component primitive layer (copy-don't-depend model), TailwindCSS 3.4 for utility-first styling, and Radix UI primitives for headless accessibility behavior (used internally by shadcn/ui).
- **Consequences:**
  - **+** Full control over component code (every line is ours)
  - **+** Radix UI provides WAI-ARIA conformant primitives (Listbox, Menu, Dialog, etc.)
  - **+** Tailwind consumes our token CSS variables natively
  - **+** Easier theming via tokens
  - **-** Larger initial setup than a packaged library like MUI
  - **-** Tailwind class strings can become long (use `cn()` helper + tailwind-merge)
  - **0** Storybook integration straightforward

### ADR-FE-003: TanStack Query for Server State, Zustand for Client State

- **Status:** Accepted
- **Context:** Need to manage server-fetched data (alerts, agents, users) with caching and refetching, plus client-side UI state (modal open, current filter chip, language).
- **Decision:** TanStack Query 5 for all server state (handles caching, refetching, mutations, optimistic updates). Zustand 4 for client state only.
- **Consequences:**
  - **+** Clear separation: server vs client state
  - **+** No Redux boilerplate
  - **+** TanStack Query handles 80% of data lifecycle without manual code
  - **+** Zustand is lightweight (1KB)
  - **-** Two libraries instead of one (acceptable for explicit boundary)
  - **0** TanStack Query keys must be tenant-scoped to prevent cross-tenant pollution

### ADR-FE-004: Axios with Interceptors

- **Status:** Accepted
- **Context:** Need an HTTP client that supports interceptors (for JWT injection, automatic refresh on 401), TypeScript types, and OpenAPI integration.
- **Decision:** Axios 1.x with custom interceptors:
  - Request interceptor: inject Authorization header from authStore
  - Response interceptor: catch 401, attempt refresh via dedupe pattern, retry original request
- **Consequences:**
  - **+** Battle-tested HTTP client
  - **+** Interceptor pattern is clean
  - **-** Axios bundle size > native fetch (acceptable trade-off for features)

### ADR-FE-005: React Hook Form + Zod for Forms and Validation

- **Status:** Accepted
- **Context:** Need a performant form library with TypeScript-friendly validation that doesn't re-render the entire form on every keystroke.
- **Decision:** React Hook Form 7 + Zod for schema validation. Use `@hookform/resolvers/zod` for integration.
- **Consequences:**
  - **+** Performant (uncontrolled inputs, refs)
  - **+** Zod schemas double as TypeScript types (single source)
  - **+** Server response validation also via Zod (defense in depth)
  - **0** Slight learning curve for `register` API

### ADR-FE-006: react-i18next with ICU MessageFormat

- **Status:** Accepted
- **Context:** Bilingual French (default) / English support is a core requirement. French has gendered nouns, accents, and pluralization rules that simple key-value translation fails on.
- **Decision:** react-i18next 15 with the `i18next-icu` plugin for ICU MessageFormat support (pluralization, gender, select).
- **Consequences:**
  - **+** ICU handles complex plurals correctly
  - **+** All strings externalized (`public/locales/{fr,en}.json`)
  - **+** Locale-aware date/number formatting via `Intl.DateTimeFormat`
  - **-** Initial setup is heavier than basic i18next
  - **0** Pseudolocale testing recommended (FR + ZZ pseudo for visual debugging)

### ADR-FE-007: Style Dictionary for Design Token Pipeline

- **Status:** Accepted
- **Context:** Need to maintain a single source of truth for design tokens that outputs to CSS variables (for Tailwind), TypeScript declarations, and (future) iOS/Android format.
- **Decision:** Style Dictionary (Amazon, open-source) as the token transformation pipeline. Source files in `dashboard/design-tokens/*.json`, build to CSS + TS + JSON.
- **Consequences:**
  - **+** Single source of truth
  - **+** Multi-platform output ready (for future native mobile)
  - **+** Reference token aliases preserved with `outputReferences: true`
  - **-** Build step required (manageable: add to CI)

### ADR-FE-008: Vitest + Playwright + Component Testing + Visual Regression

- **Status:** Accepted
- **Context:** Need to implement Google's Small/Medium/Large test taxonomy mapped to our SPA architecture.
- **Decision:**
  - **Small tests (unit):** Vitest for pure functions, parsers, validators, hooks (mocked deps).
  - **Medium tests (integration):** Playwright Component Testing with MSW (mocked API) for component + state integration.
  - **Large tests (E2E):** Playwright against `docker-compose up` full stack.
  - **Visual regression:** Playwright snapshots per Storybook story.
  - **Accessibility:** axe-core via `@axe-core/playwright` in CI.
- **Consequences:**
  - **+** Aligned with Google senior testing methodology
  - **+** Same tool (Playwright) for E2E and component testing
  - **+** Visual regression catches token regressions
  - **-** Storybook + Chromatic costs (use free tier OR Playwright snapshots locally)

### ADR-FE-009: HTTP-Only Refresh Cookie + In-Memory Access Token

- **Status:** Accepted
- **Context:** Need to balance UX (don't relogin every 15 minutes) with security (no XSS-extractable tokens).
- **Decision:**
  - **Access token:** stored in memory (Zustand store), JWT 15-min TTL.
  - **Refresh token:** stored in HttpOnly Secure SameSite=Strict cookie, 7-day TTL.
  - **Refresh:** automatic via axios interceptor on 401, deduplicated via promise singleton.
- **Consequences:**
  - **+** Access token cannot be exfiltrated by XSS
  - **+** Refresh cookie cannot be read by JS
  - **+** SameSite=Strict defends against CSRF on auth endpoints
  - **-** In-memory token lost on page reload → user re-fetches via refresh on app boot
  - **0** Aligned with STRIDE mitigations WT1.3, WT1.7

### ADR-FE-010: Optimistic UI Updates for Mutations

- **Status:** Accepted
- **Context:** Status changes (acknowledge, close) and isolation commands must feel instant for usability. Waiting for server roundtrip (300-800ms) creates anxiety in incident response.
- **Decision:** Use TanStack Query mutations with `onMutate` (apply optimistic update), `onError` (revert), `onSettled` (refetch to confirm).
- **Consequences:**
  - **+** Sub-100ms perceived latency
  - **+** Better UX for high-frequency analyst actions
  - **-** Brief UI flash on rare server rejection (acceptable)
  - **0** Aligned with OST SOL B3

---

## 6. Design Phase — Closure & Handoff

### Design Phase Deliverables Checklist (per Master Plan)

| Artefact | Status | File |
|---|:---:|---|
| Design Tokens 3-tier (Material 3) | ✅ Done | 06_design_tokens.md |
| Reference tokens (palette, type, space, motion) | ✅ Done | 06 §2 |
| System tokens (semantic) | ✅ Done | 06 §3 |
| Component tokens | ✅ Done | 06 §4 |
| Accessibility validation WCAG 2.2 AA | ✅ Done | 06 §5 |
| Style Dictionary pipeline | ✅ Done | 06 §7 |
| Atomic Design component inventory (50 components) | ✅ Done | 07-08 §1 |
| shadcn/ui install plan | ✅ Done | 07-08 §2 |
| Custom components specifications | ✅ Done | 07-08 §3 |
| Low-fi wireframes ×8 | ✅ Done | 07-08 §4-11 |
| Hi-fi mockup specifications ×3 | ✅ Done | 09-10 §1-3 |
| Clickable prototype spec | ✅ Done | 09-10 §4 |
| Frontend ADRs ×10 | ✅ Done | 09-10 §5 |
| Storybook structure | ✅ Done | 07-08 §12 |

### Top 10 Design Decisions Locked

1. **Design tokens follow Material 3 three-tier hierarchy** (Reference → System → Component).
2. **Color palette is hospital-clinical** (blue primary, no aggressive marketing colors).
3. **Typography: Inter Variable** (excellent FR diacritic support, single variable font file).
4. **Spacing: 4px base unit** with semantic aliases (inset / stack / inline).
5. **8 low-fi wireframes** cover all Sprint 7 routes (Login, 3 dashboards, Alerts list + detail, Agents, Users).
6. **3 hi-fi mockups specified in full detail** (Operational Dashboard, Alert Detail, Isolation Modal) — the highest-impact surfaces.
7. **50 components inventoried** following Atomic Design (18 atoms, 16 molecules, 12 organisms, 4 templates).
8. **shadcn/ui as primitive layer**, custom domain components on top.
9. **Storybook + Playwright snapshots** for visual regression.
10. **WCAG 2.2 AA conformance validated at token level** (every text/bg combo ≥ 4.5:1 or 3:1).

### Design Phase Risks Identified

| ID | Risk | Mitigation |
|---|---|---|
| RDS1 | Design tokens may drift from Figma | Figma Tokens Studio + Style Dictionary sync (Sprint 7 setup) |
| RDS2 | shadcn/ui updates may conflict with our tokens | Pin versions; track updates manually |
| RDS3 | Hi-fi mockups are textual, not visual Figma files | Build Figma files in parallel during Build phase; this spec is the contract |
| RDS4 | Tailwind `unsafe-inline` styles weaken CSP | Migrate to CSS-in-JS-free approach with nonces in Sprint 8 (per ADR-FE-002 + STRIDE WT1) |
| RDS5 | Clickable prototype not tested with real users in bachelor timeframe | Document as Chapter 5 limitation; plan post-soutenance validation |

### Definition Phase — Final Statement

The Design Phase has produced 14 senior-grade artefacts establishing the visual, structural, and architectural foundation for RansomGuard-CM Sprint 7 frontend implementation. Design tokens are defined with three-tier Material 3 hierarchy and validated for WCAG 2.2 AA contrast. 50 components are inventoried with Atomic Design taxonomy. 8 low-fi wireframes and 3 hi-fi mockup specifications cover all critical Sprint 7 surfaces. 10 Architecture Decision Records lock the frontend technology stack with justifications.

**Design Phase — CLOSED.**

**Next: Build Phase (Sprint 7 React Implementation) — Per Sprint 7 PRD v2 + Phase 0 backend + this Design Phase output.**
