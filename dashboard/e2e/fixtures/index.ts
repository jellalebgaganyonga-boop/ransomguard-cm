/**
 * e2e/fixtures/index.ts
 * Shared Playwright fixtures, mock data, and API route helpers.
 *
 * All E2E tests import { test, expect, AxeBuilder } from this file.
 * The `loginAs` fixture mounts API route mocks via page.route() BEFORE
 * navigation, then performs the login flow, so every test starts
 * authenticated with the chosen role.
 */

import { test as base, expect, type Page } from '@playwright/test';
import { AxeBuilder } from '@axe-core/playwright';

export { expect, AxeBuilder };

// ── Mock tokens ─────────────────────────────────────────────

export const MOCK_TOKENS = {
  access_token: 'e2e-mock-access-token',
  refresh_token: 'e2e-mock-refresh-token',
};

// ── Mock /me responses per role ──────────────────────────────

const TENANT_ID = 'a0e2e000-0000-0000-0000-000000000001';
const ADMIN_ID  = 'a0e2e000-0000-0000-0000-000000000010';

export const MOCK_ME = {
  tenant_admin: {
    user_id: ADMIN_ID,
    email: 'admin@hopital-yde.cm',
    full_name: 'Admin E2E',
    is_active: true,
    tenant: { id: TENANT_ID, name: 'Hôpital Régional de Yaoundé', status: 'active' },
    roles: ['tenant_admin'],
    last_login_at: '2026-06-21T12:00:00+00:00',
    preferences: { language: 'fr', timezone: 'Africa/Douala' },
  },
  security_analyst: {
    user_id: 'a0e2e000-0000-0000-0000-000000000020',
    email: 'analyst@hopital-yde.cm',
    full_name: 'Analyste E2E',
    is_active: true,
    tenant: { id: TENANT_ID, name: 'Hôpital Régional de Yaoundé', status: 'active' },
    roles: ['security_analyst'],
    last_login_at: '2026-06-21T11:00:00+00:00',
    preferences: { language: 'fr', timezone: 'Africa/Douala' },
  },
  read_only_auditor: {
    user_id: 'a0e2e000-0000-0000-0000-000000000030',
    email: 'auditor@hopital-yde.cm',
    full_name: 'Auditeur E2E',
    is_active: true,
    tenant: { id: TENANT_ID, name: 'Hôpital Régional de Yaoundé', status: 'active' },
    roles: ['read_only_auditor'],
    last_login_at: '2026-06-20T10:00:00+00:00',
    preferences: { language: 'fr', timezone: 'Africa/Douala' },
  },
};

// ── Mock data for other endpoints ────────────────────────────

export const MOCK_METRICS = {
  total_agents: 3,
  active_agents: 2,
  alerts_24h: 5,
  critical_alerts_24h: 1,
};

export const MOCK_ALERT_NEW = {
  id: 'alert-e2e-001',
  agent_id: 'agent-e2e-aaa',
  alert_type: 'SENTINEL_SUSPICIOUS_PROCESS',
  mitre_technique_id: 'T1055',
  severity: 'High',
  status: 'New',
  detected_at: '2026-06-21T10:00:00+00:00',
  ingested_at: '2026-06-21T10:00:05+00:00',
  summary: 'Processus suspect détecté sur le poste de travail cardio-01.',
  confidence_score: null,
  artifacts: null,
  status_history: null,
};

export const MOCK_ALERT_INVESTIGATING = { ...MOCK_ALERT_NEW, status: 'Investigating' };
export const MOCK_ALERT_RESOLVED      = { ...MOCK_ALERT_NEW, status: 'Resolved' };

export const MOCK_ALERTS_LIST = {
  items: [MOCK_ALERT_NEW],
  total: 1,
  offset: 0,
  limit: 50,
};

export const MOCK_AGENT = {
  id: 'agent-e2e-aaa',
  hostname: 'ws-cardio-01',
  fqdn: 'ws-cardio-01.hopital-yde.cm',
  os_version: 'Windows 10 Pro',
  agent_version: '0.9.0',
  status: 'active',
  last_heartbeat_at: '2026-06-21T11:55:00+00:00',
};

export const MOCK_AGENTS_LIST = {
  items: [MOCK_AGENT],
  total: 1,
  offset: 0,
  limit: 50,
};

export const MOCK_USERS_LIST = {
  items: [
    {
      id: ADMIN_ID,
      email: 'admin@hopital-yde.cm',
      full_name: 'Admin E2E',
      is_active: true,
      last_login_at: '2026-06-21T12:00:00+00:00',
    },
    {
      id: 'a0e2e000-0000-0000-0000-000000000020',
      email: 'analyst@hopital-yde.cm',
      full_name: 'Analyste E2E',
      is_active: true,
      last_login_at: '2026-06-21T11:00:00+00:00',
    },
  ],
  total: 2,
  offset: 0,
  limit: 20,
};

export const MOCK_AUDIT_LOGS = {
  items: [
    {
      id: 'log-e2e-001',
      agent_id: 'agent-e2e-aaa',
      sequence_number: 42,
      signing_key_id: 'key-abc123def456ghi789jkl',
      received_at: '2026-06-21T10:00:00+00:00',
    },
  ],
  total: 1,
  offset: 0,
  limit: 50,
};

