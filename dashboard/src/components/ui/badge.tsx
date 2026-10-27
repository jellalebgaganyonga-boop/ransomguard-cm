import * as React from 'react';
import { cva, type VariantProps } from 'class-variance-authority';
import { cn } from '@/lib/utils';

/**
 * Badge — Atom #6 (Atomic Design inventory, Design Phase Day 7-8 §1).
 *
 * Generic pill/badge primitive. Domain-specific badges (SeverityBadge,
 * StatusBadge, RoleBadge — Molecules #24-26) compose this atom with
 * fixed severity/status mappings; see severity-badge.tsx.
 *
 * Variants map to cmp.badge.* tokens (Design Phase Day 6 §4.2).
 */

const badgeVariants = cva(
  'inline-flex items-center gap-1 rounded-sm border px-2 py-0.5 text-xs font-semibold uppercase tracking-wide',
  {
    variants: {
      variant: {
        // Each badge's text is an -on-subtle token, defined once per role for each theme, never
        // a reference-palette shade picked here. Picked here, it cannot follow the theme: the
        // MEDIUM badge said yellow-20, and in dark mode the background it sits on IS yellow-20,
        // so the word was painted in its own background colour and the badge read as an empty
        // yellow box. Reported by the owner on 2026-10-28. Every pair is >= 4.5:1 in both
        // themes, checked by badge-contrast.test.ts.
        critical: 'border-severity-critical-border bg-severity-critical-subtle text-severity-critical-on-subtle',
        high: 'border-severity-high-border bg-severity-high-subtle text-severity-high-on-subtle',
        medium: 'border-severity-medium-border bg-severity-medium-subtle text-severity-medium-on-subtle',
        low: 'border-severity-low-border bg-severity-low-subtle text-severity-low-on-subtle',
        success: 'border-success-border bg-success-subtle text-success-on-subtle',
        info: 'border-severity-info-border bg-severity-info-subtle text-severity-info-on-subtle',
        neutral: 'border-border-default bg-surface-subtle text-text-secondary',
      },
    },
    defaultVariants: {
      variant: 'neutral',
    },
  }
);

export interface BadgeProps
  extends React.HTMLAttributes<HTMLSpanElement>,
    VariantProps<typeof badgeVariants> {}

export function Badge({ className, variant, ...props }: BadgeProps) {
  return <span className={cn(badgeVariants({ variant }), className)} {...props} />;
}
