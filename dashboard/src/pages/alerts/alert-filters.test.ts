import { describe, expect, it, vi, beforeEach } from 'vitest';
import { fetchAlertList } from '@/api/alerts';
import { apiClient } from '@/api/client';

/**
 * What the Alerts page actually asks the server for.
 *
 * The owner reported on 2026-10-27 that the filters did not work. The status filter did; the
 * search box did not. It is labelled "Search (hostname...)" and sent what was typed as
 * `agent_id` -- a UUID -- so a workstation name matched nothing and emptied the list, which from
 * the outside is indistinguishable from a filter that does nothing at all.
 */

vi.mock('@/api/client', () => ({ apiClient: { get: vi.fn() } }));

const get = vi.mocked(apiClient.get);

function emptyPage() {
  return { data: { items: [], total: 0, offset: 0, limit: 50 } };
}

beforeEach(() => {
  get.mockReset();
  get.mockResolvedValue(emptyPage());
});

function paramsOf() {
  return (get.mock.calls[0]?.[1] as { params: Record<string, unknown> }).params;
}

describe('fetchAlertList', () => {
  it('sends the search box as a hostname, not as an agent id', async () => {
    await fetchAlertList({ agent: 'PC-LAB' });

    expect(paramsOf()).toMatchObject({ hostname: 'PC-LAB' });
    expect(paramsOf()).not.toHaveProperty('agent_id');
  });

  it.each(['New', 'Investigating', 'Resolved', 'FalsePositive', 'Suppressed'] as const)(
    'sends status %s exactly as the server spells it',
    async (status) => {
      await fetchAlertList({ status });
      expect(paramsOf()).toMatchObject({ status });
    },
  );

  it('sends severity, status and hostname together when all three are set', async () => {
    await fetchAlertList({ severity: 'Critical', status: 'New', agent: 'PC-LAB' });

    expect(paramsOf()).toMatchObject({ severity: 'Critical', status: 'New', hostname: 'PC-LAB' });
  });

  it('maps the date range to the names the server uses', async () => {
    await fetchAlertList({ date_from: '2026-10-01', date_to: '2026-10-28' });

    expect(paramsOf()).toMatchObject({
      detected_after: '2026-10-01',
      detected_before: '2026-10-28',
    });
  });

  it('sends nothing at all when no filter is set', async () => {
    await fetchAlertList({});

    const params = paramsOf();
    for (const key of ['status', 'severity', 'hostname', 'agent_id', 'detected_after']) {
      expect(params).not.toHaveProperty(key);
    }
  });
});