export const MOCK_COMMAND = {
  id: 'cmd-e2e-001',
  agent_id: 'agent-e2e-aaa',
  command_type: 'isolate',
  status: 'Pending',
  issued_at: '2026-06-21T12:01:00+00:00',
};

// ── Client-side navigation helper ────────────────────────────

/**
 * Navigate to a path WITHOUT a full page reload, preserving in-memory auth
 * tokens (Zustand store). Uses the History API + synthetic popstate to trigger
 * React Router without resetting the browser context.
 *
 * Use this instead of page.goto() for all post-login navigation in tests.
 */
export async function navigateTo(page: Page, path: string): Promise<void> {
  await page.evaluate((p) => {
    window.history.pushState({}, '', p);
    window.dispatchEvent(new PopStateEvent('popstate', { state: window.history.state }));
  }, path);
  // Wait for React Router to process the navigation. ProtectedRoute may immediately
  // redirect to /dashboard for unauthorised roles, so we cannot assert on `path` here.
  // A short pause is enough — subsequent assertions (toHaveURL, toBeVisible) auto-wait.
  await page.waitForTimeout(300);
}

// ── Route setup helper ───────────────────────────────────────

export type Role = keyof typeof MOCK_ME;

/**
 * Set up all default API mocks for the given role.
 * Call BEFORE page.goto() so no requests escape to the (absent) backend.
 */
export async function setupMocks(page: Page, role: Role): Promise<void> {
  // Auth
  await page.route('**/api/v1/auth/login', (r) =>
    r.fulfill({ status: 200, json: MOCK_TOKENS }));
  await page.route('**/api/v1/auth/refresh', (r) =>
    r.fulfill({ status: 200, json: MOCK_TOKENS }));
  await page.route('**/api/v1/auth/logout', (r) =>
    r.fulfill({ status: 200, json: { detail: 'Logged out successfully' } }));

  // Me
  await page.route('**/api/v1/dashboard/me', (r) =>
    r.fulfill({ status: 200, json: MOCK_ME[role] }));

  // Dashboard metrics
  await page.route('**/api/v1/dashboard/metrics', (r) =>
    r.fulfill({ status: 200, json: MOCK_METRICS }));

  // Alerts — detail before list (more specific patterns registered first = lower priority in LIFO)
  await page.route('**/api/v1/dashboard/alerts/*/status', (r) =>
    r.fulfill({ status: 200, json: MOCK_ALERT_INVESTIGATING }));
  await page.route('**/api/v1/dashboard/alerts/alert-e2e-001', (r) =>
    r.fulfill({ status: 200, json: MOCK_ALERT_NEW }));
  // List — catch-all (registered last = highest priority, matches /alerts and /alerts?...)
  await page.route('**/api/v1/dashboard/alerts*', (r) => {
    const url = r.request().url();
    if (url.includes('/status')) return r.continue();
    if (url.match(/\/alerts\/[^?]+$/)) return r.continue();
    return r.fulfill({ status: 200, json: MOCK_ALERTS_LIST });
  });

  // Agents
  await page.route('**/api/v1/dashboard/agents?**', (r) =>
    r.fulfill({ status: 200, json: MOCK_AGENTS_LIST }));
  await page.route('**/api/v1/dashboard/agents', (r) =>
    r.fulfill({ status: 200, json: MOCK_AGENTS_LIST }));
  await page.route('**/api/v1/dashboard/agents/agent-e2e-aaa', (r) =>
    r.fulfill({ status: 200, json: MOCK_AGENT }));

  // Commands
  await page.route('**/api/v1/dashboard/commands', (r) =>
    r.fulfill({ status: 201, json: MOCK_COMMAND }));

  // Users
  await page.route('**/api/v1/dashboard/users?**', (r) =>
    r.fulfill({ status: 200, json: MOCK_USERS_LIST }));
  await page.route('**/api/v1/dashboard/users', (r) =>
    r.fulfill({ status: 200, json: MOCK_USERS_LIST }));

  // Audit logs
  await page.route('**/api/v1/dashboard/audit-logs?**', (r) =>
    r.fulfill({ status: 200, json: MOCK_AUDIT_LOGS }));
  await page.route('**/api/v1/dashboard/audit-logs', (r) =>
    r.fulfill({ status: 200, json: MOCK_AUDIT_LOGS }));
}

/**
 * Perform the full login flow for the given role.
 * Assumes setupMocks() has already been called.
 */
export async function loginAs(page: Page, role: Role): Promise<void> {
  await page.goto('/auth/login');
  await page.getByLabel(/code établissement/i).fill('hopital-yde');
  await page.getByLabel(/^email/i).fill(MOCK_ME[role].email);
  await page.locator('#password').fill('MotDePasseTest123!');
  await page.getByRole('button', { name: /se connecter/i }).click();
  await page.waitForURL('**/dashboard');
}

// ── Custom test fixture ──────────────────────────────────────

export const test = base.extend<{ asRole: (role: Role) => Promise<void> }>({
  asRole: async ({ page }, use) => {
    const helper = async (role: Role) => {
      await setupMocks(page, role);
      await loginAs(page, role);
    };
    // eslint-disable-next-line react-hooks/rules-of-hooks
    await use(helper);
  },
});
