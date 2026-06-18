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
        critical: 'border-severity-critical-border bg-severity-critical-subtle text-severity-critical',
        high: 'border-severity-high-border bg-severity-high-subtle text-severity-high',
        medium: 'border-severity-medium-border bg-severity-medium-subtle text-[color:var(--ref-palette-yellow-20)]',
        low: 'border-severity-low-border bg-severity-low-subtle text-severity-low',
        info: 'border-severity-info-border bg-severity-info-subtle text-severity-info',
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
