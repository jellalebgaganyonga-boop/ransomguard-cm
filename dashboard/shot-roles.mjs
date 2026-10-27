import { chromium } from '@playwright/test';

import { readFileSync } from 'node:fs';
const [email, password, tenant] = JSON.parse(readFileSync(process.argv[3], 'utf8'));

const out = process.argv[2];
const browser = await chromium.launch();
const page = await browser.newPage({ viewport: { width: 1440, height: 900 } });

await page.goto('http://localhost:5175/auth/login');
await page.getByLabel(/organization code/i).fill(tenant);
await page.getByLabel(/email/i).fill(email);
await page.getByRole('textbox', { name: /password/i }).fill(password);
await page.getByRole('button', { name: /sign in/i }).click();
await page.waitForURL(/\/(dashboard|agents|alerts)?$/, { timeout: 30000 }).catch(() => {});
await page.waitForTimeout(2500);

await page.goto('http://localhost:5175/users');
await page.waitForTimeout(2500);
await page.screenshot({ path: `${out}/roles-1-users-list.png` });

const row = page.locator('tr', { hasText: 'kelyfa3@gmail.com' }).first();
await row.getByRole('button', { name: /role/i }).first().click();
await page.waitForTimeout(2500);
await page.screenshot({ path: `${out}/roles-2-dialog-preticked.png` });

console.log('DIALOG TEXT START');
console.log(await page.getByRole('dialog').innerText());
console.log('DIALOG TEXT END');

await page.getByRole('button', { name: /save roles/i }).click();
await page.waitForTimeout(3500);
await page.screenshot({ path: `${out}/roles-3-after-save.png` });
console.log('dialogs open after save:', await page.getByRole('dialog').count());
console.log('error banners on page:', await page.getByRole('alert').count());

await page.goto('http://localhost:5175/audit');
await page.waitForTimeout(5000);
await page.screenshot({ path: `${out}/roles-4-audit-log.png` });
console.log('AUDIT PAGE START');
console.log((await page.locator('main').innerText()).slice(0, 600));
console.log('AUDIT PAGE END');

await browser.close();
