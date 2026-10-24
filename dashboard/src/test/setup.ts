import '@testing-library/jest-dom/vitest';
import { afterAll, afterEach, beforeAll } from 'vitest';
import { server } from './msw/server';

/**
 * Vitest global setup — ADR-FE-008.
 *
 * Starts the MSW (Mock Service Worker) server for the duration of the
 * Small/Medium test suites, resetting handlers between tests so one
 * test's mocked response cannot leak into another (test isolation —
 * Master Plan testing strategy: each test independently verifiable).
 */

beforeAll(() => server.listen({ onUnhandledRequest: 'warn' }));
afterEach(() => server.resetHandlers());
afterAll(() => server.close());
