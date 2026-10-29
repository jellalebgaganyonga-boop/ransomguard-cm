import { setupServer } from 'msw/node';
import { handlers } from './handlers';

/**
 * MSW server instance for Vitest (Node environment via jsdom).
 * Started/stopped in src/test/setup.ts.
 */
export const server = setupServer(...handlers);
