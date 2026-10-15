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
        critical: 'border-severity-critical-border bg-severity-critical-subtle text-[color:var(--ref-palette-red-40)]',
        high: 'border-severity-high-border bg-severity-high-subtle text-[color:var(--ref-palette-orange-30)]',
        medium: 'border-severity-medium-border bg-severity-medium-subtle text-[color:var(--ref-palette-yellow-20)]',
        low: 'border-severity-low-border bg-severity-low-subtle text-severity-low',
        // Green: the "protected" verdict. Text green-30 on green-95 = 5.7:1 (green-50 would be 2.5:1).
        success: 'border-success-border bg-success-subtle text-[color:var(--ref-palette-green-30)]',
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
