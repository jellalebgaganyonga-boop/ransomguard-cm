import { describe, it, expect } from 'vitest';
import { canDisable, canEnable, canUpdateRoles, KNOWN_ROLE_NAMES } from './user-permissions';

const SELF = 'user-self-id';
const OTHER = 'user-other-id';

// ── canDisable ────────────────────────────────────────────────────────────────
//
// SECURITY: This is the ONLY protection against self-lockout (GRID-SEC-002).
// Backend has no server-side self-disable guard (unlike enable which 422s).

describe('canDisable', () => {
  it('self → false (GRID-SEC-002: only frontend guard against self-lockout)', () => {
    expect(canDisable(SELF, SELF)).toBe(false);
  });

  it('other user → true', () => {
    expect(canDisable(OTHER, SELF)).toBe(true);
  });

  it('empty string IDs equal → false (edge case: no currentUserId yet)', () => {
    expect(canDisable('', '')).toBe(false);
  });
});

// ── canEnable ─────────────────────────────────────────────────────────────────
//
// Backend also blocks self-enable via 422, so this is defense-in-depth.

describe('canEnable', () => {
  it('self → false', () => {
    expect(canEnable(SELF, SELF)).toBe(false);
  });

  it('other user → true', () => {
    expect(canEnable(OTHER, SELF)).toBe(true);
  });

  it('empty string IDs equal → false', () => {
    expect(canEnable('', '')).toBe(false);
  });
});

// ── canUpdateRoles ────────────────────────────────────────────────────────────
//
// Self-demotion guard: admin cannot remove tenant_admin from themselves
// (backend returns 400; frontend pre-validates to show a meaningful error).

describe('canUpdateRoles', () => {
  it('self keeping only tenant_admin → true', () => {
    expect(canUpdateRoles(SELF, SELF, ['tenant_admin'])).toBe(true);
  });

  it('self keeping tenant_admin + other roles → true', () => {
    expect(canUpdateRoles(SELF, SELF, ['tenant_admin', 'security_analyst'])).toBe(true);
  });

  it('self removing tenant_admin (keeping analyst) → false (self-demotion blocked)', () => {
    expect(canUpdateRoles(SELF, SELF, ['security_analyst'])).toBe(false);
  });

  it('self removing ALL roles → false', () => {
    expect(canUpdateRoles(SELF, SELF, [])).toBe(false);
  });

  it('self keeping only read_only_auditor → false', () => {
    expect(canUpdateRoles(SELF, SELF, ['read_only_auditor'])).toBe(false);
  });

  it('other user with no roles → true (guard only applies to self)', () => {
    expect(canUpdateRoles(OTHER, SELF, [])).toBe(true);
  });

  it('other user removing tenant_admin → true (admin can demote others)', () => {
    expect(canUpdateRoles(OTHER, SELF, ['read_only_auditor'])).toBe(true);
  });

  it('other user with any roles → true', () => {
    expect(canUpdateRoles(OTHER, SELF, ['security_analyst', 'tenant_admin'])).toBe(true);
  });
});

// ── KNOWN_ROLE_NAMES ──────────────────────────────────────────────────────────

describe('KNOWN_ROLE_NAMES', () => {
  it('contains tenant_admin', () => {
    expect(KNOWN_ROLE_NAMES).toContain('tenant_admin');
  });

  it('contains security_analyst', () => {
    expect(KNOWN_ROLE_NAMES).toContain('security_analyst');
  });

  it('contains read_only_auditor', () => {
    expect(KNOWN_ROLE_NAMES).toContain('read_only_auditor');
  });

  it('has exactly 3 entries (no undocumented roles)', () => {
    expect(KNOWN_ROLE_NAMES).toHaveLength(3);
  });
});
