import { defineConfig, devices } from '@playwright/test';

// Per ADR-FE-008: Playwright for "Medium" (component + MSW) and "Large" (E2E
// against docker-compose full stack) tests, plus visual regression and
// accessibility (axe-core).
//
// Per Definition Phase Day 4 §6: target browsers Chrome/Firefox/Edge >= 110.
// Per Discovery Phase persona profiles: Marc (Chrome/Edge), Amani (Chrome),
// Jeanne (Edge — government-mandated browser).

const isCI = !!process.env.CI;

// reuseExistingServer trusts whatever already listens on this port. When another
// program holds it -- a WSL relay, a forgotten dev server -- Playwright points
// the browser at it and every test fails for a reason that has nothing to do
// with the app. E2E_PORT moves the whole suite off a contested port in one go.
const port = Number(process.env.E2E_PORT ?? 5173);
const baseURL = process.env.E2E_BASE_URL ?? `http://localhost:${port}`;

export default defineConfig({
  testDir: './e2e',
  fullyParallel: true,
  forbidOnly: isCI,
  retries: isCI ? 2 : 0,
  workers: isCI ? 2 : undefined,
  reporter: isCI
    ? [['html', { open: 'never' }], ['github']]
    : [['html', { open: 'on-failure' }], ['list']],

  use: {
    baseURL,
    trace: 'on-first-retry',
    screenshot: 'only-on-failure',
    video: 'retain-on-failure',
    // Per STRIDE WT2.1: app enforces HTTPS, but local E2E uses HTTP via
    // Vite dev server (proxy handles TLS to backend)
    locale: 'fr-FR', // French-default per Definition Phase §6
    timezoneId: 'Africa/Douala',
  },

  projects: [
    {
      name: 'chromium',
      use: { ...devices['Desktop Chrome'] },
    },
    {
      name: 'firefox',
      use: { ...devices['Desktop Firefox'] },
    },
    {
      name: 'edge',
      use: { ...devices['Desktop Edge'], channel: 'msedge' },
    },
    {
      name: 'mobile-chrome',
      use: { ...devices['Pixel 7'] },
    },
    // Per Definition Phase §1 breakpoints: 320px minimum
    {
      name: 'mobile-small',
      use: {
        ...devices['Desktop Chrome'],
        viewport: { width: 320, height: 568 },
      },
    },
  ],

  webServer: {
    command: `npm run dev -- --port ${port} --strictPort`,
    url: baseURL,
    reuseExistingServer: !isCI,
    timeout: 120_000,
  },
});
