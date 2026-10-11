import { describe, it, expect } from 'vitest';
import { agentStatusVariant, isSignalLostRed, protectionVariant } from './agent-status';

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

// ── protectionVariant: the colour of the server's verdict, nothing recomputed ─

describe('protectionVariant', () => {
  it.each([
    ['protected', 'low'],
    ['partial', 'high'],
    ['unprotected', 'critical'],
    ['unknown', 'neutral'],
  ] as const)('%s → %s', (state, variant) => {
    expect(protectionVariant(state)).toBe(variant);
  });
});

// ── isSignalLostRed: the server's "no signal for more than 60 min", not a console threshold ─

describe('isSignalLostRed', () => {
  it('unprotected because of signal_lost → true', () => {
    expect(isSignalLostRed({ protection_state: 'unprotected', reason_codes: ['signal_lost'] })).toBe(true);
  });

  it('partial signal_lost (5 to 60 min) → false', () => {
    expect(isSignalLostRed({ protection_state: 'partial', reason_codes: ['signal_lost'] })).toBe(false);
  });

  it('unprotected for another reason → false', () => {
    expect(
      isSignalLostRed({ protection_state: 'unprotected', reason_codes: ['SENTINEL:inactive:init_failed'] }),
    ).toBe(false);
  });

  it('never connected → false (it never had a signal to lose)', () => {
    expect(isSignalLostRed({ protection_state: 'unknown', reason_codes: ['never_connected'] })).toBe(false);
  });
});
