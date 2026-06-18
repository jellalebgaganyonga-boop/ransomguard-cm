# RansomGuard-CM — Design Phase, Days 7-8

**Document Type**: Component Library Inventory + 8 Low-Fi Wireframes (ASCII)
**Phase**: Design (Master Plan Phase 3)
**Status**: Authoritative
**Author Role**: UI Designer + Design Systems Engineer (solo)
**Methodology Sources**:
- Brad Frost — *Atomic Design* (atomicdesign.bradfrost.com)
- shadcn/ui — Component documentation (ui.shadcn.com)
- Radix UI — Primitives documentation (www.radix-ui.com/primitives)
- Storybook — Component-Driven Development (storybook.js.org/tutorials/intro-to-storybook)
- Nathan Curtis — *Defining Component APIs* (eightshapes.com)

---

## Table of Contents

1. [Component Library — Atomic Design Inventory](#1-component-library--atomic-design-inventory)
2. [shadcn/ui Components to Install](#2-shadcnui-components-to-install)
3. [Custom Components to Build](#3-custom-components-to-build)
4. [Low-Fi Wireframe 1 — Login](#4-low-fi-wireframe-1--login)
5. [Low-Fi Wireframe 2 — Executive Dashboard (tenant_admin)](#5-low-fi-wireframe-2--executive-dashboard-tenant_admin)
6. [Low-Fi Wireframe 3 — Operational Dashboard (security_analyst)](#6-low-fi-wireframe-3--operational-dashboard-security_analyst)
7. [Low-Fi Wireframe 4 — Read-Only Dashboard (read_only_auditor)](#7-low-fi-wireframe-4--read-only-dashboard-read_only_auditor)
8. [Low-Fi Wireframe 5 — Alerts List](#8-low-fi-wireframe-5--alerts-list)
9. [Low-Fi Wireframe 6 — Alert Detail](#9-low-fi-wireframe-6--alert-detail)
10. [Low-Fi Wireframe 7 — Agents Inventory](#10-low-fi-wireframe-7--agents-inventory)
11. [Low-Fi Wireframe 8 — Users Management](#11-low-fi-wireframe-8--users-management)
12. [Storybook Structure](#12-storybook-structure)

---

## 1. Component Library — Atomic Design Inventory

### Architecture Per Brad Frost

```
ATOMS         → Smallest building blocks (Button, Input, Badge, Icon)
MOLECULES     → Atoms combined (SearchBox, AlertRow, BadgeWithIcon)
ORGANISMS     → Complex UI blocks (Header, Sidebar, AlertTable, IncidentDetail)
TEMPLATES     → Page-level wireframes (DashboardLayout, AuthLayout)
PAGES         → Templates filled with data (ExecutiveDashboard, AlertDetailPage)
```

### Complete Inventory (50 components)

#### Atoms (18)

| # | Component | Purpose | Tokens |
|---|---|---|---|
| 1 | Button | Primary action affordance | cmp.button.* |
| 2 | IconButton | Button with icon only | cmp.button.* + size sm |
| 3 | Input | Text input single line | cmp.input.* |
| 4 | Textarea | Multi-line text input | cmp.input.* |
| 5 | Label | Field label | sys.typography.label |
| 6 | Badge | Severity / status pill | cmp.badge.* |
| 7 | Avatar | User picture / initials | sys.color.primary + ref.radius.full |
| 8 | Spinner | Loading indicator | sys.color.primary + motion |
| 9 | Skeleton | Loading placeholder | cmp.skeleton.* |
| 10 | Divider | Visual separator | sys.color.border.subtle |
| 11 | Icon | Lucide icon wrapper | sys.color.text.* |
| 12 | Checkbox | Boolean input | cmp.input.* + custom |
| 13 | Radio | Single-select input | cmp.input.* + custom |
| 14 | Switch | Toggle | cmp.input.* + custom |
| 15 | Link | Navigation link styling | sys.color.text.link |
| 16 | Kbd | Keyboard shortcut display | sys.typography.code + bordered |
| 17 | StatusDot | Colored dot indicator | sys.color.status.* |
| 18 | ProgressBar | Linear progress | sys.color.primary |

#### Molecules (16)

| # | Component | Purpose | Composed of |
|---|---|---|---|
| 19 | SearchInput | Input + Icon + Clear button | Input + IconButton + Icon |
| 20 | SelectField | Label + Select + Helper | Label + custom Select + caption |
| 21 | InputField | Label + Input + Helper / Error | Label + Input + Caption |
| 22 | TextareaField | Label + Textarea + Counter | Label + Textarea + Caption |
| 23 | PriorityScore | 0-100 chip with color band | Badge + custom logic |
| 24 | SeverityBadge | Critical/High/Medium/Low/Info | Badge + Icon |
| 25 | StatusBadge | New/In progress/Resolved/Closed/Isolated | Badge + Icon |
| 26 | RoleBadge | tenant_admin/security_analyst/auditor | Badge |
| 27 | FilterChip | Removable filter tag | Badge + IconButton |
| 28 | EmptyState | Illustration + text + action | Icon + heading + body + Button |
| 29 | Pagination | Page navigation control | IconButton + Buttons |
| 30 | Toast | Non-blocking notification | Card + Icon + Button |
| 31 | InlineAlert | In-page notification | Icon + heading + body + dismiss |
| 32 | Tooltip | Hover-revealed info | Popover + caption |
| 33 | UserMenu | Avatar + dropdown | Avatar + Dropdown |
| 34 | NotificationBell | Bell + count badge | Icon + Badge |

#### Organisms (12)

| # | Component | Purpose | Composed of |
|---|---|---|---|
| 35 | Header | Top app bar | Logo + TenantBadge + NotificationBell + LangSwitcher + UserMenu |
| 36 | Sidebar | L1 navigation | SidebarItem × 6 (RBAC-filtered) |
| 37 | DataTable | Sortable, filterable table | Header + Row + Pagination + EmptyState |
| 38 | FilterBar | Multi-filter toolbar | SearchInput + Select × N + FilterChip × N + Button reset |
| 39 | AlertRow | Alert in list | PriorityScore + SeverityBadge + StatusBadge + body + actions |
| 40 | AgentRow | Agent in list | StatusDot + body + StatusBadge + actions |
| 41 | UserRow | User in list | Avatar + body + RoleBadge + StatusBadge + actions |
| 42 | KPICard | Big number with label | Card + heading + big number + trend |
| 43 | ConfirmModal | Confirmation dialog | Modal + heading + body + Buttons |
| 44 | FormModal | Form-in-modal | Modal + Form fields + Buttons |
| 45 | DetailHeader | Detail page header | breadcrumb + title + badges + actions |
| 46 | Timeline | Chronological events | event entry × N |

#### Templates (4)

| # | Template | Purpose | Composed of |
|---|---|---|---|
| 47 | AuthLayout | Login / forgot password layout | Centered card + Header |
| 48 | AppShellLayout | Main authenticated layout | Header + Sidebar + Content area |
| 49 | DashboardLayout | Dashboard page layout | AppShellLayout + grid sections |
| 50 | DetailLayout | Detail page layout | AppShellLayout + DetailHeader + Tabs |

---

## 2. shadcn/ui Components to Install

shadcn/ui follows the "copy, don't depend" model — components are copied into the codebase and customized.

### Install Commands (Day 2 of Build Phase)

```bash
cd dashboard
npx shadcn@latest init   # configure (tokens, paths, baseColor, cssVariables)

# Then add components in this order:
npx shadcn@latest add button
npx shadcn@latest add input
npx shadcn@latest add label
npx shadcn@latest add textarea
npx shadcn@latest add checkbox
npx shadcn@latest add switch
npx shadcn@latest add separator
npx shadcn@latest add badge
npx shadcn@latest add avatar
npx shadcn@latest add dropdown-menu
npx shadcn@latest add dialog
npx shadcn@latest add alert
npx shadcn@latest add alert-dialog
npx shadcn@latest add toast
npx shadcn@latest add tooltip
npx shadcn@latest add popover
npx shadcn@latest add tabs
npx shadcn@latest add table
npx shadcn@latest add select
npx shadcn@latest add command
npx shadcn@latest add sheet
npx shadcn@latest add skeleton
npx shadcn@latest add progress
npx shadcn@latest add form
npx shadcn@latest add scroll-area
```

### Why shadcn/ui

Per shadcn/ui philosophy (ui.shadcn.com):
- **Built on Radix UI primitives** (battle-tested accessibility — WAI-ARIA conformant)
- **Customizable via Tailwind** (matches our token pipeline)
- **No runtime dependency** (copy code into our repo)
- **Zero opaque components** (we own every line)

### Components NOT from shadcn (we build custom)

The Atomic Design organisms specific to RansomGuard-CM are custom:
- `PriorityScore` (no equivalent in shadcn)
- `SeverityBadge` (custom semantic over shadcn Badge)
- `AlertRow`, `AgentRow`, `UserRow` (domain-specific)
- `KPICard`, `Timeline` (domain-specific)
- `Sidebar` (domain-specific navigation)
- `Header` (app-specific composition)

---

## 3. Custom Components to Build

### 3.1 PriorityScore Component

```typescript
// dashboard/src/components/ui/priority-score.tsx

import { cn } from '@/lib/utils';

interface PriorityScoreProps {
  score: number;       // 0-100
  className?: string;
}

export function PriorityScore({ score, className }: PriorityScoreProps) {
  // Defender XDR pattern:
  // Red    > 85
  // Orange 15-85
  // Gray   < 15
  const tone = score > 85 ? 'high' : score >= 15 ? 'medium' : 'low';

  return (
    <span
      className={cn(
        'inline-flex items-center justify-center min-w-[40px] px-2 py-1',
        'text-xs font-bold rounded-md text-white',
        tone === 'high' && 'bg-severity-critical',
        tone === 'medium' && 'bg-severity-high',
        tone === 'low' && 'bg-text-tertiary',
        className
      )}
      aria-label={`Priority score ${score} out of 100`}
    >
      {score}
    </span>
  );
}
```

### 3.2 SeverityBadge Component

```typescript
// dashboard/src/components/ui/severity-badge.tsx

import { AlertCircle, AlertTriangle, Info, CheckCircle, Bell } from 'lucide-react';
import { Badge } from '@/components/ui/badge';
import { useTranslation } from 'react-i18next';

type Severity = 'critical' | 'high' | 'medium' | 'low' | 'info';

const SEVERITY_CONFIG = {
  critical: { icon: AlertCircle, color: 'bg-severity-critical/10 text-severity-critical border-severity-critical/30' },
  high:     { icon: AlertTriangle, color: 'bg-severity-high/10 text-severity-high border-severity-high/30' },
  medium:   { icon: AlertTriangle, color: 'bg-severity-medium/10 text-severity-medium border-severity-medium/30' },
  low:      { icon: Info, color: 'bg-severity-low/10 text-severity-low border-severity-low/30' },
  info:     { icon: Bell, color: 'bg-text-tertiary/10 text-text-tertiary border-text-tertiary/30' },
};

export function SeverityBadge({ severity }: { severity: Severity }) {
  const { t } = useTranslation();
  const config = SEVERITY_CONFIG[severity];
  const Icon = config.icon;

  return (
    <Badge className={`inline-flex items-center gap-1 px-2 py-0.5 border ${config.color}`}>
      <Icon className="w-3 h-3" aria-hidden="true" />
      <span className="text-xs font-semibold uppercase tracking-wide">
        {t(`severity.${severity}`)}
      </span>
    </Badge>
  );
}
```

### 3.3 KPICard Component

```typescript
// dashboard/src/components/ui/kpi-card.tsx

import { Card } from '@/components/ui/card';
import { ArrowUp, ArrowDown, Minus } from 'lucide-react';

interface KPICardProps {
  label: string;
  value: number | string;
  trend?: 'up' | 'down' | 'stable';
  trendValue?: string;
  description?: string;
}

export function KPICard({ label, value, trend, trendValue, description }: KPICardProps) {
  const TrendIcon = trend === 'up' ? ArrowUp : trend === 'down' ? ArrowDown : Minus;

  return (
    <Card className="p-6">
      <p className="text-sm font-medium text-text-secondary uppercase tracking-wide">{label}</p>
      <p className="mt-2 text-4xl font-bold text-text-primary tabular-nums">{value}</p>
      {trend && trendValue && (
        <p className="mt-2 flex items-center gap-1 text-sm text-text-tertiary">
          <TrendIcon className="w-4 h-4" />
          <span>{trendValue}</span>
        </p>
      )}
      {description && <p className="mt-1 text-xs text-text-tertiary">{description}</p>}
    </Card>
  );
}
```

---

## 4. Low-Fi Wireframe 1 — Login

```
╔══════════════════════════════════════════════════════════════════════╗
║                                                                        ║
║                                                                        ║
║                                                                        ║
║                                                                        ║
║                                                                        ║
║                  ┌────────────────────────────────────┐               ║
║                  │                                      │               ║
║                  │            🛡 RansomGuard-CM         │               ║
║                  │                                      │               ║
║                  │            Connexion                 │               ║
║                  │                                      │               ║
║                  │  ─────────────────────────────────  │               ║
║                  │                                      │               ║
║                  │  Email                               │               ║
║                  │  ┌──────────────────────────────┐  │               ║
║                  │  │ marc.tchoumi@hopital-yde.cm   │  │               ║
║                  │  └──────────────────────────────┘  │               ║
║                  │                                      │               ║
║                  │  Mot de passe                        │               ║
║                  │  ┌──────────────────────────────┐  │               ║
║                  │  │ ••••••••••••••                │👁│               ║
║                  │  └──────────────────────────────┘  │               ║
║                  │                                      │               ║
║                  │  [ ] Se souvenir de moi              │               ║
║                  │                                      │               ║
║                  │  ┌──────────────────────────────┐  │               ║
║                  │  │      Se connecter              │  │               ║
║                  │  └──────────────────────────────┘  │               ║
║                  │                                      │               ║
║                  │  ─────────────────────────────────  │               ║
║                  │                                      │               ║
║                  │  Version 0.9.0    🌐 [Français ▼]   │               ║
║                  │                                      │               ║
║                  └────────────────────────────────────┘               ║
║                                                                        ║
║                                                                        ║
║                                                                        ║
║                                                                        ║
╚══════════════════════════════════════════════════════════════════════╝
```

### Specifications

- Centered card max-width 400px on desktop, full-width with padding on mobile
- Background: `sys.color.surface.subtle`
- Card: `sys.color.background.elevated` + `sys.elevation.card`
- Logo height: 32px
- Title: `sys.typography.heading.2`
- Form fields: 12px gap (stack.sm)
- "Se connecter" button: full-width, `cmp.button.primary`
- Language switcher: bottom-right, opens dropdown with FR | EN
- Forgot-password link: Sprint 8 placeholder (visible but disabled tooltip)

### Behavior

- Email validation client-side: format only
- Password validation client-side: min length only (server is authoritative)
- Submit: triggers `POST /api/v1/auth/login`
- On error: inline below button "Email ou mot de passe incorrect"
- On success: 200ms transition → role-based redirect to `/dashboard`
- Eye icon toggles password visibility (with `aria-label` switch)
- Caps Lock detection: warning icon if Caps Lock is on while typing password

### Empty State

Not applicable (always shows form).

### Error States

- 401: Inline generic error message + shake animation on form (200ms)
- 500: Toast "Erreur serveur, veuillez réessayer"
- Network: Toast "Connexion perdue, vérifiez votre réseau"

---

## 5. Low-Fi Wireframe 2 — Executive Dashboard (tenant_admin)

```
╔══════════════════════════════════════════════════════════════════════════════════╗
║ [🛡 RGCM]  Hôpital Régional de Yaoundé                  🔔[2] 🌐[FR] 👤 Dr.Amani▼ ║
╠═════════════╦══════════════════════════════════════════════════════════════════╣
║ 📊 Tableau   ║                                                                    ║
║ 🚨 Alertes  3║   Tableau de bord exécutif                                       ║
║ 💻 Agents    ║   Vue d'ensemble de la sécurité de l'hôpital                     ║
║ 👥 Util.     ║                                                                    ║
║ 📜 Audit     ║   ┌─────────────────────────────────────────────────────────┐   ║
║ ⚙️ Paramètres║   │                                                            │   ║
║              ║   │  🔴  3 INCIDENTS CRITIQUES REQUIÈRENT VOTRE ATTENTION     │   ║
║              ║   │                                                            │   ║
║              ║   │      Le plus récent : il y a 14 min sur PEDIATRIE-02      │   ║
║              ║   │                                                            │   ║
║              ║   │      [ Voir les détails →]                                 │   ║
║              ║   │                                                            │   ║
║              ║   └─────────────────────────────────────────────────────────┘   ║
║              ║                                                                    ║
║              ║   ┌──────────────┐ ┌──────────────┐ ┌──────────────┐ ┌──────────┐║
║              ║   │ POSTES        │ │ ALERTES 24h   │ │ JOURS SANS   │ │ STATUT  │║
║              ║   │ PROTÉGÉS      │ │               │ │ INCIDENT     │ │ CONF.   │║
║              ║   │               │ │               │ │ CRITIQUE     │ │         │║
║              ║   │  47 / 47      │ │     12        │ │      0       │ │ 98%     │║
║              ║   │  100%         │ │     ⬆ +3      │ │     reset    │ │  ✅      │║
║              ║   │  ─────        │ │   vs hier     │ │              │ │ ANTIC   │║
║              ║   │  Tous actifs  │ │               │ │              │ │ OK      │║
║              ║   └──────────────┘ └──────────────┘ └──────────────┘ └──────────┘║
║              ║                                                                    ║
║              ║   ┌─────────────────────────────────────────────────────────┐   ║
║              ║   │ ALERTES RÉCENTES                                          │   ║
║              ║   │                                              [ Voir tout →] │   ║
║              ║   ├─────────────────────────────────────────────────────────┤   ║
║              ║   │ [92] 🔴 CRITIQUE   il y a 14 min   PEDIATRIE-02         │   ║
║              ║   │       SENTINEL · Chiffrement détecté                      │   ║
║              ║   ├─────────────────────────────────────────────────────────┤   ║
║              ║   │ [74] 🟠 HAUT       il y a 47 min   CARDIO-05            │   ║
║              ║   │       USB GUARD · Périphérique USB non autorisé          │   ║
║              ║   ├─────────────────────────────────────────────────────────┤   ║
║              ║   │ [38] 🟡 MOYEN      il y a 2 h      ADMIN-12             │   ║
║              ║   │       ENTROPY · Activité de fichier inhabituelle         │   ║
║              ║   ├─────────────────────────────────────────────────────────┤   ║
║              ║   │ [22] 🔵 FAIBLE     il y a 5 h      IMAGERIE-03          │   ║
║              ║   │       GENEALOGY · Processus parent inhabituel             │   ║
║              ║   └─────────────────────────────────────────────────────────┘   ║
║              ║                                                                    ║
║              ║   ┌────────────────────────────┐ ┌────────────────────────────┐ ║
║              ║   │ Actions rapides             │ │ Conformité réglementaire   │ ║
║              ║   ├────────────────────────────┤ ├────────────────────────────┤ ║
║              ║   │                              │ │                              │ ║
║              ║   │ ┌──────────────────────┐   │ │ Loi 2024/017                 │ ║
║              ║   │ │ 👥 Gérer utilisateurs │   │ │ ✅ Conforme                   │ ║
║              ║   │ └──────────────────────┘   │ │ Dernière vérification :       │ ║
║              ║   │                              │ │ il y a 2 jours                │ ║
║              ║   │ ┌──────────────────────┐   │ │                              │ ║
║              ║   │ │ 📜 Consulter journal  │   │ │ Prochain audit ANTIC :       │ ║
║              ║   │ └──────────────────────┘   │ │ 15 septembre 2026             │ ║
║              ║   │                              │ │                              │ ║
║              ║   │ ┌──────────────────────┐   │ │ ┌────────────────────────┐  │ ║
║              ║   │ │ 📄 Rapport trimestriel│   │ │ │ Télécharger le rapport │  │ ║
║              ║   │ │   (Sprint 8)          │   │ │ │   (Sprint 8)            │  │ ║
║              ║   │ └──────────────────────┘   │ │ └────────────────────────┘  │ ║
║              ║   └────────────────────────────┘ └────────────────────────────┘ ║
║              ║                                                                    ║
╚══════════════╩══════════════════════════════════════════════════════════════════╝
```

### Specifications

- Layout: AppShellLayout with persistent Sidebar (260px) + Header (64px)
- Content: `max-w-[1280px]` + responsive grid
- Status card: full-width, severity-driven (red/orange/green)
- KPI cards: 4-column grid on desktop (1280px+), 2-column on tablet, 1-column on mobile
- Recent alerts: scrollable list, 5 items max
- Quick actions + Compliance: 2-column grid
- Auto-refresh: 30s polling via TanStack Query

### KPI Card Spec

- Big number: `sys.typography.display.medium` (39px)
- Label: `sys.typography.label` uppercase
- Trend: optional arrow + delta vs previous period
- Compliance status: shows ✅ or ⚠️ icon based on `metrics/summary` data

### RBAC Visibility

- All sections visible to `tenant_admin`
- "Gérer utilisateurs" button → `/users`
- "Consulter journal" → `/audit`
- "Rapport trimestriel" → disabled with "Sprint 8" tooltip
- "Télécharger le rapport" → disabled with "Sprint 8" tooltip

---

## 6. Low-Fi Wireframe 3 — Operational Dashboard (security_analyst)

```
╔══════════════════════════════════════════════════════════════════════════════════╗
║ [🛡 RGCM]  Hôpital Régional de Yaoundé                  🔔[3] 🌐[FR] 👤 Marc T. ▼ ║
╠═════════════╦══════════════════════════════════════════════════════════════════╣
║ 📊 Tableau   ║                                                                    ║
║ 🚨 Alertes  3║   Console opérationnelle                                          ║
║ 💻 Agents    ║   ⌘K pour la palette de commandes                                ║
║ 📜 Audit     ║                                                                    ║
║              ║   ┌──────────────────────────────────────────────────────────┐  ║
║              ║   │ ÉTAT DES AGENTS                                            │  ║
║              ║   │ 🟢 42 actifs   🟡 3 obsolètes   🔴 2 hors ligne           │  ║
║              ║   └──────────────────────────────────────────────────────────┘  ║
║              ║                                                                    ║
║              ║   ┌──────────────────────────────────────────────────────────┐  ║
║              ║   │ FLUX D'ALERTES TEMPS RÉEL              Auto ⟳ toutes 15s  │  ║
║              ║   │                                                            │  ║
║              ║   │ Filtres: [Tous▼] [Sévérité▼] [Dernière heure▼] [Module▼]│  ║
║              ║   ├──────────────────────────────────────────────────────────┤  ║
║              ║   │ [92] 🔴 CRITIQUE  PEDIATRIE-02  SENTINEL  il y a 14 min  │  ║
║              ║   │      Chiffrement détecté · 12 fichiers · powershell.exe  │  ║
║              ║   │      [Prendre en charge] [Voir détails]                   │  ║
║              ║   ├──────────────────────────────────────────────────────────┤  ║
║              ║   │ [74] 🟠 HAUT      CARDIO-05    USB GUARD  il y a 47 min  │  ║
║              ║   │      Périphérique USB non autorisé · VID:1234 PID:5678   │  ║
║              ║   │      [Prendre en charge] [Voir détails]                   │  ║
║              ║   ├──────────────────────────────────────────────────────────┤  ║
║              ║   │ [38] 🟡 MOYEN     ADMIN-12     ENTROPY    il y a 2 h     │  ║
║              ║   │      Activité fichier inhabituelle · 23 fichiers          │  ║
║              ║   │      [Prendre en charge] [Voir détails]                   │  ║
║              ║   ├──────────────────────────────────────────────────────────┤  ║
║              ║   │ [22] 🔵 FAIBLE    IMAGERIE-03  GENEALOGY  il y a 5 h     │  ║
║              ║   │      cmd.exe → powershell.exe inhabituel                  │  ║
║              ║   │      [Prendre en charge] [Voir détails]                   │  ║
║              ║   └──────────────────────────────────────────────────────────┘  ║
║              ║                                                                    ║
║              ║   ┌──────────────────────────────────────────────────────────┐  ║
║              ║   │ AGENTS — APERÇU RAPIDE                  [Voir l'inventaire→]║
║              ║   ├──────────────────────────────────────────────────────────┤  ║
║              ║   │ Hôte           OS         Heartbeat       État              │  ║
║              ║   ├──────────────────────────────────────────────────────────┤  ║
║              ║   │ PEDIATRIE-02   Win 10    il y a 12 s     🟢 Actif         │  ║
║              ║   │ CARDIO-05      Win 10    il y a 18 s     🟢 Actif         │  ║
║              ║   │ ADMIN-12       Win 11    il y a 23 s     🟢 Actif         │  ║
║              ║   │ IMAGERIE-03    Win 7     il y a 1 min    🟢 Actif         │  ║
║              ║   │ URGENCES-01    Win 10    il y a 2 min    🟢 Actif         │  ║
║              ║   │ LABO-08        Win 10    il y a 45 min   🟡 Obsolète      │  ║
║              ║   │ PHARMA-04      Win 10    il y a 2 h      🔴 Hors ligne    │  ║
║              ║   └──────────────────────────────────────────────────────────┘  ║
║              ║                                                                    ║
╚══════════════╩══════════════════════════════════════════════════════════════════╝
```

### Specifications

- Same AppShellLayout
- "Utilisateurs" sidebar item NOT visible (RBAC: not analyst)
- "Paramètres" sidebar item NOT visible (RBAC)
- Agent health: top KPI strip (3 counts)
- Alert feed: 15s auto-refresh (faster than executive)
- Agent quick view: top 7 by recent heartbeat, link to full inventory
- Ctrl+K reminder in header for command palette (Sprint 7 stretch)

### Color Coding

- Status dots green / yellow / red
- Priority scores in numeric format with color band
- Hover row: `sys.color.interactive.hover.bg`

---

## 7. Low-Fi Wireframe 4 — Read-Only Dashboard (read_only_auditor)

```
╔══════════════════════════════════════════════════════════════════════════════════╗
║ [🛡 RGCM]  Hôpital Régional de Yaoundé                  🔔[0] 🌐[FR] 👤 Mme B. ▼ ║
╠═════════════╦══════════════════════════════════════════════════════════════════╣
║ 📊 Tableau   ║                                                                    ║
║ 🚨 Alertes   ║   Vue de conformité — Hôpital Régional de Yaoundé                ║
║ 💻 Agents    ║   Accès en lecture seule                                          ║
║ 📜 Audit     ║                                                                    ║
║              ║   ┌──────────────────────────────────────────────────────────┐  ║
║              ║   │ ✅ CONFORMITÉ ACTUELLE : 98%                              │  ║
║              ║   │                                                            │  ║
║              ║   │ Statut Loi 2024/017 : Conforme                             │  ║
║              ║   │ Dernier audit ANTIC : 15 juin 2026                         │  ║
║              ║   │ Prochain audit prévu : 15 septembre 2026                   │  ║
║              ║   └──────────────────────────────────────────────────────────┘  ║
║              ║                                                                    ║
║              ║   ┌──────────────────────────┐ ┌──────────────────────────────┐║
║              ║   │ ALERTES DERNIÈRES 24h     │ │ JOURNAL DERNIERS 7 JOURS     │║
║              ║   │                            │ │                                │║
║              ║   │           12               │ │           487                  │║
║              ║   │       ─────────            │ │      ─────────                 │║
║              ║   │     dont 3 critiques       │ │     entrées d'audit            │║
║              ║   └──────────────────────────┘ └──────────────────────────────┘ ║
║              ║                                                                    ║
║              ║   ACTIONS D'AUDIT                                                  ║
║              ║                                                                    ║
║              ║   ┌──────────────────────────────────────────────────────────┐  ║
║              ║   │  📜  Consulter le journal d'audit complet                  │  ║
║              ║   │                                                            │  ║
║              ║   │      Recherche, filtres par date, exportation CSV          │  ║
║              ║   │                                                            │  ║
║              ║   │                                       [Accéder au journal →]║
║              ║   └──────────────────────────────────────────────────────────┘  ║
║              ║                                                                    ║
║              ║   ┌──────────────────────────────────────────────────────────┐  ║
║              ║   │  🚨  Consulter les alertes                                 │  ║
║              ║   │                                                            │  ║
║              ║   │      Lecture seule des alertes de sécurité                 │  ║
║              ║   │                                                            │  ║
║              ║   │                                  [Voir les alertes →]      │  ║
║              ║   └──────────────────────────────────────────────────────────┘  ║
║              ║                                                                    ║
║              ║   ┌──────────────────────────────────────────────────────────┐  ║
║              ║   │  📄  Rapport de conformité trimestriel                    │  ║
║              ║   │                                                            │  ║
║              ║   │      Disponible à partir de Sprint 8                       │  ║
║              ║   │                                                            │  ║
║              ║   │                                  [Bientôt disponible]      │  ║
║              ║   └──────────────────────────────────────────────────────────┘  ║
║              ║                                                                    ║
║              ║   ─────────────────────────────────────────────────────────────  ║
║              ║                                                                    ║
║              ║   ℹ️  Vous êtes connectée en mode auditeur. Aucune modification ║
║              ║      n'est possible. Pour toute question, contactez l'admini-     ║
║              ║      strateur de l'hôpital.                                       ║
║              ║                                                                    ║
╚══════════════╩══════════════════════════════════════════════════════════════════╝
```

### Specifications

- Sidebar limited to: Tableau, Alertes, Agents, Audit (no Users, no Settings)
- Notification bell shows 0 (auditor doesn't receive alerts)
- Compliance card uses success green
- KPIs: only 2 (alerts 24h + audit entries 7d)
- No action buttons (read-only)
- Info banner at bottom reminding read-only status

### Kaspersky Dashboard-only Mode Parallel

Per Kaspersky Security Center 14: "Only a dashboard with a predefined set of widgets is displayed to the user." We follow this pattern faithfully.

---

## 8. Low-Fi Wireframe 5 — Alerts List

```
╔══════════════════════════════════════════════════════════════════════════════════╗
║ [🛡 RGCM]  Hôpital Régional de Yaoundé                  🔔[3] 🌐[FR] 👤 Marc T. ▼ ║
╠═════════════╦══════════════════════════════════════════════════════════════════╣
║ 📊 Tableau   ║                                                                    ║
║ 🚨 Alertes  3║   Alertes                                                          ║
║ 💻 Agents    ║                                                                    ║
║ 📜 Audit     ║   ┌──────────────────────────────────────────────────────────┐  ║
║              ║   │ 🔍 Rechercher (ID, hôte, résumé...)                       │  ║
║              ║   └──────────────────────────────────────────────────────────┘  ║
║              ║                                                                    ║
║              ║   Filtres actifs :                                                ║
║              ║   [Sévérité ▼] [Statut: Nouveau ×] [Module ▼] [24h ▼]            ║
║              ║   [Réinitialiser]                                                  ║
║              ║                                                                    ║
║              ║   Onglets:                                                         ║
║              ║   ╔══════════╗ ┌────────────┐ ┌──────────┐                       ║
║              ║   ║ File (47) ║ │ Assignées 5│ │ Résolues │                       ║
║              ║   ╚══════════╝ └────────────┘ └──────────┘                       ║
║              ║                                                                    ║
║              ║   ┌──────┬──────────┬────────────┬──────────┬────────┬─────────┐║
║              ║   │ ID    │ Score    │ Hôte         │ Module    │ Statut │ Détecté │║
║              ║   ├──────┼──────────┼────────────┼──────────┼────────┼─────────┤║
║              ║   │ a1f3 │ [92] 🔴 │ PEDIATRIE-02│SENTINEL  │ Nouveau│ 14 min  │║
║              ║   ├──────┼──────────┼────────────┼──────────┼────────┼─────────┤║
║              ║   │ b2c5 │ [88] 🔴 │ URGENCES-01 │SENTINEL  │ Nouveau│ 32 min  │║
║              ║   ├──────┼──────────┼────────────┼──────────┼────────┼─────────┤║
║              ║   │ c3d7 │ [74] 🟠 │ CARDIO-05   │USB GUARD │ Nouveau│ 47 min  │║
║              ║   ├──────┼──────────┼────────────┼──────────┼────────┼─────────┤║
║              ║   │ d4e9 │ [38] 🟡 │ ADMIN-12    │ENTROPY   │ Nouveau│ 2 h     │║
║              ║   ├──────┼──────────┼────────────┼──────────┼────────┼─────────┤║
║              ║   │ e5f1 │ [22] 🔵 │ IMAGERIE-03 │GENEALOGY │ Nouveau│ 5 h     │║
║              ║   ├──────┼──────────┼────────────┼──────────┼────────┼─────────┤║
║              ║   │ f6a3 │ [18] 🔵 │ LABO-08     │EXFIL     │ Nouveau│ 7 h     │║
║              ║   ├──────┼──────────┼────────────┼──────────┼────────┼─────────┤║
║              ║   │ ...   │ ...      │ ...          │ ...       │ ...     │ ...     │║
║              ║   └──────┴──────────┴────────────┴──────────┴────────┴─────────┘║
║              ║                                                                    ║
║              ║                                  ← Précédent  Page 1/3  Suivant →║
║              ║                                  Affichage 50 par page              ║
║              ║                                                                    ║
╚══════════════╩══════════════════════════════════════════════════════════════════╝
```

### Specifications

- Search input top of page (debounce 300ms)
- Filter chips below search (each removable)
- 3 tabs: File d'attente / Mes assignées / Résolues
- Counts in tab labels (active tab uses `cmp.tabs.active`)
- Table: sortable columns (click header)
- Row click → navigate to `/alerts/:id`
- Pagination bottom, 50 per page default (configurable 10/25/50/100)
- Default sort: priority score descending (highest first)
- Empty state when no alerts match filters

### Mobile (320-768 px)

Table collapses to card list:

```
┌────────────────────────────────────────┐
│ [92] 🔴 CRITIQUE          il y a 14 min │
│ PEDIATRIE-02 · SENTINEL                  │
│ Chiffrement détecté · 12 fichiers        │
│ Statut: Nouveau                          │
│ [Voir →]                                  │
└────────────────────────────────────────┘
```

---

## 9. Low-Fi Wireframe 6 — Alert Detail

```
╔══════════════════════════════════════════════════════════════════════════════════╗
║ [🛡 RGCM]  Hôpital Régional de Yaoundé                  🔔[3] 🌐[FR] 👤 Marc T. ▼ ║
╠═════════════╦══════════════════════════════════════════════════════════════════╣
║ 📊 Tableau   ║                                                                    ║
║ 🚨 Alertes   ║   ← Retour aux alertes                                            ║
║ 💻 Agents    ║                                                                    ║
║ 📜 Audit     ║   Alerte #a1f3b8e2-...                            ⧉ Copier ID    ║
║              ║                                                                    ║
║              ║   [92] 🔴 CRITIQUE      [Nouveau]      Détecté il y a 14 min     ║
║              ║                                                                    ║
║              ║   Agent : PEDIATRIE-02 · Service Pédiatrie                        ║
║              ║   Module : SENTINEL                                                ║
║              ║                                                                    ║
║              ║   ┌──────────────────────────────────────────────────────────┐  ║
║              ║   │                                                            │  ║
║              ║   │  Prendre en charge       Marquer comme faux positif      │  ║
║              ║   │  [   Prendre en charge   ]  [  Marquer FP  ]            │  ║
║              ║   │                                                            │  ║
║              ║   │  ⚠️ Action immédiate suggérée :                            │  ║
║              ║   │  [   Isoler PEDIATRIE-02 du réseau   ]                   │  ║
║              ║   │                                                            │  ║
║              ║   └──────────────────────────────────────────────────────────┘  ║
║              ║                                                                    ║
║              ║   ╔════════════╗ ┌────────────┐ ┌───────────┐ ┌──────────────┐  ║
║              ║   ║ RÉSUMÉ      ║ │ Chronologie │ │ Artefacts │ │ Actions      │  ║
║              ║   ╚════════════╝ └────────────┘ └───────────┘ └──────────────┘  ║
║              ║                                                                    ║
║              ║   ┌──────────────────────────────────────────────────────────┐  ║
║              ║   │                                                            │  ║
║              ║   │   QUE S'EST-IL PASSÉ ?                                    │  ║
║              ║   │                                                            │  ║
║              ║   │   Le module SENTINEL a détecté qu'un fichier canary a     │  ║
║              ║   │   été modifié par powershell.exe (PID 4892) sur le poste  │  ║
║              ║   │   PEDIATRIE-02. L'analyse d'entropie a relevé une valeur  │  ║
║              ║   │   de 7.92 sur 12 fichiers en 4 secondes — un indicateur   │  ║
║              ║   │   fort de chiffrement actif par ransomware.                │  ║
║              ║   │                                                            │  ║
║              ║   │   Confiance de détection : 94% (élevée)                    │  ║
║              ║   │                                                            │  ║
║              ║   │   ─────────────────────────────────────────────────────  │  ║
║              ║   │                                                            │  ║
║              ║   │   POURQUOI EST-CE IMPORTANT ?                             │  ║
║              ║   │                                                            │  ║
║              ║   │   Ce poste est dans le service de Pédiatrie et a accès   │  ║
║              ║   │   au dossier patient. Une infection active pourrait        │  ║
║              ║   │   compromettre les données médicales et propager le        │  ║
║              ║   │   ransomware au réseau hospitalier.                         │  ║
║              ║   │                                                            │  ║
║              ║   │   ─────────────────────────────────────────────────────  │  ║
║              ║   │                                                            │  ║
║              ║   │   ACTIONS RECOMMANDÉES                                    │  ║
║              ║   │                                                            │  ║
║              ║   │   1. Isoler immédiatement le poste du réseau              │  ║
║              ║   │   2. Identifier l'utilisateur connecté                     │  ║
║              ║   │   3. Investiguer l'origine du processus PowerShell         │  ║
║              ║   │   4. Vérifier les sauvegardes du service                   │  ║
║              ║   │                                                            │  ║
║              ║   └──────────────────────────────────────────────────────────┘  ║
║              ║                                                                    ║
║              ║   ▼ Détails techniques (cliquez pour développer)                  ║
║              ║                                                                    ║
╚══════════════╩══════════════════════════════════════════════════════════════════╝
```

### Specifications

- Breadcrumb top: ← Retour
- Header section with: ID + Severity badge + Priority score + Status + Agent + Module + age
- Action buttons cluster (Acknowledge primary, Mark FP secondary, Isolate destructive)
- Tabs: Résumé (default) | Chronologie | Artefacts | Actions
- **Inverted-pyramid narrative** (per OST decision SOL A2):
  - "What happened?" (the answer, top)
  - "Why does it matter?" (impact context)
  - "Recommended actions" (call to action)
  - "Technical details" (collapsed by default)
- Confidence score visible (per OST SOL D1)

### Read-only auditor variant

- No "Prendre en charge" / "Marquer FP" / "Isoler" buttons
- Action panel replaced with: "Mode lecture seule — aucune action disponible"

### Acknowledged variant

- "Prendre en charge" button hidden
- "Fermer" button visible (primary)
- "Marquer FP" button visible (secondary)
- Header shows "En cours" badge + assignee name

### Closed variant

- All action buttons hidden
- Resolution category + notes prominently displayed
- Timeline tab shows full lifecycle

---

## 10. Low-Fi Wireframe 7 — Agents Inventory

```
╔══════════════════════════════════════════════════════════════════════════════════╗
║ [🛡 RGCM]  Hôpital Régional de Yaoundé                  🔔[3] 🌐[FR] 👤 Marc T. ▼ ║
╠═════════════╦══════════════════════════════════════════════════════════════════╣
║ 📊 Tableau   ║                                                                    ║
║ 🚨 Alertes   ║   Agents                                                           ║
║ 💻 Agents    ║                                                                    ║
║ 📜 Audit     ║   ┌──────────────────────────────────────────────────────────┐  ║
║              ║   │ 🔍 Rechercher un hôte (ex: PEDIATRIE)                     │  ║
║              ║   └──────────────────────────────────────────────────────────┘  ║
║              ║                                                                    ║
║              ║   Filtres :                                                        ║
║              ║   [Statut: Tous ▼] [OS ▼] [Heartbeat ▼] [Groupe ▼]              ║
║              ║                                                                    ║
║              ║   ┌────────────┬────────┬──────────┬────────────┬───────┬─────┐ ║
║              ║   │ Hôte        │ OS      │ Version  │ Heartbeat   │ État  │ Actions ║
║              ║   ├────────────┼────────┼──────────┼────────────┼───────┼─────┤ ║
║              ║   │ PEDIATRIE-02│ Win 10 │ v0.7.0   │ il y a 12 s│ 🟢    │ ⋮   │ ║
║              ║   ├────────────┼────────┼──────────┼────────────┼───────┼─────┤ ║
║              ║   │ CARDIO-05   │ Win 10 │ v0.7.0   │ il y a 18 s│ 🟢    │ ⋮   │ ║
║              ║   ├────────────┼────────┼──────────┼────────────┼───────┼─────┤ ║
║              ║   │ ADMIN-12    │ Win 11 │ v0.7.0   │ il y a 23 s│ 🟢    │ ⋮   │ ║
║              ║   ├────────────┼────────┼──────────┼────────────┼───────┼─────┤ ║
║              ║   │ IMAGERIE-03 │ Win 7  │ v0.6.5   │ il y a 1 m │ 🟢    │ ⋮   │ ║
║              ║   ├────────────┼────────┼──────────┼────────────┼───────┼─────┤ ║
║              ║   │ URGENCES-01 │ Win 10 │ v0.7.0   │ il y a 2 m │ 🟢    │ ⋮   │ ║
║              ║   ├────────────┼────────┼──────────┼────────────┼───────┼─────┤ ║
║              ║   │ LABO-08     │ Win 10 │ v0.7.0   │ il y a 45m │ 🟡    │ ⋮   │ ║
║              ║   ├────────────┼────────┼──────────┼────────────┼───────┼─────┤ ║
║              ║   │ PHARMA-04   │ Win 10 │ v0.6.5   │ il y a 2 h │ 🔴    │ ⋮   │ ║
║              ║   ├────────────┼────────┼──────────┼────────────┼───────┼─────┤ ║
║              ║   │ ...         │ ...    │ ...      │ ...        │ ...   │ ⋮   │ ║
║              ║   └────────────┴────────┴──────────┴────────────┴───────┴─────┘ ║
║              ║                                                                    ║
║              ║                                  ← Précédent  Page 1/2  Suivant → ║
║              ║                                  47 agents au total                ║
║              ║                                                                    ║
║              ║   Actions par lot (sélection multiple) — Sprint 8                  ║
║              ║                                                                    ║
╚══════════════╩══════════════════════════════════════════════════════════════════╝
```

### Specifications

- Search by hostname (full-text)
- Filters: Status / OS / Heartbeat age / Group
- Status dots: green (recent < 5min), yellow (stale 5-60min), red (offline > 60min), black (isolated)
- Actions menu (⋮): Voir détails / Isoler (admin only) / Désactiver (admin only)
- Bulk actions: Sprint 8 deferred per OST
- Row click → `/agents/:id`

### Row Detail on Hover (tooltip)

Hover on hostname shows tooltip with: service / department / last user logged in

---

## 11. Low-Fi Wireframe 8 — Users Management (admin only)

```
╔══════════════════════════════════════════════════════════════════════════════════╗
║ [🛡 RGCM]  Hôpital Régional de Yaoundé                  🔔[2] 🌐[FR] 👤 Dr.Amani▼ ║
╠═════════════╦══════════════════════════════════════════════════════════════════╣
║ 📊 Tableau   ║                                                                    ║
║ 🚨 Alertes   ║   Gestion des utilisateurs                                         ║
║ 💻 Agents    ║                                                  ┌──────────────┐ ║
║ 👥 Util.    5║                                                  │+ Ajouter user │ ║
║ 📜 Audit     ║                                                  └──────────────┘ ║
║ ⚙️ Paramètres║                                                                    ║
║              ║   ┌──────────────────────────────────────────────────────────┐  ║
║              ║   │ 🔍 Rechercher par nom ou email                             │  ║
║              ║   └──────────────────────────────────────────────────────────┘  ║
║              ║                                                                    ║
║              ║   Filtres :                                                        ║
║              ║   [Statut: Tous ▼] [Rôle: Tous ▼]                                 ║
║              ║                                                                    ║
║              ║   ┌─────┬────────────────┬──────────┬─────────┬────────┬─────┐  ║
║              ║   │ 👤   │ Nom complet     │ Email     │ Rôle    │ Statut │ ⋮  │  ║
║              ║   ├─────┼────────────────┼──────────┼─────────┼────────┼─────┤  ║
║              ║   │ DA  │ Dr. Amani Nkomo │ amani@... │ Admin   │ 🟢 Actif│ ⋮  │  ║
║              ║   ├─────┼────────────────┼──────────┼─────────┼────────┼─────┤  ║
║              ║   │ MT  │ Marc Tchoumi    │ marc@...  │ Analyste│ 🟢 Actif│ ⋮  │  ║
║              ║   ├─────┼────────────────┼──────────┼─────────┼────────┼─────┤  ║
║              ║   │ JN  │ Jean N.         │ jean@...  │ Analyste│ 🟢 Actif│ ⋮  │  ║
║              ║   ├─────┼────────────────┼──────────┼─────────┼────────┼─────┤  ║
║              ║   │ JB  │ Sr Jeanne Bilo'o│ jeanne@.. │ Audit   │ 🟢 Actif│ ⋮  │  ║
║              ║   ├─────┼────────────────┼──────────┼─────────┼────────┼─────┤  ║
║              ║   │ PK  │ Paul K.         │ paul@...  │ Analyste│ ⚪ Désact│ ⋮ │  ║
║              ║   └─────┴────────────────┴──────────┴─────────┴────────┴─────┘  ║
║              ║                                                                    ║
║              ║   5 utilisateurs au total                                          ║
║              ║                                                                    ║
║              ║   ─────────────────────────────────────────────────────────────  ║
║              ║                                                                    ║
║              ║   ℹ️  Vous ne pouvez pas modifier votre propre rôle.              ║
║              ║      Vous ne pouvez pas désactiver votre propre compte.           ║
║              ║                                                                    ║
╚══════════════╩══════════════════════════════════════════════════════════════════╝
```

### Specifications

- "Ajouter user" button top-right (opens FormModal)
- Search by name or email (debounce 300ms)
- Filters: Status / Role
- Actions menu (⋮) per row:
  - Voir profil
  - Modifier les rôles
  - Réinitialiser MFA (Sprint 8)
  - Désactiver (if active) / Réactiver (if disabled)
- Self-row: Modifier les rôles + Désactiver are disabled (greyed with tooltip)
- Bottom info reminder

### Add User Modal

```
┌──────────────────────────────────────────────────────┐
│  Ajouter un utilisateur                              │
├──────────────────────────────────────────────────────┤
│                                                        │
│  Nom complet *                                         │
│  ┌────────────────────────────────────────────────┐ │
│  │                                                  │ │
│  └────────────────────────────────────────────────┘ │
│                                                        │
│  Email *                                               │
│  ┌────────────────────────────────────────────────┐ │
│  │                                                  │ │
│  └────────────────────────────────────────────────┘ │
│                                                        │
│  Rôle *                                                │
│  ┌────────────────────────────────────────────────┐ │
│  │  Sélectionner un rôle              ▼            │ │
│  └────────────────────────────────────────────────┘ │
│      ◯ Administrateur Hôpital (tenant_admin)         │
│      ◯ Analyste Sécurité (security_analyst)          │
│      ◯ Auditeur Lecture seule (read_only_auditor)    │
│                                                        │
│  Mot de passe initial *                                │
│  ┌────────────────────────────────────────────────┐ │
│  │ ••••••••••••••                                  │ │
│  └────────────────────────────────────────────────┘ │
│  Force : ████████░░ (Fort)                            │
│  ≥12 caractères, majuscule, chiffre, symbole          │
│                                                        │
│  Confirmer le mot de passe *                           │
│  ┌────────────────────────────────────────────────┐ │
│  │                                                  │ │
│  └────────────────────────────────────────────────┘ │
│                                                        │
│  ┌──────────┐  ┌──────────────────┐                  │
│  │ Annuler  │  │ Créer l'utilisateur│                  │
│  └──────────┘  └──────────────────┘                  │
│                                                        │
└──────────────────────────────────────────────────────┘
```

---

## 12. Storybook Structure

### Story File Organization

```
dashboard/
└── src/
    └── components/
        ├── ui/
        │   ├── button.tsx
        │   ├── button.stories.tsx
        │   ├── badge.tsx
        │   ├── badge.stories.tsx
        │   ├── priority-score.tsx
        │   ├── priority-score.stories.tsx
        │   ├── severity-badge.tsx
        │   ├── severity-badge.stories.tsx
        │   ├── kpi-card.tsx
        │   ├── kpi-card.stories.tsx
        │   ...
        └── ...
```

### Sample Story (PriorityScore)

```typescript
// dashboard/src/components/ui/priority-score.stories.tsx

import type { Meta, StoryObj } from '@storybook/react';
import { PriorityScore } from './priority-score';

const meta = {
  title: 'UI/PriorityScore',
  component: PriorityScore,
  parameters: { layout: 'centered' },
  argTypes: {
    score: { control: { type: 'range', min: 0, max: 100 } }
  }
} satisfies Meta<typeof PriorityScore>;

export default meta;
type Story = StoryObj<typeof meta>;

export const HighPriority: Story = { args: { score: 92 } };       // Red
export const MediumPriority: Story = { args: { score: 47 } };     // Orange
export const LowPriority: Story = { args: { score: 8 } };         // Gray
export const Maximum: Story = { args: { score: 100 } };
export const Minimum: Story = { args: { score: 0 } };
export const Boundary85: Story = { args: { score: 85 } };         // Border
export const Boundary15: Story = { args: { score: 15 } };         // Border
```

### CI Visual Regression with Chromatic / Playwright Snapshots

Each story:
1. Builds in Storybook
2. Renders in Playwright
3. Captures snapshot
4. CI compares against baseline → reports diffs

---

**End of Design Days 7-8 — Component Inventory + 8 Low-Fi Wireframes**

**Next**: Design Day 9-10 — 3 Hi-Fi Mockups + Clickable Prototype Description + ADRs Frontend Architecture.
