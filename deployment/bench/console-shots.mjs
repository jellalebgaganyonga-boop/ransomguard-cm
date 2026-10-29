// Screenshots of the console half of the owner's demo, against the real stack.
//
// Playwright resolves from the console's own node_modules, so it runs from there:
//
//   cd dashboard
//   node ../deployment/bench/console-shots.mjs <step> <outDir>
//
// Steps: signin, counts, agents, alert, resume, resumed, agent-detail.
// The administrator's credentials come from the environment -- RG_TENANT, RG_ADMIN_EMAIL,
// RG_ADMIN_PASSWORD, read from deployment/bench/credentials.local.md by the caller -- and are
// never written to a file or printed.
import { chromium } from '@playwright/test';

const [, , step, outDir] = process.argv;
const BASE = 'http://localhost:5175';

const signIn = async (page) => {
  await page.goto(BASE + '/auth/login', { waitUntil: 'networkidle' });
  if (!page.url().includes('/auth/login')) return;
  await page.getByLabel(/organization code/i).fill(process.env.RG_TENANT);
  await page.getByLabel(/^email/i).fill(process.env.RG_ADMIN_EMAIL);
  await page.locator('#password').fill(process.env.RG_ADMIN_PASSWORD);
  await page.getByRole('button', { name: /sign in|log in/i }).click();
  await page.waitForURL((u) => !u.toString().includes('/auth/login'), { timeout: 30000 });
  await page.waitForLoadState('networkidle');
};

const shot = async (page, name) => {
  await page.waitForTimeout(1500);
  await page.screenshot({ path: `${outDir}/${name}`, fullPage: false });
  console.log(name);
};

const run = async () => {
  const browser = await chromium.launch();
  const page = await browser.newPage({ viewport: { width: 1440, height: 900 } });

  if (step === 'signin') {
    await page.goto(BASE + '/auth/login', { waitUntil: 'networkidle' });
    await shot(page, '02-console-sign-in-page.png');
    await signIn(page);
    await shot(page, '03-signed-in-as-the-hospital-administrator.png');
  } else if (step === 'agents') {
    await signIn(page);
    await page.goto(BASE + '/agents', { waitUntil: 'networkidle' });
    await shot(page, '07-pc-reception-active-in-the-console.png');
  } else if (step === 'counts') {
    await signIn(page);
    await page.goto(BASE + '/', { waitUntil: 'networkidle' });
    await shot(page, '04-dashboard-counts-one-fleet.png');
  } else if (step === 'alert') {
    await signIn(page);
    await page.goto(BASE + '/alerts', { waitUntil: 'networkidle' });
    await shot(page, '09a-the-detection-in-the-alert-list.png');
    const row = page.getByRole('row').filter({ hasText: /canary|sentinel|decoy/i }).first();
    if (await row.count()) {
      await row.click();
      await page.waitForLoadState('networkidle');
      await shot(page, '09b-the-writer-named-and-suspended.png');
    }
  } else if (step === 'resume') {
    await signIn(page);
    await page.goto(BASE + '/alerts', { waitUntil: 'networkidle' });
    const row = page.getByRole('row').filter({ hasText: /suspended/i }).first();
    await row.click();
    await page.waitForLoadState('networkidle');
    const resume = page.getByRole('button', { name: /resume the process/i }).first();
    await resume.click();
    const confirm = page.getByRole('button', { name: /confirm|yes|send/i }).first();
    if (await confirm.count()) await confirm.click();
    await page.waitForTimeout(3000);
    await shot(page, '10a-resume-requested-from-the-console.png');
  } else if (step === 'resumed') {
    await signIn(page);
    await page.goto(BASE + '/alerts', { waitUntil: 'networkidle' });
    const row = page.getByRole('row').filter({ hasText: /suspended/i }).first();
    await row.click();
    await page.waitForLoadState('networkidle');
    await shot(page, '10b-the-process-resumed.png');
  } else if (step === 'agent-detail') {
    await signIn(page);
    await page.goto(BASE + '/agents', { waitUntil: 'networkidle' });
    const row = page.getByRole('row').filter({ hasText: /PC-RECEPTION/i }).first();
    if (await row.count()) {
      await row.click();
      await page.waitForLoadState('networkidle');
      await shot(page, '10-the-workstation-page-after-resume.png');
    }
  }

  await browser.close();
};

run().catch((e) => {
  console.error('FAILED:', e.message);
  process.exit(1);
});
