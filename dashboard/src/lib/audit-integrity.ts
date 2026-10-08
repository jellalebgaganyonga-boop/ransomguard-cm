/**
 * audit-integrity.ts
 * EPIC-AUDIT — AC6.1.4: Frontend-only sequence gap detection.
 *
 * Pure functions with no React dependency — fully unit-testable.
 *
 * SCOPE:
 * Gap detection is per-agent_id, not global. The backend returns items
 * ordered by received_at DESC (newest first). Items from different agents
 * are interleaved, so a naive consecutive-index comparison would produce
 * false negatives when entries from agent A are separated by entries from
 * agent B. We track the last seen sequence_number per agent_id instead.
 *
 * ALGORITHM:
 * Walk items in list order (newest → oldest as returned by backend).
 * For each item, look up the previous sequence_number seen for that agent.
 * If the absolute difference > 1, mark that position as a gap.
 * The first occurrence of each agent is never a gap (no prior baseline).
 */

import type { AuditLogItem } from '@/api/audit';

/**
 * Returns a boolean[] of the same length as `items`.
 * `result[i] === true` means there is a sequence gap BEFORE items[i]
 * relative to the previous entry for the same agent_id in the list.
 *
 * AC6.1.4: gap indicator is purely informational — no backend call,
 * no mutation. Gaps can occur due to missing entries, agent restarts,
 * or out-of-order delivery across page boundaries.
 */
export function detectSequenceGaps(items: AuditLogItem[]): boolean[] {
  const result: boolean[] = items.map(() => false);
  // last sequence_number seen per agent_id in list traversal order
  const lastSeq = new Map<string, number>();

  for (let i = 0; i < items.length; i++) {
    const item = items[i];
    if (!item) continue;

    const prev = lastSeq.get(item.agent_id);
    if (prev !== undefined && Math.abs(item.sequence_number - prev) > 1) {
      result[i] = true;
    }
    lastSeq.set(item.agent_id, item.sequence_number);
  }

  return result;
}
