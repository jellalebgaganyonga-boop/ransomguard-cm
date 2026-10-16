import { describe, it, expect } from 'vitest';
import { getAlertActionPermissions } from './alert-permissions';

/**
 * Exhaustive matrix test: 3 roles × 3 statuses = 9 cases minimum.
 * Per AC3.2.5, AC3.3.2-3, AC3.4.2-3 — every cell of this matrix is
 * specified explicitly in the PRD, so every cell gets a test.
 */

describe('getAlertActionPermissions', () => {
  // ── tenant_admin ──────────────────────────────────────────

  it('admin + New → can acknowledge, cannot close', () => {
    expect(getAlertActionPermissions('tenant_admin', 'New')).toEqual({
      canAcknowledge: true,
      canClose: false,
    });
  });

  it('admin + Investigating → cannot acknowledge, can close', () => {
    expect(getAlertActionPermissions('tenant_admin', 'Investigating')).toEqual({
      canAcknowledge: false,
      canClose: true,
    });
  });

  it('admin + Resolved → no actions (AC3.4.3, read-only)', () => {
    expect(getAlertActionPermissions('tenant_admin', 'Resolved')).toEqual({
      canAcknowledge: false,
      canClose: false,
    });
  });

  // ── security_analyst ──────────────────────────────────────

  it('analyst + New → can acknowledge, cannot close', () => {
    expect(getAlertActionPermissions('security_analyst', 'New')).toEqual({
      canAcknowledge: true,
      canClose: false,
    });
  });

  it('analyst + Investigating → cannot acknowledge, can close', () => {
    expect(getAlertActionPermissions('security_analyst', 'Investigating')).toEqual({
      canAcknowledge: false,
      canClose: true,
    });
  });

  it('analyst + Resolved → no actions', () => {
    expect(getAlertActionPermissions('security_analyst', 'Resolved')).toEqual({
      canAcknowledge: false,
      canClose: false,
    });
  });

  // ── read_only_auditor (AC3.3.3 — never write, regardless of status) ──

  it('auditor + New → no actions (AC3.3.3)', () => {
    expect(getAlertActionPermissions('read_only_auditor', 'New')).toEqual({
      canAcknowledge: false,
      canClose: false,
    });
  });

  it('auditor + Investigating → no actions (AC3.3.3)', () => {
    expect(getAlertActionPermissions('read_only_auditor', 'Investigating')).toEqual({
      canAcknowledge: false,
      canClose: false,
    });
  });

  it('auditor + Resolved → no actions', () => {
    expect(getAlertActionPermissions('read_only_auditor', 'Resolved')).toEqual({
      canAcknowledge: false,
      canClose: false,
    });
  });

  // ── Security regression guards ────────────────────────────

  it('NEVER returns canAcknowledge=true for read_only_auditor regardless of status', () => {
    const statuses = [
      'New',
      'Investigating',
      'Resolved',
      'FalsePositive',
      'Suppressed',
    ] as const;
    for (const status of statuses) {
      const perms = getAlertActionPermissions('read_only_auditor', status);
      expect(perms.canAcknowledge).toBe(false);
      expect(perms.canClose).toBe(false);
    }
  });

  it('NEVER allows both canAcknowledge and canClose simultaneously (mutually exclusive statuses)', () => {
    const roles = [
      'tenant_admin',
      'security_analyst',
      'read_only_auditor',
    ] as const;
    const statuses = [
      'New',
      'Investigating',
      'Resolved',
      'FalsePositive',
      'Suppressed',
    ] as const;
    for (const role of roles) {
      for (const status of statuses) {
        const perms = getAlertActionPermissions(role, status);
        expect(perms.canAcknowledge && perms.canClose).toBe(false);
      }
    }
  });
});
