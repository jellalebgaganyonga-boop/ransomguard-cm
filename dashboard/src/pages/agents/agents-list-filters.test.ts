import { describe, expect, it } from 'vitest';

import { STATUS_FILTERS } from './agent-status-filters';

/**
 * A filter that can never match anything is worse than no filter: it is tried once, it returns an
 * empty page, and from then on the page is not trusted. "Provisioned" was such a filter -- the
 * status exists in the API's enum as a column default and nothing ever writes it, because
 * enrolment creates the agent `active` in the same statement.
 *
 * The GRID side of this is grid/tests/api/test_agent_counts.py, which asserts that an enrolled
 * workstation is `active` and that no row is ever `provisioned`.
 */
describe('the Agents page status filters', () => {
  it('offers only the states a workstation can actually be in', () => {
    expect(STATUS_FILTERS.map((f) => f.value)).toEqual([
      '',
      'active',
      'disconnected',
      'decommissioned',
    ]);
  });

  it('does not offer Provisioned, which no agent is ever in', () => {
    expect(STATUS_FILTERS.some((f) => f.value === 'provisioned')).toBe(false);
  });

  it('gives every filter a translation key and an English fallback', () => {
    for (const filter of STATUS_FILTERS) {
      expect(filter.labelKey).toMatch(/^[a-z]+\./);
      expect(filter.fallback).not.toBe('');
    }
  });
});
