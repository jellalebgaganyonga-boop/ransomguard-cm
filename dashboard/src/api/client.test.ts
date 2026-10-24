import { afterEach, describe, expect, it, vi } from 'vitest';
import { http, HttpResponse, delay } from 'msw';
import { server } from '@/test/msw/server';

/**
 * refreshAccessToken — one refresh per tab, one tab at a time (GRID-SEC-005).
 *
 * The GRID revokes the whole session when a consumed refresh token comes back.
 * Two tabs refreshing at the same instant with the shared cookie must therefore
 * never be in flight together. Each "tab" here is a fresh instance of the
 * module (vi.resetModules), sharing the same navigator.locks, as real tabs do.
 */

type Client = typeof import('./client');

async function aTab(): Promise<Client> {
  vi.resetModules();
  return import('./client');
}

/** Minimal Web Locks: one holder per name, FIFO, records the names asked for. */
function installLocks(): string[] {
  const names: string[] = [];
  const queues = new Map<string, Promise<unknown>>();
  const locks = {
    request: (name: string, task: () => Promise<unknown>) => {
      names.push(name);
      const previous = queues.get(name) ?? Promise.resolve();
      const run = previous.catch(() => undefined).then(task);
      queues.set(name, run);
      return run;
    },
  };
  Object.defineProperty(navigator, 'locks', { value: locks, configurable: true });
  return names;
}

function removeLocks(): void {
  Reflect.deleteProperty(navigator, 'locks');
}

/** A refresh endpoint that reports how many calls were in flight at once. */
function countingRefresh(): { calls: () => number; maxInFlight: () => number } {
  let calls = 0;
  let inFlight = 0;
  let maxInFlight = 0;
  server.use(
    http.post('*/api/v1/auth/refresh', async () => {
      calls += 1;
      inFlight += 1;
      maxInFlight = Math.max(maxInFlight, inFlight);
      await delay(50);
      inFlight -= 1;
      return HttpResponse.json({ access_token: `access-${calls}`, refresh_token: '' });
    }),
  );
  return { calls: () => calls, maxInFlight: () => maxInFlight };
}

afterEach(() => {
  removeLocks();
});

describe('refreshAccessToken', () => {
  it('sends one request for concurrent callers in the same tab (StrictMode boot, parallel 401s)', async () => {
    const endpoint = countingRefresh();
    const tab = await aTab();

    const [first, second] = await Promise.all([tab.refreshAccessToken(), tab.refreshAccessToken()]);

    expect(endpoint.calls()).toBe(1);
    expect(second).toBe(first);
  });

  it('runs under the shared rg-auth-refresh lock', async () => {
    const names = installLocks();
    countingRefresh();
    const tab = await aTab();

    await tab.refreshAccessToken();

    expect(names).toEqual(['rg-auth-refresh']);
    expect(tab.REFRESH_LOCK_NAME).toBe('rg-auth-refresh');
  });

  it('never has two tabs refreshing at the same time when the Web Locks API exists', async () => {
    installLocks();
    const endpoint = countingRefresh();
    const [tabA, tabB] = [await aTab(), await aTab()];

    await Promise.all([tabA.refreshAccessToken(), tabB.refreshAccessToken()]);

    expect(endpoint.calls()).toBe(2);
    expect(endpoint.maxInFlight()).toBe(1);
  });

  it('still refreshes without the Web Locks API (previous behaviour, tabs not serialised)', async () => {
    removeLocks();
    const endpoint = countingRefresh();
    const [tabA, tabB] = [await aTab(), await aTab()];

    await Promise.all([tabA.refreshAccessToken(), tabB.refreshAccessToken()]);

    expect(endpoint.calls()).toBe(2);
    expect(endpoint.maxInFlight()).toBe(2);
  });
});
