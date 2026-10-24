/**
 * Capture console screenshots for the dissertation, at a fixed 1440x900 desktop viewport.
 *
 * The console is desktop-only, so there is one viewport here and no phone width.
 *
 * The data behind these screenshots is SYNTHETIC: it comes from
 * grid/scripts/seed_demo_data.py, which invents agents and alerts so the
 * interface has something to draw. These images show that the console renders,
 * not that anything was detected. See docs/screenshots/README.md.
 *
 * Usage, with the GRID on 8443 and Vite on 5174:
 *   node scripts/capture-screenshots.mjs
 */

import { chromium } from 'playwright';
import { mkdir } from 'node:fs/promises';
import path from 'node:path';

const CONSOLE_URL = process.env.CONSOLE_URL ?? 'http://localhost:5174';
const GRID_URL = process.env.GRID_URL ?? 'http://127.0.0.1:8443';
const OUT_DIR = path.resolve(process.cwd(), '..', 'docs', 'screenshots');

const CREDENTIALS = {
  tenant: 'hopital-yde',
  email: 'admin@hopital-yde.cm',
  password: 'MotDePasseDemo!2026',
};

const VIEWPORT = { width: 1440, height: 900 };

/** Let the list/detail queries settle, then the paint. */
async function settle(page, ms = 1200) {
  await page.waitForLoadState('networkidle').catch(() => {});
  await page.waitForTimeout(ms);
}

async function shot(page, name) {
  const file = path.join(OUT_DIR, name);
  await page.screenshot({ path: file });
  console.log(`  ${name}`);
}

async function login(page) {
  await page.goto(`${CONSOLE_URL}/auth/login`);
  await settle(page, 600);
  // Same selectors the e2e suite uses (e2e/auth.spec.ts), so this script and the
  // tests agree on what the login form is.
  await page.getByLabel(/organization code/i).fill(CREDENTIALS.tenant);
  await page.getByLabel(/^email/i).fill(CREDENTIALS.email);
  await page.locator('#password').fill(CREDENTIALS.password);
  await page.getByRole('button', { name: /^sign in$/i }).click();
  await page.waitForURL(/\/dashboard/, { timeout: 20000 });
  await settle(page);
}

/**
 * The real ids of the seeded alerts and agents, read from the GRID so the detail
 * screenshots point at whatever is actually in the database rather than a guess.
 *
 * Runs in Node, not in the page: the browser is on localhost:5174 and the GRID
 * sends no CORS header for that origin.
 */
async function token() {
  const auth = await fetch(`${GRID_URL}/api/v1/auth/login`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({
      email: CREDENTIALS.email,
      password: CREDENTIALS.password,
      tenant_code: CREDENTIALS.tenant,
    }),
  });
  if (!auth.ok) throw new Error(`login failed: ${auth.status}`);
  return (await auth.json()).access_token;
}

/**
 * Pin the stored language. The preference lives on the GRID and survives between
 * runs, so without this the French capture at step 11 would silently translate
 * every later run from step 02 onwards. English is the project default.
 */
async function setLanguage(language) {
  const res = await fetch(`${GRID_URL}/api/v1/dashboard/me/preferences`, {
    method: 'PATCH',
    headers: { 'Content-Type': 'application/json', Authorization: `Bearer ${await token()}` },
    body: JSON.stringify({ language }),
  });
  if (!res.ok) throw new Error(`setting language to ${language} failed: ${res.status}`);
}

async function fetchIds() {
  const headers = { Authorization: `Bearer ${await token()}` };

  const list = await fetch(`${GRID_URL}/api/v1/dashboard/alerts?limit=50`, { headers }).then((x) => x.json());
  const agents = await fetch(`${GRID_URL}/api/v1/dashboard/agents?limit=50`, { headers }).then((x) => x.json());

  return {
    alerts: list.items.map((a) => ({ id: a.id, type: a.alert_type, severity: a.severity })),
    agents: agents.items.map((a) => ({ id: a.id, hostname: a.hostname })),
  };
}

async function main() {
  await mkdir(OUT_DIR, { recursive: true });

  // Everything that signs in from Node happens before the browser does, and
  // nothing does afterwards: a second sign-in revokes the first session, which
  // would drop the browser back to the login page mid-capture.
  await setLanguage('en');
  const { alerts, agents } = await fetchIds();

  const browser = await chromium.launch();
  const context = await browser.newContext({ viewport: VIEWPORT });
  const page = await context.newPage();

  console.log('capturing:');

  // 01 — login page, before any credentials are entered.
  await page.goto(`${CONSOLE_URL}/auth/login`);
  await settle(page, 800);
  await shot(page, '01_login_page.png');

  await login(page);
  await shot(page, '02_dashboard_overview.png');

  await page.goto(`${CONSOLE_URL}/agents`);
  await settle(page);
  await shot(page, '03_agents_list.png');

  const urgences = agents.find((a) => a.hostname === 'PC-URGENCES-01') ?? agents[0];
  if (urgences) {
    await page.goto(`${CONSOLE_URL}/agents/${urgences.id}`);
    await settle(page);
    await shot(page, '04_agent_detail_protected.png');
  }

  await page.goto(`${CONSOLE_URL}/alerts`);
  await settle(page);
  await shot(page, '05_alerts_list.png');

  const pick = (pred) => alerts.find(pred);
  const targets = [
    ['06_alert_detail_sentinel.png', pick((a) => a.type.startsWith('Sentinel') && a.severity === 'Critical')],
    ['07_alert_detail_entropy.png', pick((a) => a.type.startsWith('Entropy'))],
    ['08_alert_detail_exfil.png', pick((a) => a.type.startsWith('ExfilWatch'))],
    ['09_alert_detail_usb_blocked.png', pick((a) => a.type === 'UsbGuard' && a.severity === 'High')],
  ];
  for (const [name, alert] of targets) {
    if (!alert) {
      console.log(`  SKIP ${name} (no matching alert)`);
      continue;
    }
    await page.goto(`${CONSOLE_URL}/alerts/${alert.id}`);
    await settle(page);
    await shot(page, name);
  }

  // 10 — Settings. There is no separate tenant page in this build; the tenant is
  // carried on the Profile tab, which is what this captures.
  await page.goto(`${CONSOLE_URL}/settings?tab=profile`);
  await settle(page);
  await shot(page, '10_settings_tenant.png');

  // 11 — the same dashboard in French, switched on the Language tab the way a
  // user does. It has to happen in the browser: every Node-side call signs in
  // again, and a second sign-in revokes the session this page is holding.
  await page.goto(`${CONSOLE_URL}/settings?tab=language`);
  await settle(page);
  await page.getByRole('tabpanel').getByRole('combobox').selectOption('fr');
  await settle(page);
  await page.goto(`${CONSOLE_URL}/dashboard`);
  await settle(page);
  await shot(page, '11_console_french.png');

  // Leave the account on the project default, so the console a human opens next
  // is English and a later run starts from English.
  await page.goto(`${CONSOLE_URL}/settings?tab=language`);
  await settle(page);
  await page.getByRole('tabpanel').getByRole('combobox').selectOption('en');
  await settle(page);

  // 12 — the GRID's own API documentation (Swagger UI), served because
  // GRID_DEBUG=true on this local run.
  await page.goto(`${GRID_URL}/docs`);
  await settle(page, 2000);
  await shot(page, '12_grid_swagger.png');

  await browser.close();
  console.log('done');
}

main().catch((error) => {
  console.error(error);
  process.exit(1);
});
