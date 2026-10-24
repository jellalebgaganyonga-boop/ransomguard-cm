import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';

import './i18n'; // side-effect: initializes i18next (ADR-FE-006)
import './styles/globals.css';
import { App } from './App';

/**
 * QueryClient — ADR-FE-003 (TanStack Query for server state).
 *
 * Defaults:
 * - refetchOnWindowFocus: true — appropriate for a security console
 *   where analysts switch tabs frequently and expect fresh alert data
 *   on return (Discovery Phase: Marc's primary workflow).
 * - retry: 1 for queries (avoid hammering a degraded backend), but
 *   note useMe() overrides this to `false` (auth failures should not
 *   retry — see src/hooks/use-me.ts).
 */
const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      retry: 1,
      refetchOnWindowFocus: true,
      staleTime: 30_000,
    },
  },
});

const rootElement = document.getElementById('root');
if (!rootElement) {
  throw new Error('Root element #root not found in index.html');
}

createRoot(rootElement).render(
  <StrictMode>
    <QueryClientProvider client={queryClient}>
      <App />
    </QueryClientProvider>
  </StrictMode>
);
