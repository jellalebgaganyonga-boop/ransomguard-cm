import { describe, it, expect } from 'vitest';
import { detectSequenceGaps } from './audit-integrity';
import type { AuditLogItem } from '@/api/audit';

// ── Helpers ─────────────────────────────────────────────────

function entry(agent_id: string, sequence_number: number): AuditLogItem {
  return {
    id: `${agent_id}-${sequence_number}`,
    agent_id,
    sequence_number,
    signing_key_id: 'key-abc',
    received_at: '2026-06-21T12:00:00+00:00',
  };
}

const A = 'agent-aaa';
const B = 'agent-bbb';
const C = 'agent-ccc';

// ── detectSequenceGaps ────────────────────────────────────────

describe('detectSequenceGaps', () => {
  // ── Edge cases ─────────────────────────────────────────────

  it('empty list → []', () => {
    expect(detectSequenceGaps([])).toEqual([]);
  });

  it('single entry → [false] (no prior baseline)', () => {
    expect(detectSequenceGaps([entry(A, 1)])).toEqual([false]);
  });

  // ── Single agent, no gap ───────────────────────────────────

  it('two consecutive entries (diff=1, newest first) → no gap', () => {
    // Backend returns newest first: seq 10 then seq 9
    const items = [entry(A, 10), entry(A, 9)];
    expect(detectSequenceGaps(items)).toEqual([false, false]);
  });

  it('three consecutive entries (diffs all 1) → no gaps', () => {
    const items = [entry(A, 10), entry(A, 9), entry(A, 8)];
    expect(detectSequenceGaps(items)).toEqual([false, false, false]);
  });

  // ── Single agent, gap ──────────────────────────────────────

  it('gap of 2 between two entries of same agent → second entry flagged', () => {
    // seq 10 → seq 8: diff = 2 → gap before index 1
    const items = [entry(A, 10), entry(A, 8)];
    expect(detectSequenceGaps(items)).toEqual([false, true]);
  });

  it('gap of 100 → flagged', () => {
    const items = [entry(A, 100), entry(A, 1)];
    expect(detectSequenceGaps(items)).toEqual([false, true]);
  });

  it('multiple gaps within same agent', () => {
    // 10 → 8 (gap), 8 → 7 (ok), 7 → 4 (gap)
    const items = [entry(A, 10), entry(A, 8), entry(A, 7), entry(A, 4)];
    expect(detectSequenceGaps(items)).toEqual([false, true, false, true]);
  });

  // ── Multiple agents, no interleaving ──────────────────────

  it('two agents in separate blocks, no gaps', () => {
    // A: 10,9,8  then B: 5,4,3
    const items = [
      entry(A, 10), entry(A, 9), entry(A, 8),
      entry(B, 5),  entry(B, 4), entry(B, 3),
    ];
    expect(detectSequenceGaps(items)).toEqual([false, false, false, false, false, false]);
  });

  it('agent boundary is not flagged as a gap (different agent_id)', () => {
    // A seq 10, then B seq 1 — large numeric gap but different agents → no flag
    const items = [entry(A, 10), entry(B, 1)];
    expect(detectSequenceGaps(items)).toEqual([false, false]);
  });

  // ── Interleaved agents (key scenario: per-agent tracking) ──

  it('interleaved agents — gap for A detected across interleaved B entries', () => {
    // List order: A(10), B(5), A(8)
    // A baseline = 10; after B(5): A last seen = 10; A(8): |8 - 10| = 2 → gap
    const items = [entry(A, 10), entry(B, 5), entry(A, 8)];
    expect(detectSequenceGaps(items)).toEqual([false, false, true]);
  });

  it('interleaved agents — no gap for A when diff is exactly 1 across interleaved B entries', () => {
    // A(10), B(5), A(9): |9 - 10| = 1 → no gap
    const items = [entry(A, 10), entry(B, 5), entry(A, 9)];
    expect(detectSequenceGaps(items)).toEqual([false, false, false]);
  });

  it('three interleaved agents, multiple gaps', () => {
    // A(10), B(20), C(1), A(7), B(18), C(1) — wait, C repeats seq 1? Use distinct
    // A: 10 → 7 (gap=3), B: 20 → 18 (gap=2), C: 5 → 4 (ok)
    const items = [
      entry(A, 10), entry(B, 20), entry(C, 5),
      entry(A, 7),  entry(B, 18), entry(C, 4),
    ];
    // A(7): |7-10|=3 → gap; B(18): |18-20|=2 → gap; C(4): |4-5|=1 → ok
    expect(detectSequenceGaps(items)).toEqual([false, false, false, true, true, false]);
  });

  it('interleaved — first occurrence of each agent never flagged', () => {
    const items = [entry(A, 99), entry(B, 999), entry(C, 1)];
    expect(detectSequenceGaps(items)).toEqual([false, false, false]);
  });

  // ── Result length always matches input length ──────────────

  it('result length always equals items length', () => {
    for (const n of [0, 1, 5, 20]) {
      const items = Array.from({ length: n }, (_, i) => entry(A, n - i));
      expect(detectSequenceGaps(items)).toHaveLength(n);
    }
  });
});
