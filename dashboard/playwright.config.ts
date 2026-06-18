import { defineConfig, devices } from '@playwright/test';

// Per ADR-FE-008: Playwright for "Medium" (component + MSW) and "Large" (E2E
// against docker-compose full stack) tests, plus visual regression and
// accessibility (axe-core).
//
// Per Definition Phase Day 4 §6: target browsers Chrome/Firefox/Edge >= 110.
// Per Discovery Phase persona profiles: Marc (Chrome/Edge), Amani (Chrome),
// Jeanne (Edge — government-mandated browser).

const isCI = !!process.env.CI;

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
    baseURL: process.env.E2E_BASE_URL ?? 'http://localhost:5173',
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
    command: 'npm run dev',
    url: 'http://localhost:5173',
    reuseExistingServer: !isCI,
    timeout: 120_000,
  },
});
