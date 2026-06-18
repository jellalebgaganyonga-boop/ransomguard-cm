import { describe, it, expect } from 'vitest';
import { resolvePrimaryRole } from './use-me';

/**
 * resolvePrimaryRole — Small test (Google taxonomy, ADR-FE-008).
 *
 * Critical security property under test: FAIL CLOSED. An empty,
 * unrecognized, or ambiguous roles array must resolve to the MOST
 * RESTRICTIVE role (read_only_auditor), never to tenant_admin or
 * security_analyst by default — see use-me.ts docstring and STRIDE
 * WT4.5 (privilege escalation).
 */

describe('resolvePrimaryRole', () => {
  it('resolves a single recognized role directly', () => {
    expect(resolvePrimaryRole(['tenant_admin'])).toBe('tenant_admin');
    expect(resolvePrimaryRole(['security_analyst'])).toBe('security_analyst');
    expect(resolvePrimaryRole(['read_only_auditor'])).toBe('read_only_auditor');
  });

  it('prefers tenant_admin when multiple recognized roles are present', () => {
    expect(resolvePrimaryRole(['security_analyst', 'tenant_admin'])).toBe(
      'tenant_admin'
    );
  });

  it('prefers security_analyst over read_only_auditor when both present (no admin)', () => {
    expect(resolvePrimaryRole(['read_only_auditor', 'security_analyst'])).toBe(
      'security_analyst'
    );
  });

  it('fails closed to read_only_auditor on empty roles array', () => {
    expect(resolvePrimaryRole([])).toBe('read_only_auditor');
  });

  it('fails closed to read_only_auditor on unrecognized role string', () => {
    expect(resolvePrimaryRole(['super_admin_v2'])).toBe('read_only_auditor');
  });

  it('fails closed to read_only_auditor when only unrecognized roles are present alongside recognized non-admin/analyst roles', () => {
    // Defensive: a future backend role 'billing_manager' alongside
    // read_only_auditor must not accidentally grant analyst/admin access.
    expect(resolvePrimaryRole(['billing_manager', 'read_only_auditor'])).toBe(
      'read_only_auditor'
    );
  });
});
