import { cn } from '@/lib/utils';

/**
 * PriorityScore — Molecule #23 (Atomic Design inventory, Design Phase Day 7-8 §1).
 *
 * Implements the Defender XDR 0-100 priority score pattern (Discovery
 * Phase OST SOL A1, Definition Phase decision #2):
 * - score > 85  -> Red   (cmp.priority.score.bg.high)
 * - 15-85       -> Orange (cmp.priority.score.bg.medium)
 * - score < 15  -> Gray  (cmp.priority.score.bg.low)
 *
 * Per WCAG 1.4.1 (Use of Color): the numeric score itself provides a
 * non-color-dependent encoding alongside the color band, satisfying the
 * "color + label + icon + position" pattern from Design Phase Day 6 §5.
 */

interface PriorityScoreProps {
  /** 0-100 priority score from the backend correlation engine. */
  score: number;
  className?: string;
}

export function PriorityScore({ score, className }: PriorityScoreProps) {
  const clamped = Math.max(0, Math.min(100, Math.round(score)));
  const tone = clamped > 85 ? 'high' : clamped >= 15 ? 'medium' : 'low';

  return (
    <span
      className={cn(
        'inline-flex min-w-[40px] items-center justify-center rounded-md px-2 py-1',
        'text-xs font-bold tabular-nums text-white',
        tone === 'high' && 'bg-priority-high',
        tone === 'medium' && 'bg-priority-medium',
        tone === 'low' && 'bg-priority-low',
        className
      )}
      role="img"
      aria-label={`Score de priorité ${clamped} sur 100`}
    >
      {clamped}
    </span>
  );
}
