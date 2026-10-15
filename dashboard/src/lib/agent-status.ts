/**
 * Agent status utilities — colours only, pure functions, no React.
 *
 * AgentStatus is lowercase (provisioned/active/disconnected/decommissioned): the enrolment
 * lifecycle, not protection.
 *
 * Protection is decided by the GRID at read time (protection_state + reason_codes, grid
 * services/agent_posture.py), with the one definition of the 5 and 60 min thresholds. The
 * console used to recompute heartbeat freshness here with its own thresholds -- and disagreed
 * with the server at 5 min exactly. It now only colours what the server says
 * (LOT2_CONTRAT_V2_VERDICT.md, arbitrage 2: "agent-status.ts ne garde que la couleur").
 */

import type { AgentItem, AgentStatus, ProtectionState } from '@/api/agents';

type BadgeVariant = 'critical' | 'high' | 'medium' | 'low' | 'info' | 'neutral' | 'success';

// ── Enrolment status badge ──────────────────────────────────

/**
 * Maps AgentStatus to Badge component variant. Active is the "success" token (green): it used
 * severity-low, which is BLUE, until the LOT 2 demo caught it.
 */
export function agentStatusVariant(
  status: AgentStatus
): 'info' | 'success' | 'high' | 'neutral' {
  switch (status) {
    case 'active':        return 'success'; // green
    case 'provisioned':   return 'info';    // grey (severity-info = gray-50)
    case 'disconnected':  return 'high';    // orange
    case 'decommissioned': return 'neutral'; // grey
  }
}

// ── Module state colour ─────────────────────────────────────

/** The four detectors the GRID verdict reads (DETECTION_MODULES, grid services/agent_posture.py). */
export const VERDICT_DETECTORS: readonly string[] = ['SENTINEL', 'ENTROPY', 'USB_GUARD', 'EXFIL_WATCH'];

/**
 * A module's state badge: active green, degraded orange, inactive red for a detector the verdict
 * reads -- grey for a module it does not (GENEALOGY, ANTI_TAMPERING "not implemented"): showing
 * red for a module that is not supposed to run would cry wolf. disabled_by_config is a choice,
 * not a failure: grey (the verdict banner says what it costs). An unknown state: grey.
 */
export function moduleStateVariant(code: string, state: string): BadgeVariant {
  switch (state) {
    case 'active':   return 'success';
    case 'degraded': return 'high';
    case 'inactive': return VERDICT_DETECTORS.includes(code) ? 'critical' : 'neutral';
    default:         return 'neutral';
  }
}

// ── Protection verdict colour ───────────────────────────────

/**
 * protected green, partial orange, unprotected red, unknown grey (decision 1 of LOT 2).
 * Green is the "success" token: the severity-low token is BLUE (blue-40), and the verdict
 * showed blue until the LOT 2 demo caught it.
 */
export function protectionVariant(state: ProtectionState): BadgeVariant {
  switch (state) {
    case 'protected':   return 'success';
    case 'partial':     return 'high';
    case 'unprotected': return 'critical';
    case 'unknown':     return 'neutral';
  }
}

/** Banner background and border, per verdict (contrast checked by the axe scans of the E2E suite). */
export const PROTECTION_BANNER_CLASS: Record<ProtectionState, string> = {
  protected:   'border-success-border bg-success-subtle',
  partial:     'border-severity-high-border bg-severity-high-subtle',
  unprotected: 'border-severity-critical-border bg-severity-critical-subtle',
  unknown:     'border-border-default bg-surface-subtle',
};

/**
 * Endpoints the server reports silent for more than 60 min (verdict unprotected because of
 * signal_lost). Replaces the console's own heartbeat threshold.
 */
export function isSignalLostRed(agent: Pick<AgentItem, 'protection_state' | 'reason_codes'>): boolean {
  return agent.protection_state === 'unprotected' && agent.reason_codes.includes('signal_lost');
}
