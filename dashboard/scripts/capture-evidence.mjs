/**
 * Render the two evidence images that are not console pages (Task O):
 *   13_test_results.png  — the test-results card in scripts/test-results-card.html
 *   15_ci_badge.png      — the repository's GitHub Actions run list
 *
 * Both at the same 1440x900 desktop viewport as the console screenshots.
 */

import { chromium } from 'playwright';
import { mkdir } from 'node:fs/promises';
import path from 'node:path';
import { pathToFileURL } from 'node:url';

const OUT_DIR = path.resolve(process.cwd(), '..', 'docs', 'screenshots');
const CARD = path.resolve(process.cwd(), 'scripts', 'test-results-card.html');
const ACTIONS_URL =
  process.env.ACTIONS_URL ??
  'https://github.com/jellalebgaganyonga-boop/ransomguard-cm/actions?query=branch%3Asprint8%2Frecovery';

const VIEWPORT = { width: 1440, height: 900 };

async function main() {
  await mkdir(OUT_DIR, { recursive: true });
  const browser = await chromium.launch();
  const page = await browser.newPage({ viewport: VIEWPORT });

  await page.goto(pathToFileURL(CARD).href);
  await page.waitForTimeout(400);
  await page.screenshot({ path: path.join(OUT_DIR, '13_test_results.png') });
  console.log('  13_test_results.png');

  // The repository is public, so the run list renders without signing in. If
  // GitHub is unreachable the image is simply not produced, and the run is not
  // claimed: no placeholder is written.
  try {
    await page.goto(ACTIONS_URL, { waitUntil: 'domcontentloaded', timeout: 30000 });
    await page.waitForTimeout(3500);
    await page.screenshot({ path: path.join(OUT_DIR, '15_ci_badge.png') });
    console.log('  15_ci_badge.png');
  } catch (error) {
    console.log(`  SKIP 15_ci_badge.png (${error.message})`);
  }

  await browser.close();
}

main().catch((error) => {
  console.error(error);
  process.exit(1);
});
