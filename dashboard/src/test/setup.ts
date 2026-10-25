import '@testing-library/jest-dom/vitest';
import { afterAll, afterEach, beforeAll } from 'vitest';
import { server } from './msw/server';
import { loadFrench } from '@/i18n';

/**
 * Vitest global setup — ADR-FE-008.
 *
 * Starts the MSW (Mock Service Worker) server for the duration of the
 * Small/Medium test suites, resetting handlers between tests so one
 * test's mocked response cannot leak into another (test isolation —
 * Master Plan testing strategy: each test independently verifiable).
 */

// The console no longer bundles French: it is fetched only when something asks for it, so that
// the bundle check-no-french scans has none in it. The tests that assert the French wording of a
// label still need those translations, so they are loaded once here rather than in each of them.
beforeAll(async () => {
  await loadFrench();
  server.listen({ onUnhandledRequest: 'warn' });
});
afterEach(() => server.resetHandlers());
afterAll(() => server.close());
