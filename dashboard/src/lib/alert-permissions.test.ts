import { describe, it, expect } from 'vitest';
import { getAlertActionPermissions } from './alert-permissions';

/**
 * Exhaustive matrix test: 3 roles × 3 statuses = 9 cases minimum.
 * Per AC3.2.5, AC3.3.2-3, AC3.4.2-3 — every cell of this matrix is
 * specified explicitly in the PRD, so every cell gets a test.
 */

describe('getAlertActionPermissions', () => {
  // ── tenant_admin ──────────────────────────────────────────

  it('admin + new → can acknowledge, cannot close', () => {
    expect(getAlertActionPermissions('tenant_admin', 'new')).toEqual({
      canAcknowledge: true,
      canClose: false,
    });
  });

  it('admin + acknowledged → cannot acknowledge, can close', () => {
    expect(getAlertActionPermissions('tenant_admin', 'acknowledged')).toEqual({
      canAcknowledge: false,
      canClose: true,
    });
  });

  it('admin + closed → no actions (AC3.4.3, read-only)', () => {
    expect(getAlertActionPermissions('tenant_admin', 'closed')).toEqual({
      canAcknowledge: false,
      canClose: false,
    });
  });

  // ── security_analyst ──────────────────────────────────────

  it('analyst + new → can acknowledge, cannot close', () => {
    expect(getAlertActionPermissions('security_analyst', 'new')).toEqual({
      canAcknowledge: true,
      canClose: false,
    });
  });

  it('analyst + acknowledged → cannot acknowledge, can close', () => {
    expect(getAlertActionPermissions('security_analyst', 'acknowledged')).toEqual({
      canAcknowledge: false,
      canClose: true,
    });
  });

  it('analyst + closed → no actions', () => {
    expect(getAlertActionPermissions('security_analyst', 'closed')).toEqual({
      canAcknowledge: false,
      canClose: false,
    });
  });

  // ── read_only_auditor (AC3.3.3 — never write, regardless of status) ──

  it('auditor + new → no actions (AC3.3.3)', () => {
    expect(getAlertActionPermissions('read_only_auditor', 'new')).toEqual({
      canAcknowledge: false,
      canClose: false,
    });
  });

  it('auditor + acknowledged → no actions (AC3.3.3)', () => {
    expect(getAlertActionPermissions('read_only_auditor', 'acknowledged')).toEqual({
      canAcknowledge: false,
      canClose: false,
    });
  });

  it('auditor + closed → no actions', () => {
    expect(getAlertActionPermissions('read_only_auditor', 'closed')).toEqual({
      canAcknowledge: false,
      canClose: false,
    });
  });

  // ── Security regression guards ────────────────────────────

  it('NEVER returns canAcknowledge=true for read_only_auditor regardless of status', () => {
    const statuses: Array<'new' | 'acknowledged' | 'closed'> = [
      'new',
      'acknowledged',
      'closed',
    ];
    for (const status of statuses) {
      const perms = getAlertActionPermissions('read_only_auditor', status);
      expect(perms.canAcknowledge).toBe(false);
      expect(perms.canClose).toBe(false);
    }
  });

  it('NEVER allows both canAcknowledge and canClose simultaneously (mutually exclusive statuses)', () => {
    const roles: Array<'tenant_admin' | 'security_analyst' | 'read_only_auditor'> = [
      'tenant_admin',
      'security_analyst',
      'read_only_auditor',
    ];
    const statuses: Array<'new' | 'acknowledged' | 'closed'> = [
      'new',
      'acknowledged',
      'closed',
    ];
    for (const role of roles) {
      for (const status of statuses) {
        const perms = getAlertActionPermissions(role, status);
        expect(perms.canAcknowledge && perms.canClose).toBe(false);
      }
    }
  });
});
