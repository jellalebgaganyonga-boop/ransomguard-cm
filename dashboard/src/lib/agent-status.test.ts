import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest';
import {
  agentStatusVariant,
  heartbeatColor,
  isAgentStale,
  HEARTBEAT_COLOR_CLASS,
} from './agent-status';

// ── agentStatusVariant ────────────────────────────────────────────────────────

describe('agentStatusVariant', () => {
  it('active → "low" (green badge)', () => {
    expect(agentStatusVariant('active')).toBe('low');
  });

  it('provisioned → "info" (blue badge)', () => {
    expect(agentStatusVariant('provisioned')).toBe('info');
  });

  it('disconnected → "high" (red badge)', () => {
    expect(agentStatusVariant('disconnected')).toBe('high');
  });

  it('decommissioned → "neutral" (gray badge)', () => {
    expect(agentStatusVariant('decommissioned')).toBe('neutral');
  });
});

// ── heartbeatColor ────────────────────────────────────────────────────────────
//
// Thresholds (AC4.4.1):
//   null           → neutral (never reported)
//   ageMins < 5    → success (fresh)
//   5 ≤ ageMins < 60 → warning
//   ageMins ≥ 60   → error
//
// Boundary values: exactly 5 min (first warning) and exactly 60 min (first error).

describe('heartbeatColor', () => {
  const FIXED_NOW = new Date('2026-06-21T12:00:00.000Z').getTime();

  beforeEach(() => {
    vi.useFakeTimers();
    vi.setSystemTime(FIXED_NOW);
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  /** Returns ISO string for a heartbeat that occurred `ms` milliseconds ago. */
  function ago(ms: number): string {
    return new Date(FIXED_NOW - ms).toISOString();
  }

  it('null → "neutral" (never reported)', () => {
    expect(heartbeatColor(null)).toBe('neutral');
  });

  it('1 min ago → "success" (well within 5-min fresh window)', () => {
    expect(heartbeatColor(ago(60_000))).toBe('success');
  });

  it('4 min 59 s ago → "success" (last moment before threshold)', () => {
    expect(heartbeatColor(ago(4 * 60_000 + 59_000))).toBe('success');
  });

  it('exactly 5 min ago → "warning" (boundary: ageMins === 5, not < 5)', () => {
    expect(heartbeatColor(ago(5 * 60_000))).toBe('warning');
  });

  it('30 min ago → "warning"', () => {
    expect(heartbeatColor(ago(30 * 60_000))).toBe('warning');
  });

  it('59 min 59 s ago → "warning" (last moment before stale threshold)', () => {
    expect(heartbeatColor(ago(59 * 60_000 + 59_000))).toBe('warning');
  });

  it('exactly 60 min ago → "error" (boundary: ageMins === 60, not < 60)', () => {
    expect(heartbeatColor(ago(60 * 60_000))).toBe('error');
  });

  it('2 h ago → "error" (well past threshold)', () => {
    expect(heartbeatColor(ago(2 * 60 * 60_000))).toBe('error');
  });
});

// ── isAgentStale ──────────────────────────────────────────────────────────────
//
// Stale = strictly more than 60 min (uses `>`, NOT `>=`).
// Exactly 60 min is therefore NOT stale.

describe('isAgentStale', () => {
  const FIXED_NOW = new Date('2026-06-21T12:00:00.000Z').getTime();
  const SIXTY_MIN_MS = 60 * 60 * 1000;

  beforeEach(() => {
    vi.useFakeTimers();
    vi.setSystemTime(FIXED_NOW);
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  function ago(ms: number): string {
    return new Date(FIXED_NOW - ms).toISOString();
  }

  it('null → true (no heartbeat ever = stale)', () => {
    expect(isAgentStale(null)).toBe(true);
  });

  it('5 min ago → false', () => {
    expect(isAgentStale(ago(5 * 60_000))).toBe(false);
  });

  it('30 min ago → false', () => {
    expect(isAgentStale(ago(30 * 60_000))).toBe(false);
  });

  it('exactly 60 min ago → false (strict >: 3 600 000 is NOT > 3 600 000)', () => {
    expect(isAgentStale(ago(SIXTY_MIN_MS))).toBe(false);
  });

  it('60 min + 1 ms ago → true (just crossed threshold)', () => {
    expect(isAgentStale(ago(SIXTY_MIN_MS + 1))).toBe(true);
  });

  it('2 h ago → true', () => {
    expect(isAgentStale(ago(2 * SIXTY_MIN_MS))).toBe(true);
  });
});

// ── HEARTBEAT_COLOR_CLASS ─────────────────────────────────────────────────────

describe('HEARTBEAT_COLOR_CLASS', () => {
  it('success maps to text-success', () => {
    expect(HEARTBEAT_COLOR_CLASS.success).toBe('text-success');
  });

  it('warning maps to text-warning', () => {
    expect(HEARTBEAT_COLOR_CLASS.warning).toBe('text-warning');
  });

  it('error maps to text-error', () => {
    expect(HEARTBEAT_COLOR_CLASS.error).toBe('text-error');
  });

  it('neutral maps to text-text-tertiary', () => {
    expect(HEARTBEAT_COLOR_CLASS.neutral).toBe('text-text-tertiary');
  });
});
