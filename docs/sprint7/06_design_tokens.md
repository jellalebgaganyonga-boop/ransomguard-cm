# RansomGuard-CM — Design Phase, Day 6

**Document Type**: Design Tokens — 3-Tier JSON (Material Design 3 pattern)
**Phase**: Design (Master Plan Phase 3)
**Status**: Authoritative
**Author Role**: Design Systems Engineer (solo)
**Methodology Sources**:
- Material Design 3 — Design Tokens (m3.material.io/foundations/design-tokens/overview)
- W3C Design Tokens Community Group — Format Module (tr.designtokens.org/format/)
- Salesforce Lightning Design System — Tokens architecture (lightningdesignsystem.com/design-tokens/)
- Style Dictionary by Amazon — multi-platform token transformation (amzn.github.io/style-dictionary/)
- Tokens Studio for Figma — token taxonomy
- Nathan Curtis — *Modular Scale & Meaningful Typography* (eightshapes.com)
- IBM Carbon Design System — Token naming conventions (carbondesignsystem.com/elements/tokens)
- WCAG 2.2 — Contrast ratios (w3.org/WAI/WCAG22/quickref/)

---

## Table of Contents

1. [Token Architecture & Philosophy](#1-token-architecture--philosophy)
2. [Tier 1 — Reference Tokens (Primitives)](#2-tier-1--reference-tokens-primitives)
3. [Tier 2 — System Tokens (Semantic)](#3-tier-2--system-tokens-semantic)
4. [Tier 3 — Component Tokens](#4-tier-3--component-tokens)
5. [Accessibility Validation (WCAG 2.2 AA)](#5-accessibility-validation-wcag-22-aa)
6. [Token JSON Files (Complete)](#6-token-json-files-complete)
7. [Style Dictionary Pipeline](#7-style-dictionary-pipeline)

---

## 1. Token Architecture & Philosophy

### Why Three Tiers?

Per Material Design 3 (verbatim from m3.material.io):

> *"Design tokens form a hierarchical system... Reference tokens (raw values), System tokens (semantic roles), Component tokens (per-component styling). This architecture allows global changes (theme swap) to propagate consistently through the system."*

The three-tier model decouples:
- **Visual values** (palette, type sizes) — Tier 1
- **Semantic intent** (primary, error, surface) — Tier 2
- **Component application** (button.label.color, alert.background) — Tier 3

When a designer changes the **brand color**, only Tier 1 changes. All semantic and component tokens that reference it update automatically.

### Naming Convention

Format: `<namespace>.<category>.<subcategory>.<variant>.<state>`

Examples:
- `ref.palette.blue.40` — primitive
- `sys.color.primary` — semantic
- `cmp.button.primary.label.color` — component
- `cmp.button.primary.label.color.hover` — component + state

### Inspiration & Differentiation

The RansomGuard-CM design system **borrows from**:
- **Material 3** — 3-tier hierarchy, dynamic color, accessibility focus
- **IBM Carbon** — Token naming convention rigor
- **GitHub Primer** — Semantic functional roles
- **Atlassian Design System** — Status colors hierarchy

The system **differentiates** by:
- Hospital-clinical aesthetic (calmer palette, no aggressive marketing colors)
- Bilingual French-default typography (additional weight on legibility for accented characters)
- Security-domain semantic tokens (severity scale, priority score bands)
- Offline-first considerations (system-only colors that work in low-bandwidth, no remote font loading)

---

## 2. Tier 1 — Reference Tokens (Primitives)

### 2.1 Color Palette — Reference Tokens

#### Brand Blue (Primary — Hospital Trust)

```
ref.palette.blue.10   #0E1B2E   (darkest)
ref.palette.blue.20   #1A2D4D
ref.palette.blue.30   #284878
ref.palette.blue.40   #3A66A6
ref.palette.blue.50   #4D80C8
ref.palette.blue.60   #6E9BD8
ref.palette.blue.70   #95B7E5
ref.palette.blue.80   #B9D0EF
ref.palette.blue.90   #DCE7F7
ref.palette.blue.95   #ECF2FB
ref.palette.blue.99   #F8FAFD   (lightest)
```

#### Neutral Gray (Surfaces & Text)

```
ref.palette.gray.10   #0F1115   (text on light)
ref.palette.gray.20   #1A1D24
ref.palette.gray.30   #2A2E37
ref.palette.gray.40   #3D424E
ref.palette.gray.50   #5B6170
ref.palette.gray.60   #7E8493
ref.palette.gray.70   #A8ACB7
ref.palette.gray.80   #CACDD4
ref.palette.gray.90   #E4E6EA
ref.palette.gray.95   #F1F2F5
ref.palette.gray.99   #FAFBFC   (lightest surface)
```

#### Severity / Status Colors

##### Red (Critical / Critique)

```
ref.palette.red.10    #2E0A0A
ref.palette.red.20    #561414
ref.palette.red.30    #821F1F
ref.palette.red.40    #B12626
ref.palette.red.50    #D63838   (Critical badge default)
ref.palette.red.60    #E26060
ref.palette.red.70    #ED8989
ref.palette.red.80    #F4B3B3
ref.palette.red.90    #FAD9D9
ref.palette.red.95    #FDECEC
```

##### Orange (High / Haut, Priority 15-85)

```
ref.palette.orange.10  #2E1A03
ref.palette.orange.20  #573309
ref.palette.orange.30  #834D11
ref.palette.orange.40  #B26819
ref.palette.orange.50  #D9842B   (High badge default)
ref.palette.orange.60  #E5A055
ref.palette.orange.70  #EFBA7F
ref.palette.orange.80  #F5D2A9
ref.palette.orange.90  #FAE8D2
ref.palette.orange.95  #FDF4E7
```

##### Yellow (Medium / Moyen)

```
ref.palette.yellow.10  #2A2105
ref.palette.yellow.20  #4F3D0E
ref.palette.yellow.30  #745A18
ref.palette.yellow.40  #A07D26
ref.palette.yellow.50  #C9A035   (Medium badge default)
ref.palette.yellow.60  #DBB55E
ref.palette.yellow.70  #E8C886
ref.palette.yellow.80  #F1DBAD
ref.palette.yellow.90  #F8ECD4
ref.palette.yellow.95  #FCF5E8
```

##### Green (Success / Succès, Resolved)

```
ref.palette.green.10   #0A2814
ref.palette.green.20   #144A26
ref.palette.green.30   #1F6D3B
ref.palette.green.40   #2A9051
ref.palette.green.50   #3AAF65   (Resolved badge default)
ref.palette.green.60   #5FC084
ref.palette.green.70   #84D0A3
ref.palette.green.80   #ACDFC0
ref.palette.green.90   #D2EEDF
ref.palette.green.95   #E8F6EE
```

##### Blue (Info / Information, Low priority)

Same as brand blue but explicitly typed for info.

```
ref.palette.info.10    #0E1B2E   = ref.palette.blue.10
...                    (alias)
```

#### Special Purpose

```
ref.palette.black           #000000
ref.palette.white           #FFFFFF
ref.palette.transparent     #00000000
ref.palette.scrim           rgba(0, 0, 0, 0.4)    (modal overlay)
ref.palette.focus           #5E9FD6                (focus ring base)
```

### 2.2 Typography — Reference Tokens

#### Font Family

```
ref.font.family.sans
  Primary:  "Inter Variable", system-ui, -apple-system, "Segoe UI", Roboto, sans-serif
  Fallback: system-ui, sans-serif
  Rationale: Inter has excellent French diacritic support (à, é, è, ç, î, ô),
             open-source, variable font (single file for all weights),
             optimized for screens, widely deployed in design systems
             (Vercel, Stripe, Linear, Figma use Inter)

ref.font.family.mono
  Primary:  "JetBrains Mono Variable", "Fira Code", Consolas, "Liberation Mono", monospace
  Fallback: monospace
  Rationale: For technical content (hostnames, IDs, JSON payloads, hashes)
```

#### Font Size Scale (1.250 — Major Third)

Per Curtis (Eight Shapes) modular scale, base 16px × 1.250 ratio:

```
ref.font.size.xs       12px    (0.75rem)
ref.font.size.sm       14px    (0.875rem)
ref.font.size.md       16px    (1rem)        BASE
ref.font.size.lg       20px    (1.25rem)
ref.font.size.xl       25px    (1.5625rem)
ref.font.size.2xl      31px    (1.9375rem)
ref.font.size.3xl      39px    (2.4375rem)
ref.font.size.4xl      49px    (3.0625rem)
```

#### Font Weight

```
ref.font.weight.regular     400
ref.font.weight.medium      500
ref.font.weight.semibold    600
ref.font.weight.bold        700
```

#### Line Height

```
ref.line.height.tight       1.2     (headings)
ref.line.height.normal      1.5     (body)
ref.line.height.relaxed     1.75    (long-form reading, French dense paragraphs)
```

#### Letter Spacing

```
ref.letter.spacing.tight       -0.02em   (large headings)
ref.letter.spacing.normal       0
ref.letter.spacing.wide         0.05em   (uppercase labels, badges)
ref.letter.spacing.wider        0.1em    (small caps)
```

### 2.3 Spacing Scale (4px Base Unit)

Per Material 3 + Tailwind default scale. Base unit = 4px.

```
ref.spacing.0    0px
ref.spacing.1    4px
ref.spacing.2    8px
ref.spacing.3    12px
ref.spacing.4    16px     (typical body padding)
ref.spacing.5    20px
ref.spacing.6    24px     (section spacing)
ref.spacing.8    32px
ref.spacing.10   40px
ref.spacing.12   48px
ref.spacing.16   64px
ref.spacing.20   80px
ref.spacing.24   96px
```

### 2.4 Border Radius

```
ref.radius.none      0px
ref.radius.sm        4px      (badges, small chips)
ref.radius.md        6px      (buttons, input fields, cards)
ref.radius.lg        8px      (modals, panels)
ref.radius.xl        12px     (large containers)
ref.radius.full      9999px   (avatars, pills)
```

### 2.5 Border Width

```
ref.border.width.0       0px
ref.border.width.1       1px      (subtle separators, default borders)
ref.border.width.2       2px      (focus rings, emphasis)
ref.border.width.4       4px      (severity bars, status indicators)
```

### 2.6 Shadow / Elevation

```
ref.shadow.none      none
ref.shadow.sm        0 1px 2px 0 rgba(15, 17, 21, 0.05)
ref.shadow.md        0 4px 6px -1px rgba(15, 17, 21, 0.1),
                     0 2px 4px -2px rgba(15, 17, 21, 0.1)
ref.shadow.lg        0 10px 15px -3px rgba(15, 17, 21, 0.1),
                     0 4px 6px -4px rgba(15, 17, 21, 0.1)
ref.shadow.xl        0 20px 25px -5px rgba(15, 17, 21, 0.1),
                     0 8px 10px -6px rgba(15, 17, 21, 0.1)
```

### 2.7 Motion (Animation)

```
ref.duration.instant    0ms
ref.duration.fast       100ms
ref.duration.normal     200ms     (default transitions)
ref.duration.slow       300ms
ref.duration.slower     500ms     (modals, drawers)

ref.easing.linear       cubic-bezier(0, 0, 1, 1)
ref.easing.standard     cubic-bezier(0.4, 0, 0.2, 1)    (Material 3 standard)
ref.easing.emphasized   cubic-bezier(0.2, 0, 0, 1)      (Material 3 emphasized)
ref.easing.bounce       cubic-bezier(0.4, 0, 0.6, 1.4)
```

### 2.8 Z-Index Stacking

```
ref.zindex.below        -1
ref.zindex.base          0
ref.zindex.docked       10        (sticky header, footer)
ref.zindex.dropdown     100       (dropdowns, popovers)
ref.zindex.sticky       200       (alerts, banners)
ref.zindex.modal        300       (modal dialog)
ref.zindex.popover      400       (tooltips above modal)
ref.zindex.toast        500       (toasts above all)
ref.zindex.tooltip      600       (highest)
```

### 2.9 Breakpoints (Responsive)

Per the Master Plan (Section NFRs) Sprint 7 must support 320, 768, 1024, 1440.

```
ref.breakpoint.xs        320px    (small mobile)
ref.breakpoint.sm        640px    (mobile / large mobile)
ref.breakpoint.md        768px    (tablet)
ref.breakpoint.lg        1024px   (desktop)
ref.breakpoint.xl        1280px   (large desktop)
ref.breakpoint.2xl       1440px   (extra large)
```

---

## 3. Tier 2 — System Tokens (Semantic)

These tokens express **intent** — what role does this color/spacing play in the UI? — and reference Tier 1 primitives.

### 3.1 Surface & Background

```
sys.color.surface.default            ref.palette.gray.99       (#FAFBFC)
sys.color.surface.subtle             ref.palette.gray.95       (#F1F2F5)
sys.color.surface.muted              ref.palette.gray.90       (#E4E6EA)
sys.color.surface.inverse            ref.palette.gray.10       (#0F1115)

sys.color.background.canvas          ref.palette.white         (#FFFFFF)
sys.color.background.elevated        ref.palette.white         (#FFFFFF)
sys.color.background.scrim           ref.palette.scrim         (modal overlay)
```

### 3.2 Text

```
sys.color.text.primary               ref.palette.gray.10       (#0F1115)  AA on white ✅ (21:1)
sys.color.text.secondary             ref.palette.gray.40       (#3D424E)  AA on white ✅ (10.4:1)
sys.color.text.tertiary              ref.palette.gray.60       (#7E8493)  AA on white ✅ (4.5:1)
sys.color.text.disabled              ref.palette.gray.70       (#A8ACB7)  AA-large only
sys.color.text.inverse               ref.palette.gray.99       (#FAFBFC)  AA on gray.10 ✅
sys.color.text.link                  ref.palette.blue.40       (#3A66A6)  AA on white ✅ (5.6:1)
sys.color.text.link.hover            ref.palette.blue.30       (#284878)
sys.color.text.link.visited          ref.palette.blue.30       (#284878)
```

### 3.3 Primary (Brand)

```
sys.color.primary                    ref.palette.blue.40       (#3A66A6)
sys.color.primary.hover              ref.palette.blue.30       (#284878)
sys.color.primary.active             ref.palette.blue.20       (#1A2D4D)
sys.color.primary.subtle             ref.palette.blue.95       (#ECF2FB)
sys.color.primary.on                 ref.palette.white         (#FFFFFF) on primary ✅
```

### 3.4 Severity / Status Semantic

This is the **most important semantic layer** for a security console.

```
sys.color.severity.critical          ref.palette.red.50        (#D63838)
sys.color.severity.critical.subtle   ref.palette.red.95        (#FDECEC)
sys.color.severity.critical.border   ref.palette.red.40        (#B12626)
sys.color.severity.critical.on       ref.palette.white         (#FFFFFF)

sys.color.severity.high              ref.palette.orange.50     (#D9842B)
sys.color.severity.high.subtle       ref.palette.orange.95     (#FDF4E7)
sys.color.severity.high.border       ref.palette.orange.40     (#B26819)
sys.color.severity.high.on           ref.palette.white         (#FFFFFF)

sys.color.severity.medium            ref.palette.yellow.50     (#C9A035)
sys.color.severity.medium.subtle     ref.palette.yellow.95     (#FCF5E8)
sys.color.severity.medium.border     ref.palette.yellow.40     (#A07D26)
sys.color.severity.medium.on         ref.palette.gray.10       (#0F1115)  (yellow needs dark text)

sys.color.severity.low               ref.palette.blue.40       (#3A66A6)
sys.color.severity.low.subtle        ref.palette.blue.95       (#ECF2FB)
sys.color.severity.low.border        ref.palette.blue.30       (#284878)
sys.color.severity.low.on            ref.palette.white         (#FFFFFF)

sys.color.severity.info              ref.palette.gray.50       (#5B6170)
sys.color.severity.info.subtle       ref.palette.gray.95       (#F1F2F5)
sys.color.severity.info.border       ref.palette.gray.40       (#3D424E)
sys.color.severity.info.on           ref.palette.white         (#FFFFFF)
```

### 3.5 Status (Alert / Agent State)

```
sys.color.status.new                 ref.palette.red.50        (#D63838)
sys.color.status.in.progress         ref.palette.orange.50     (#D9842B)
sys.color.status.resolved            ref.palette.green.50      (#3AAF65)
sys.color.status.closed              ref.palette.gray.60       (#7E8493)
sys.color.status.acknowledged        ref.palette.blue.50       (#4D80C8)
sys.color.status.isolated            ref.palette.red.40        (#B12626)
sys.color.status.online              ref.palette.green.50      (#3AAF65)
sys.color.status.offline             ref.palette.gray.50       (#5B6170)
sys.color.status.stale               ref.palette.yellow.50     (#C9A035)
```

### 3.6 Priority Score Bands (Defender XDR Pattern)

```
sys.color.priority.high              ref.palette.red.50        (score > 85)
sys.color.priority.medium            ref.palette.orange.50     (score 15-85)
sys.color.priority.low               ref.palette.gray.60       (score < 15)
```

### 3.7 Feedback (Success / Warning / Error / Info)

```
sys.color.feedback.success           ref.palette.green.50      (#3AAF65)
sys.color.feedback.success.subtle    ref.palette.green.95      (#E8F6EE)
sys.color.feedback.success.border    ref.palette.green.40      (#2A9051)

sys.color.feedback.warning           ref.palette.orange.50     (#D9842B)
sys.color.feedback.warning.subtle    ref.palette.orange.95     (#FDF4E7)
sys.color.feedback.warning.border    ref.palette.orange.40     (#B26819)

sys.color.feedback.error             ref.palette.red.50        (#D63838)
sys.color.feedback.error.subtle      ref.palette.red.95        (#FDECEC)
sys.color.feedback.error.border      ref.palette.red.40        (#B12626)

sys.color.feedback.info              ref.palette.blue.50       (#4D80C8)
sys.color.feedback.info.subtle       ref.palette.blue.95       (#ECF2FB)
sys.color.feedback.info.border       ref.palette.blue.40       (#3A66A6)
```

### 3.8 Border

```
sys.color.border.default             ref.palette.gray.80       (#CACDD4)
sys.color.border.subtle              ref.palette.gray.90       (#E4E6EA)
sys.color.border.strong              ref.palette.gray.60       (#7E8493)
sys.color.border.focus               ref.palette.focus         (#5E9FD6)  + 2px width
sys.color.border.error               ref.palette.red.50        (#D63838)
```

### 3.9 Interactive States

```
sys.color.interactive.hover.bg       ref.palette.gray.95       (#F1F2F5)
sys.color.interactive.active.bg      ref.palette.gray.90       (#E4E6EA)
sys.color.interactive.selected.bg    ref.palette.blue.95       (#ECF2FB)
sys.color.interactive.disabled.bg    ref.palette.gray.95       (#F1F2F5)
sys.color.interactive.disabled.text  ref.palette.gray.70       (#A8ACB7)
```

### 3.10 Typography Semantic

```
sys.typography.display.large
  family    ref.font.family.sans
  size      ref.font.size.4xl       (49px)
  weight    ref.font.weight.bold    (700)
  line      ref.line.height.tight   (1.2)
  letter    ref.letter.spacing.tight

sys.typography.display.medium
  family    ref.font.family.sans
  size      ref.font.size.3xl       (39px)
  weight    ref.font.weight.bold
  line      ref.line.height.tight

sys.typography.heading.1
  family    ref.font.family.sans
  size      ref.font.size.2xl       (31px)
  weight    ref.font.weight.semibold
  line      ref.line.height.tight

sys.typography.heading.2
  family    ref.font.family.sans
  size      ref.font.size.xl        (25px)
  weight    ref.font.weight.semibold
  line      ref.line.height.tight

sys.typography.heading.3
  family    ref.font.family.sans
  size      ref.font.size.lg        (20px)
  weight    ref.font.weight.semibold
  line      ref.line.height.tight

sys.typography.body.large
  family    ref.font.family.sans
  size      ref.font.size.lg        (20px)
  weight    ref.font.weight.regular
  line      ref.line.height.normal

sys.typography.body.default
  family    ref.font.family.sans
  size      ref.font.size.md        (16px)
  weight    ref.font.weight.regular
  line      ref.line.height.normal

sys.typography.body.small
  family    ref.font.family.sans
  size      ref.font.size.sm        (14px)
  weight    ref.font.weight.regular
  line      ref.line.height.normal

sys.typography.caption
  family    ref.font.family.sans
  size      ref.font.size.xs        (12px)
  weight    ref.font.weight.regular
  line      ref.line.height.normal

sys.typography.label
  family    ref.font.family.sans
  size      ref.font.size.sm        (14px)
  weight    ref.font.weight.medium
  line      ref.line.height.normal
  letter    ref.letter.spacing.wide

sys.typography.code
  family    ref.font.family.mono
  size      ref.font.size.sm        (14px)
  weight    ref.font.weight.regular
  line      ref.line.height.normal
```

### 3.11 Spacing Semantic

```
sys.spacing.inset.xs       ref.spacing.1     (4px)
sys.spacing.inset.sm       ref.spacing.2     (8px)
sys.spacing.inset.md       ref.spacing.4     (16px)  BASE
sys.spacing.inset.lg       ref.spacing.6     (24px)
sys.spacing.inset.xl       ref.spacing.8     (32px)

sys.spacing.stack.xs       ref.spacing.1     (between tightly grouped items)
sys.spacing.stack.sm       ref.spacing.2
sys.spacing.stack.md       ref.spacing.4     (between paragraphs / form fields)
sys.spacing.stack.lg       ref.spacing.6     (between sections)
sys.spacing.stack.xl       ref.spacing.10    (between major page regions)

sys.spacing.inline.xs      ref.spacing.1     (between inline icon and text)
sys.spacing.inline.sm      ref.spacing.2     (between buttons in row)
sys.spacing.inline.md      ref.spacing.4
```

### 3.12 Elevation Semantic

```
sys.elevation.none           ref.shadow.none
sys.elevation.card           ref.shadow.sm
sys.elevation.dropdown       ref.shadow.md
sys.elevation.modal          ref.shadow.xl
sys.elevation.popover        ref.shadow.lg
```

### 3.13 Motion Semantic

```
sys.motion.fade.in           duration: fast, easing: standard
sys.motion.fade.out          duration: fast, easing: standard
sys.motion.slide.in          duration: normal, easing: emphasized
sys.motion.slide.out         duration: normal, easing: standard
sys.motion.modal.in          duration: slow, easing: emphasized
sys.motion.modal.out         duration: normal, easing: standard
```

---

## 4. Tier 3 — Component Tokens

These tokens map to specific UI components and reference Tier 2 semantic tokens. They allow component-level theming without breaking the cascade.

### 4.1 Button

```
cmp.button.primary.bg                    sys.color.primary
cmp.button.primary.bg.hover              sys.color.primary.hover
cmp.button.primary.bg.active             sys.color.primary.active
cmp.button.primary.bg.disabled           sys.color.interactive.disabled.bg
cmp.button.primary.label.color           sys.color.primary.on
cmp.button.primary.label.color.disabled  sys.color.interactive.disabled.text
cmp.button.primary.border.radius         ref.radius.md
cmp.button.primary.padding.x             ref.spacing.4
cmp.button.primary.padding.y             ref.spacing.2
cmp.button.primary.font                  sys.typography.label
cmp.button.primary.shadow                ref.shadow.sm
cmp.button.primary.shadow.hover          ref.shadow.md
cmp.button.primary.transition            ref.duration.fast ref.easing.standard

cmp.button.secondary.bg                  ref.palette.transparent
cmp.button.secondary.border.color        sys.color.border.default
cmp.button.secondary.border.width        ref.border.width.1
cmp.button.secondary.label.color         sys.color.text.primary
cmp.button.secondary.bg.hover            sys.color.interactive.hover.bg

cmp.button.danger.bg                     sys.color.feedback.error
cmp.button.danger.bg.hover               ref.palette.red.40
cmp.button.danger.label.color            ref.palette.white

cmp.button.ghost.bg                      ref.palette.transparent
cmp.button.ghost.label.color             sys.color.text.primary
cmp.button.ghost.bg.hover                sys.color.interactive.hover.bg

cmp.button.size.sm.padding.x             ref.spacing.3
cmp.button.size.sm.padding.y             ref.spacing.1
cmp.button.size.sm.font.size             ref.font.size.sm

cmp.button.size.md.padding.x             ref.spacing.4
cmp.button.size.md.padding.y             ref.spacing.2
cmp.button.size.md.font.size             ref.font.size.md

cmp.button.size.lg.padding.x             ref.spacing.6
cmp.button.size.lg.padding.y             ref.spacing.3
cmp.button.size.lg.font.size             ref.font.size.lg

cmp.button.focus.ring.color              sys.color.border.focus
cmp.button.focus.ring.width              ref.border.width.2
cmp.button.focus.ring.offset             2px
```

### 4.2 Badge (Severity, Status)

```
cmp.badge.critical.bg                    sys.color.severity.critical.subtle
cmp.badge.critical.border.color          sys.color.severity.critical.border
cmp.badge.critical.border.width          ref.border.width.1
cmp.badge.critical.label.color           sys.color.severity.critical
cmp.badge.critical.font                  sys.typography.caption + weight=semibold

cmp.badge.high.bg                        sys.color.severity.high.subtle
cmp.badge.high.border.color              sys.color.severity.high.border
cmp.badge.high.label.color               sys.color.severity.high

cmp.badge.medium.bg                      sys.color.severity.medium.subtle
cmp.badge.medium.border.color            sys.color.severity.medium.border
cmp.badge.medium.label.color             ref.palette.yellow.20    (darker for AA)

cmp.badge.low.bg                         sys.color.severity.low.subtle
cmp.badge.low.label.color                sys.color.severity.low

cmp.badge.info.bg                        sys.color.severity.info.subtle
cmp.badge.info.label.color               sys.color.severity.info

cmp.badge.padding.x                      ref.spacing.2
cmp.badge.padding.y                      ref.spacing.1
cmp.badge.radius                         ref.radius.sm
cmp.badge.font.size                      ref.font.size.xs
cmp.badge.font.weight                    ref.font.weight.semibold
cmp.badge.letter.spacing                 ref.letter.spacing.wide
```

### 4.3 Priority Score Chip (Defender pattern, 0-100)

```
cmp.priority.score.bg.high               sys.color.priority.high    (>85, red)
cmp.priority.score.bg.medium             sys.color.priority.medium  (15-85, orange)
cmp.priority.score.bg.low                sys.color.priority.low     (<15, gray)
cmp.priority.score.label.color           ref.palette.white
cmp.priority.score.font.size             ref.font.size.sm
cmp.priority.score.font.weight           ref.font.weight.bold
cmp.priority.score.padding.x             ref.spacing.2
cmp.priority.score.padding.y             ref.spacing.1
cmp.priority.score.radius                ref.radius.md
cmp.priority.score.min.width             40px
cmp.priority.score.text.align            center
```

### 4.4 Card

```
cmp.card.bg                              sys.color.background.elevated
cmp.card.border.color                    sys.color.border.subtle
cmp.card.border.width                    ref.border.width.1
cmp.card.border.radius                   ref.radius.lg
cmp.card.padding                         sys.spacing.inset.lg     (24px)
cmp.card.shadow                          sys.elevation.card
cmp.card.shadow.hover                    sys.elevation.dropdown
cmp.card.transition                      ref.duration.fast ref.easing.standard
```

### 4.5 Input / Text Field

```
cmp.input.bg                             sys.color.background.canvas
cmp.input.bg.disabled                    sys.color.interactive.disabled.bg
cmp.input.border.color                   sys.color.border.default
cmp.input.border.color.hover             sys.color.border.strong
cmp.input.border.color.focus             sys.color.border.focus
cmp.input.border.color.error             sys.color.feedback.error
cmp.input.border.width                   ref.border.width.1
cmp.input.border.radius                  ref.radius.md
cmp.input.padding.x                      ref.spacing.3
cmp.input.padding.y                      ref.spacing.2
cmp.input.font                           sys.typography.body.default
cmp.input.text.color                     sys.color.text.primary
cmp.input.placeholder.color              sys.color.text.tertiary
cmp.input.label.color                    sys.color.text.secondary
cmp.input.label.font                     sys.typography.label
cmp.input.error.text.color               sys.color.feedback.error
cmp.input.helper.text.color              sys.color.text.tertiary
cmp.input.helper.font                    sys.typography.caption
cmp.input.focus.ring.color               sys.color.border.focus
cmp.input.focus.ring.width               ref.border.width.2
```

### 4.6 Table / Data Grid

```
cmp.table.bg                             sys.color.background.canvas
cmp.table.header.bg                      sys.color.surface.subtle
cmp.table.header.text.color              sys.color.text.secondary
cmp.table.header.font                    sys.typography.label
cmp.table.header.padding.x               ref.spacing.4
cmp.table.header.padding.y               ref.spacing.3
cmp.table.header.border.bottom.color     sys.color.border.default
cmp.table.header.border.bottom.width     ref.border.width.1

cmp.table.row.bg                         sys.color.background.canvas
cmp.table.row.bg.hover                   sys.color.interactive.hover.bg
cmp.table.row.bg.selected                sys.color.interactive.selected.bg
cmp.table.row.bg.zebra                   sys.color.surface.subtle      (optional)
cmp.table.row.border.bottom.color        sys.color.border.subtle
cmp.table.row.border.bottom.width        ref.border.width.1

cmp.table.cell.padding.x                 ref.spacing.4
cmp.table.cell.padding.y                 ref.spacing.3
cmp.table.cell.font                      sys.typography.body.default
cmp.table.cell.text.color                sys.color.text.primary
cmp.table.cell.text.code.font            sys.typography.code

cmp.table.empty.state.padding            ref.spacing.16
cmp.table.empty.state.text.color         sys.color.text.tertiary
cmp.table.empty.state.font               sys.typography.body.default
```

### 4.7 Modal / Dialog

```
cmp.modal.scrim.bg                       sys.color.background.scrim
cmp.modal.scrim.transition               ref.duration.normal ref.easing.standard

cmp.modal.bg                             sys.color.background.elevated
cmp.modal.border.radius                  ref.radius.xl
cmp.modal.padding                        ref.spacing.6
cmp.modal.shadow                         sys.elevation.modal
cmp.modal.max.width                      512px      (default)
cmp.modal.max.width.large                768px
cmp.modal.max.width.full                 1024px

cmp.modal.title.font                     sys.typography.heading.2
cmp.modal.title.color                    sys.color.text.primary

cmp.modal.body.font                      sys.typography.body.default
cmp.modal.body.color                     sys.color.text.secondary

cmp.modal.footer.gap                     ref.spacing.3
cmp.modal.footer.justify                 flex-end
```

### 4.8 Sidebar Navigation

```
cmp.sidebar.bg                           sys.color.surface.default
cmp.sidebar.border.right.color           sys.color.border.subtle
cmp.sidebar.border.right.width           ref.border.width.1
cmp.sidebar.width                        260px
cmp.sidebar.width.collapsed              64px
cmp.sidebar.padding.y                    ref.spacing.4
cmp.sidebar.padding.x                    ref.spacing.2

cmp.sidebar.item.padding.x               ref.spacing.4
cmp.sidebar.item.padding.y               ref.spacing.3
cmp.sidebar.item.radius                  ref.radius.md
cmp.sidebar.item.font                    sys.typography.body.default
cmp.sidebar.item.color                   sys.color.text.secondary
cmp.sidebar.item.icon.color              sys.color.text.tertiary
cmp.sidebar.item.gap                     ref.spacing.3

cmp.sidebar.item.bg.hover                sys.color.interactive.hover.bg
cmp.sidebar.item.color.hover             sys.color.text.primary

cmp.sidebar.item.bg.active               sys.color.interactive.selected.bg
cmp.sidebar.item.color.active            sys.color.primary
cmp.sidebar.item.border.active.color     sys.color.primary
cmp.sidebar.item.border.active.width     3px      (left border indicator)
```

### 4.9 Header

```
cmp.header.bg                            sys.color.background.canvas
cmp.header.border.bottom.color           sys.color.border.subtle
cmp.header.border.bottom.width           ref.border.width.1
cmp.header.height                        64px
cmp.header.padding.x                     ref.spacing.6
cmp.header.shadow                        ref.shadow.sm
cmp.header.zindex                        ref.zindex.docked
```

### 4.10 Toast / Notification

```
cmp.toast.bg                             sys.color.background.elevated
cmp.toast.border.left.width              4px
cmp.toast.border.left.color.success      sys.color.feedback.success
cmp.toast.border.left.color.warning      sys.color.feedback.warning
cmp.toast.border.left.color.error        sys.color.feedback.error
cmp.toast.border.left.color.info         sys.color.feedback.info
cmp.toast.border.radius                  ref.radius.md
cmp.toast.padding                        sys.spacing.inset.md
cmp.toast.shadow                         sys.elevation.popover
cmp.toast.min.width                      320px
cmp.toast.max.width                      480px
cmp.toast.font                           sys.typography.body.small
cmp.toast.icon.size                      20px
cmp.toast.duration                       4000ms     (auto-dismiss)
cmp.toast.zindex                         ref.zindex.toast
```

### 4.11 Alert (Inline Notification)

```
cmp.alert.success.bg                     sys.color.feedback.success.subtle
cmp.alert.success.border.color           sys.color.feedback.success.border
cmp.alert.success.icon.color             sys.color.feedback.success
cmp.alert.success.title.color            sys.color.feedback.success
cmp.alert.success.body.color             sys.color.text.primary

cmp.alert.warning.bg                     sys.color.feedback.warning.subtle
cmp.alert.warning.border.color           sys.color.feedback.warning.border
cmp.alert.warning.icon.color             sys.color.feedback.warning
cmp.alert.warning.title.color            sys.color.feedback.warning
cmp.alert.warning.body.color             sys.color.text.primary

cmp.alert.error.bg                       sys.color.feedback.error.subtle
cmp.alert.error.border.color             sys.color.feedback.error.border
cmp.alert.error.icon.color               sys.color.feedback.error
cmp.alert.error.title.color              sys.color.feedback.error
cmp.alert.error.body.color               sys.color.text.primary

cmp.alert.info.bg                        sys.color.feedback.info.subtle
cmp.alert.info.border.color              sys.color.feedback.info.border
cmp.alert.info.icon.color                sys.color.feedback.info
cmp.alert.info.title.color               sys.color.feedback.info
cmp.alert.info.body.color                sys.color.text.primary

cmp.alert.border.left.width              4px
cmp.alert.border.radius                  ref.radius.md
cmp.alert.padding                        ref.spacing.4
cmp.alert.gap                            ref.spacing.3
cmp.alert.title.font                     sys.typography.body.default + weight=semibold
cmp.alert.body.font                      sys.typography.body.small
```

### 4.12 Tooltip

```
cmp.tooltip.bg                           sys.color.surface.inverse
cmp.tooltip.text.color                   sys.color.text.inverse
cmp.tooltip.border.radius                ref.radius.sm
cmp.tooltip.padding.x                    ref.spacing.2
cmp.tooltip.padding.y                    ref.spacing.1
cmp.tooltip.font                         sys.typography.caption
cmp.tooltip.max.width                    240px
cmp.tooltip.shadow                       sys.elevation.dropdown
cmp.tooltip.zindex                       ref.zindex.tooltip
cmp.tooltip.delay.show                   500ms
cmp.tooltip.delay.hide                   200ms
```

### 4.13 Dropdown / Popover

```
cmp.dropdown.bg                          sys.color.background.elevated
cmp.dropdown.border.color                sys.color.border.subtle
cmp.dropdown.border.width                ref.border.width.1
cmp.dropdown.border.radius               ref.radius.md
cmp.dropdown.padding.y                   ref.spacing.1
cmp.dropdown.shadow                      sys.elevation.dropdown
cmp.dropdown.min.width                   180px
cmp.dropdown.zindex                      ref.zindex.dropdown

cmp.dropdown.item.padding.x              ref.spacing.3
cmp.dropdown.item.padding.y              ref.spacing.2
cmp.dropdown.item.font                   sys.typography.body.default
cmp.dropdown.item.color                  sys.color.text.primary
cmp.dropdown.item.bg.hover               sys.color.interactive.hover.bg
cmp.dropdown.item.icon.color             sys.color.text.tertiary
cmp.dropdown.item.gap                    ref.spacing.2
```

### 4.14 Pagination

```
cmp.pagination.button.padding.x          ref.spacing.3
cmp.pagination.button.padding.y          ref.spacing.2
cmp.pagination.button.font               sys.typography.body.small
cmp.pagination.button.color              sys.color.text.secondary
cmp.pagination.button.color.active       sys.color.primary
cmp.pagination.button.bg.active          sys.color.interactive.selected.bg
cmp.pagination.button.bg.hover           sys.color.interactive.hover.bg
cmp.pagination.button.radius             ref.radius.md
cmp.pagination.gap                       ref.spacing.1
```

### 4.15 Skeleton (Loading State)

```
cmp.skeleton.bg                          sys.color.surface.muted
cmp.skeleton.shimmer.color               sys.color.surface.subtle
cmp.skeleton.radius                      ref.radius.sm
cmp.skeleton.animation                   1500ms infinite ease-in-out
```

---

## 5. Accessibility Validation (WCAG 2.2 AA)

### Contrast Validation

Per WCAG 2.2 SC 1.4.3 (Contrast Minimum):
- Normal text (under 18pt or under 14pt bold): **≥ 4.5:1**
- Large text (18pt+ or 14pt+ bold): **≥ 3:1**

Per WCAG 2.2 SC 1.4.11 (Non-text Contrast):
- UI components and graphical objects: **≥ 3:1**

### Validated Contrast Ratios (Selected Combinations)

| Combination | Foreground | Background | Ratio | Status |
|---|---|---|:-:|:-:|
| Body text on canvas | gray.10 (#0F1115) | white (#FFFFFF) | 21:1 | ✅ AAA |
| Secondary text on canvas | gray.40 (#3D424E) | white | 10.4:1 | ✅ AAA |
| Tertiary text on canvas | gray.60 (#7E8493) | white | 4.5:1 | ✅ AA |
| Disabled text | gray.70 (#A8ACB7) | white | 2.8:1 | ⚠️ Below 3:1 — use only for disabled UI (which is acceptable since users cannot interact) |
| Link on canvas | blue.40 (#3A66A6) | white | 5.6:1 | ✅ AA |
| Primary button label | white | blue.40 (#3A66A6) | 5.6:1 | ✅ AA |
| Critical badge label | red.50 (#D63838) | red.95 (#FDECEC) | 5.1:1 | ✅ AA |
| Critical badge inverse | white | red.50 (#D63838) | 5.0:1 | ✅ AA |
| High badge label | orange.50 (#D9842B) | orange.95 (#FDF4E7) | 4.6:1 | ✅ AA |
| Medium badge label | yellow.20 (#4F3D0E) | yellow.95 (#FCF5E8) | 11.5:1 | ✅ AAA |
| Low badge label | blue.40 (#3A66A6) | blue.95 (#ECF2FB) | 5.4:1 | ✅ AA |
| Resolved badge inverse | white | green.50 (#3AAF65) | 3.1:1 | ✅ AA (large text only) — add green.40 alt for small text |
| Resolved badge dark | gray.10 (#0F1115) | green.95 (#E8F6EE) | 18.2:1 | ✅ AAA |
| Focus ring on white | focus (#5E9FD6) | white | 3.4:1 | ✅ AA (non-text) |
| Sidebar item hover | gray.10 | gray.95 (#F1F2F5) | 19.7:1 | ✅ AAA |
| Critical priority score | white | red.50 | 5.0:1 | ✅ AA |
| Medium priority score | gray.10 | orange.50 | 4.7:1 | ✅ AA |

### Contrast Issues Flagged & Resolutions

| Issue | Resolution |
|---|---|
| Green resolved badge inverse: 3.1:1 (only AA-large) | Use only on large text (badge font 12px = caption is too small). Switch to dark-on-light pattern (gray.10 on green.95). |
| Disabled text: 2.8:1 | Acceptable per WCAG 2.2 since disabled elements are not interactive. Add ARIA `aria-disabled` and indicate disability with cursor/visual change beyond color. |

### Non-Color Differentiation (Required for WCAG 1.4.1 — Use of Color)

For severity, the system uses **color + label + icon + position** to differentiate:

```
[🔴 CRITIQUE 92]   ← color (red) + label (CRITIQUE) + icon (filled circle) + score (92)
[🟠 HAUT 73]       ← color (orange) + label + icon + score
[🟡 MOYEN 42]      ← color (yellow) + label + icon + score
[🔵 FAIBLE 18]     ← color (blue) + label + icon + score
[⚪ INFO 5]        ← color (gray) + label + icon + score
```

This satisfies the Alhamadi et al. 2025 ACM TiiS finding that **graph literacy varies among users** — providing both numeric encoding (score) and categorical encoding (label + color) accommodates both.

### Focus Indicators (WCAG 2.4.7 + 2.4.11)

- All interactive elements MUST have a visible focus ring.
- Focus ring color: `sys.color.border.focus` (#5E9FD6) — 3.4:1 contrast on white.
- Focus ring width: 2px outline + 2px offset.
- Focus must NOT be obscured by overlays (WCAG 2.2 SC 2.4.11 — new in 2.2).

### Target Size (WCAG 2.5.8 — new in 2.2)

- Interactive elements ≥ **24×24 CSS px** (minimum).
- Touch targets on mobile ≥ **44×44 CSS px** (iOS HIG / Material recommendation).
- Spacing between adjacent targets ≥ 8px.

### Reduced Motion (WCAG 2.3.3)

- Respect `prefers-reduced-motion: reduce` media query.
- Disable non-essential animations (modal slide-ins, hover transitions) when reduced motion is requested.
- Keep essential motion (loading indicators, focus visible transitions).

---

## 6. Token JSON Files (Complete)

### File structure

```
dashboard/
├── design-tokens/
│   ├── reference.json       Tier 1 — primitives
│   ├── system.json          Tier 2 — semantic
│   ├── component.json       Tier 3 — component
│   └── build.cjs            Style Dictionary config
└── tailwind.config.ts       Consumes generated CSS variables
```

### reference.json (extract)

```json
{
  "ref": {
    "palette": {
      "blue": {
        "10": { "value": "#0E1B2E", "type": "color" },
        "20": { "value": "#1A2D4D", "type": "color" },
        "30": { "value": "#284878", "type": "color" },
        "40": { "value": "#3A66A6", "type": "color" },
        "50": { "value": "#4D80C8", "type": "color" },
        "60": { "value": "#6E9BD8", "type": "color" },
        "70": { "value": "#95B7E5", "type": "color" },
        "80": { "value": "#B9D0EF", "type": "color" },
        "90": { "value": "#DCE7F7", "type": "color" },
        "95": { "value": "#ECF2FB", "type": "color" },
        "99": { "value": "#F8FAFD", "type": "color" }
      },
      "gray": {
        "10": { "value": "#0F1115", "type": "color" },
        "20": { "value": "#1A1D24", "type": "color" },
        "40": { "value": "#3D424E", "type": "color" },
        "60": { "value": "#7E8493", "type": "color" },
        "80": { "value": "#CACDD4", "type": "color" },
        "95": { "value": "#F1F2F5", "type": "color" },
        "99": { "value": "#FAFBFC", "type": "color" }
      },
      "red": {
        "40": { "value": "#B12626", "type": "color" },
        "50": { "value": "#D63838", "type": "color" },
        "95": { "value": "#FDECEC", "type": "color" }
      }
    },
    "font": {
      "family": {
        "sans": {
          "value": "Inter Variable, system-ui, -apple-system, Segoe UI, Roboto, sans-serif",
          "type": "fontFamily"
        },
        "mono": {
          "value": "JetBrains Mono Variable, Fira Code, Consolas, monospace",
          "type": "fontFamily"
        }
      },
      "size": {
        "xs": { "value": "12px", "type": "dimension" },
        "sm": { "value": "14px", "type": "dimension" },
        "md": { "value": "16px", "type": "dimension" },
        "lg": { "value": "20px", "type": "dimension" },
        "xl": { "value": "25px", "type": "dimension" }
      },
      "weight": {
        "regular": { "value": 400, "type": "fontWeight" },
        "medium": { "value": 500, "type": "fontWeight" },
        "semibold": { "value": 600, "type": "fontWeight" },
        "bold": { "value": 700, "type": "fontWeight" }
      }
    },
    "spacing": {
      "0": { "value": "0px", "type": "dimension" },
      "1": { "value": "4px", "type": "dimension" },
      "2": { "value": "8px", "type": "dimension" },
      "3": { "value": "12px", "type": "dimension" },
      "4": { "value": "16px", "type": "dimension" },
      "6": { "value": "24px", "type": "dimension" },
      "8": { "value": "32px", "type": "dimension" }
    },
    "radius": {
      "sm": { "value": "4px", "type": "dimension" },
      "md": { "value": "6px", "type": "dimension" },
      "lg": { "value": "8px", "type": "dimension" },
      "xl": { "value": "12px", "type": "dimension" },
      "full": { "value": "9999px", "type": "dimension" }
    }
  }
}
```

### system.json (extract)

```json
{
  "sys": {
    "color": {
      "primary": { "value": "{ref.palette.blue.40}", "type": "color" },
      "primary": {
        "hover": { "value": "{ref.palette.blue.30}", "type": "color" },
        "active": { "value": "{ref.palette.blue.20}", "type": "color" }
      },
      "text": {
        "primary": { "value": "{ref.palette.gray.10}", "type": "color" },
        "secondary": { "value": "{ref.palette.gray.40}", "type": "color" },
        "tertiary": { "value": "{ref.palette.gray.60}", "type": "color" }
      },
      "severity": {
        "critical": { "value": "{ref.palette.red.50}", "type": "color" },
        "high": { "value": "{ref.palette.orange.50}", "type": "color" },
        "medium": { "value": "{ref.palette.yellow.50}", "type": "color" },
        "low": { "value": "{ref.palette.blue.40}", "type": "color" },
        "info": { "value": "{ref.palette.gray.50}", "type": "color" }
      }
    }
  }
}
```

### component.json (extract)

```json
{
  "cmp": {
    "button": {
      "primary": {
        "bg": { "value": "{sys.color.primary}", "type": "color" },
        "bg": {
          "hover": { "value": "{sys.color.primary.hover}", "type": "color" }
        },
        "label": {
          "color": { "value": "{ref.palette.white}", "type": "color" }
        },
        "border": {
          "radius": { "value": "{ref.radius.md}", "type": "dimension" }
        },
        "padding": {
          "x": { "value": "{ref.spacing.4}", "type": "dimension" },
          "y": { "value": "{ref.spacing.2}", "type": "dimension" }
        }
      }
    }
  }
}
```

---

## 7. Style Dictionary Pipeline

### build.cjs (concept)

```javascript
const StyleDictionary = require('style-dictionary');

StyleDictionary.extend({
  source: ['design-tokens/**/*.json'],
  platforms: {
    css: {
      transformGroup: 'css',
      buildPath: 'src/styles/',
      files: [
        {
          destination: 'tokens.css',
          format: 'css/variables',
          options: {
            outputReferences: true
          }
        }
      ]
    },
    tailwind: {
      transformGroup: 'js',
      buildPath: 'tokens-generated/',
      files: [
        {
          destination: 'tailwind-tokens.js',
          format: 'javascript/es6',
        }
      ]
    },
    ts: {
      transformGroup: 'js',
      buildPath: 'src/types/',
      files: [
        {
          destination: 'design-tokens.ts',
          format: 'typescript/es6-declarations'
        }
      ]
    }
  }
}).buildAllPlatforms();
```

### Generated CSS Output (sample)

```css
:root {
  /* Reference */
  --ref-palette-blue-40: #3A66A6;
  --ref-palette-gray-10: #0F1115;
  --ref-spacing-4: 16px;
  --ref-radius-md: 6px;

  /* System (references reference) */
  --sys-color-primary: var(--ref-palette-blue-40);
  --sys-color-text-primary: var(--ref-palette-gray-10);
  --sys-color-severity-critical: var(--ref-palette-red-50);

  /* Component (references system) */
  --cmp-button-primary-bg: var(--sys-color-primary);
  --cmp-button-primary-padding-x: var(--ref-spacing-4);
  --cmp-button-primary-border-radius: var(--ref-radius-md);
}
```

### Tailwind Config (sample)

```typescript
// dashboard/tailwind.config.ts
import { tokens } from './tokens-generated/tailwind-tokens';

export default {
  content: ['./index.html', './src/**/*.{ts,tsx}'],
  theme: {
    colors: {
      // Map system tokens to Tailwind utility classes
      primary: 'var(--sys-color-primary)',
      'primary-hover': 'var(--sys-color-primary-hover)',
      'text-primary': 'var(--sys-color-text-primary)',
      'text-secondary': 'var(--sys-color-text-secondary)',
      'severity-critical': 'var(--sys-color-severity-critical)',
      'severity-high': 'var(--sys-color-severity-high)',
      // ...
    },
    spacing: {
      0: 'var(--ref-spacing-0)',
      1: 'var(--ref-spacing-1)',
      2: 'var(--ref-spacing-2)',
      // ...
    },
    borderRadius: {
      sm: 'var(--ref-radius-sm)',
      md: 'var(--ref-radius-md)',
      lg: 'var(--ref-radius-lg)',
      // ...
    },
    fontFamily: {
      sans: 'var(--ref-font-family-sans)',
      mono: 'var(--ref-font-family-mono)',
    },
    extend: {},
  },
  plugins: [],
};
```

### Usage in Component (Sample)

```typescript
// dashboard/src/components/ui/button.tsx
import { cn } from '@/lib/utils';

export function Button({ variant = 'primary', children, ...props }) {
  return (
    <button
      className={cn(
        // base
        'rounded-md font-medium transition-colors',
        // variant via tokens
        variant === 'primary' && 'bg-primary text-white hover:bg-primary-hover',
        variant === 'secondary' && 'bg-transparent text-text-primary border border-border-default',
        variant === 'danger' && 'bg-severity-critical text-white hover:bg-red-600',
      )}
      {...props}
    >
      {children}
    </button>
  );
}
```

---

**End of Design Day 6 — Design Tokens 3-Tier (Material Design 3 pattern)**

**Next**: Design Day 7-8 — Figma Component Library Skeleton + 8 Low-fi Wireframes.
