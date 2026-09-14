/**
 * Agent status utilities — pure functions, no React.
 *
 * AgentStatus is lowercase (provisioned/active/disconnected/decommissioned).
 * Heartbeat freshness is separate from DB status: an agent can be "active"
 * in DB but have a stale heartbeat if the connection dropped recently.
 *
 * AC4.4.1: heartbeat freshness thresholds
 *   < 5 min  → green (fresh)
 *   5–60 min → yellow (warning)
 *   > 60 min → red (stale)
 *   null     → gray (never reported)
 */

import type { AgentStatus } from '@/api/agents';

/** Append Z if no tz indicator — FastAPI returns naive UTC datetimes. */
function parseUtc(s: string): Date {
  return new Date(
    s.endsWith('Z') || /[+-]\d{2}:\d{2}$/.test(s) ? s : s + 'Z'
  );
}

// ── Status badge variant ────────────────────────────────────

/** Maps AgentStatus to Badge component variant. */
export function agentStatusVariant(
  status: AgentStatus
): 'info' | 'low' | 'high' | 'neutral' {
  switch (status) {
    case 'active':        return 'low';    // green (severity-low token = green)
    case 'provisioned':   return 'info';   // blue
    case 'disconnected':  return 'high';   // red
    case 'decommissioned': return 'neutral';
  }
}

// ── Heartbeat freshness ─────────────────────────────────────

export type HeartbeatColor = 'success' | 'warning' | 'error' | 'neutral';

/** Returns a Tailwind text color class based on heartbeat age (AC4.4.1). */
export function heartbeatColor(lastHeartbeatAt: string | null): HeartbeatColor {
  if (!lastHeartbeatAt) return 'neutral';
  const ageMs = Date.now() - parseUtc(lastHeartbeatAt).getTime();
  const ageMins = ageMs / 60_000;
  if (ageMins < 5)  return 'success';
  if (ageMins < 60) return 'warning';
  return 'error';
}

export const HEARTBEAT_COLOR_CLASS: Record<HeartbeatColor, string> = {
  success: 'text-success',
  warning: 'text-warning',
  error:   'text-error',
  neutral: 'text-text-tertiary',
};

/** True if agent has not sent a heartbeat in over 60 minutes (AC4.4.3). */
export function isAgentStale(lastHeartbeatAt: string | null): boolean {
  if (!lastHeartbeatAt) return true;
  return Date.now() - parseUtc(lastHeartbeatAt).getTime() > 60 * 60 * 1000;
}
