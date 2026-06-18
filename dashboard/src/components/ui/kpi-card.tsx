import type { ReactNode } from 'react';
import { cn } from '@/lib/utils';

/**
 * KPICard — Organism #42 (Atomic Design inventory, Design Phase Day 7-8 §1).
 * Visual spec: cmp.card.* tokens (Design Phase Day 6 §4.4),
 * sys.typography.display.medium for the big number (Day 6 §3.10).
 *
 * Sprint 7 Day 1: structural component only, static props. Day 2-3
 * EPIC-DASHBOARD wires real data from GET /dashboard/metrics/summary.
 */

interface KPICardProps {
  label: string;
  value: ReactNode;
  description?: string;
  className?: string;
}

export function KPICard({ label, value, description, className }: KPICardProps) {
  return (
    <div
      className={cn(
        'rounded-lg border border-border-subtle bg-elevated p-6 shadow-sm',
        className
      )}
    >
      <p className="text-xs font-medium uppercase tracking-wide text-text-secondary">
        {label}
      </p>
      <p className="mt-2 text-3xl font-bold tabular-nums text-text-primary">{value}</p>
      {description && <p className="mt-1 text-xs text-text-tertiary">{description}</p>}
    </div>
  );
}
