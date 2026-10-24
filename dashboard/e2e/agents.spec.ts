/**
 * e2e/agents.spec.ts — EPIC-AGENTS happy paths + RBAC
 * AC4.x: List, detail, isolate command (admin-only).
 */

import { test, expect, AxeBuilder, setupMocks, loginAs, navigateTo, MOCK_AGENT } from './fixtures/index';

test.describe('Agents list', () => {
  test('tenant_admin sees agent list with hostname', async ({ page }) => {
    await setupMocks(page, 'tenant_admin');
    await loginAs(page, 'tenant_admin');

    await navigateTo(page, '/agents');
    await expect(page.getByText('ws-cardio-01', { exact: true }).first()).toBeVisible();
  });

  test('status filter shows results', async ({ page }) => {
    await setupMocks(page, 'tenant_admin');
    await loginAs(page, 'tenant_admin');

    await navigateTo(page, '/agents');
    // Status badge "Active" visible for the mock agent
    await expect(page.getByText(/active/i).first()).toBeVisible();
  });

  test('@a11y accessibility: agents list', async ({ page }) => {
    await setupMocks(page, 'tenant_admin');
    await loginAs(page, 'tenant_admin');
    await navigateTo(page, '/agents');

    const results = await new AxeBuilder({ page }).analyze();
    const critical = results.violations.filter(
      (v) => v.impact === 'critical' || v.impact === 'serious',
    );
    expect(critical, `Critical/serious a11y violations: ${JSON.stringify(critical, null, 2)}`).toEqual([]);
  });
});

test.describe('Agent detail — no isolate command (ADR-026)', () => {
  test('the "Isolate" button is gone: the response lives on the alert, where the process is named', async ({ page }) => {
    await setupMocks(page, 'tenant_admin');
    await page.route('**/api/v1/dashboard/agents/agent-e2e-aaa', (r) =>
      r.fulfill({ status: 200, json: MOCK_AGENT }));

    await loginAs(page, 'tenant_admin');
    await navigateTo(page, '/agents/agent-e2e-aaa');

    await expect(page.getByText(MOCK_AGENT.hostname).first()).toBeVisible();
    // It queued a command the agent never executed; nothing here may suggest an isolation exists.
    await expect(page.getByRole('button', { name: /isolate/i })).toHaveCount(0);
  });

  test('@a11y accessibility: agent detail', async ({ page }) => {
    await setupMocks(page, 'tenant_admin');
    await page.route('**/api/v1/dashboard/agents/agent-e2e-aaa', (r) =>
      r.fulfill({ status: 200, json: MOCK_AGENT }));

    await loginAs(page, 'tenant_admin');
    await navigateTo(page, '/agents/agent-e2e-aaa');

    const results = await new AxeBuilder({ page }).analyze();
    const critical = results.violations.filter(
      (v) => v.impact === 'critical' || v.impact === 'serious',
    );
    expect(critical, `Critical/serious a11y violations: ${JSON.stringify(critical, null, 2)}`).toEqual([]);
  });
});

test.describe('Agent detail — posture (R8)', () => {
  test('the banner composes the whole sentence; product names, six entries', async ({ page }) => {
    await setupMocks(page, 'security_analyst');
    await loginAs(page, 'security_analyst');
    await navigateTo(page, '/agents/agent-e2e-aaa');

    await expect(
      page.getByRole('heading', {
        name: 'Partial protection — EXFIL WATCH inactive: administrator rights missing',
      }),
    ).toBeVisible();
    for (const name of ['SENTINEL', 'ENTROPY', 'GENEALOGY', 'USB GUARD', 'EXFIL WATCH', 'ANTI-TAMPERING']) {
      await expect(page.getByRole('cell', { name, exact: true })).toBeVisible();
    }
    await expect(page.getByText('Alerts not transmitted')).toBeVisible();
  });

  // WCAG 2.2 AA contrast of every banner colour, not only the orange one of MOCK_AGENT.
  for (const [state, reasons] of [
    ['unprotected', ['SENTINEL:inactive:stopped_unexpectedly', 'restart_loop']],
    ['unknown', ['never_connected']],
    ['protected', []],
  ] as const) {
    test(`@a11y accessibility: agent detail, ${state} banner`, async ({ page }) => {
      await setupMocks(page, 'tenant_admin');
      await page.route('**/api/v1/dashboard/agents/agent-e2e-aaa', (r) =>
        r.fulfill({ status: 200, json: { ...MOCK_AGENT, protection_state: state, reason_codes: reasons } }));
      await loginAs(page, 'tenant_admin');
      await navigateTo(page, '/agents/agent-e2e-aaa');
      await expect(page.getByTestId('posture-banner')).toHaveAttribute('data-state', state);

      const results = await new AxeBuilder({ page }).analyze();
      const critical = results.violations.filter(
        (v) => v.impact === 'critical' || v.impact === 'serious',
      );
      expect(critical, `Critical/serious a11y violations: ${JSON.stringify(critical, null, 2)}`).toEqual([]);
    });
  }

  // LOT 2 demo: during a cut the page showed "Alertes non transmises 0", the last value received,
  // as if it were live. Signal lost: every transmission value says when it was received.
  test('signal lost: the transmission values are marked as the last ones received, muted', async ({ page }) => {
    await setupMocks(page, 'tenant_admin');
    await page.route('**/api/v1/dashboard/agents/agent-e2e-aaa', (r) =>
      r.fulfill({
        status: 200,
        json: { ...MOCK_AGENT, protection_state: 'partial', reason_codes: ['signal_lost'], signal_age_seconds: 420 },
      }));
    await loginAs(page, 'tenant_admin');
    await navigateTo(page, '/agents/agent-e2e-aaa');

    const transmission = page.getByTestId('posture-transmission');
    // received_at 11:55 UTC = 12:55 in Douala.
    await expect(transmission.getByText('last value received at 12:55')).toHaveCount(5);
    const values = transmission.locator('dd');
    await expect(values).toHaveCount(5);
    for (const value of await values.all()) {
      await expect(value).toHaveAttribute('data-stale', 'true');
      await expect(value).toHaveClass(/text-text-secondary/);
    }

    const results = await new AxeBuilder({ page }).analyze();
    const critical = results.violations.filter((v) => v.impact === 'critical' || v.impact === 'serious');
    expect(critical, `Critical/serious a11y violations: ${JSON.stringify(critical, null, 2)}`).toEqual([]);
  });

  test('signal fresh: the transmission values are current, not marked', async ({ page }) => {
    await setupMocks(page, 'tenant_admin');
    await loginAs(page, 'tenant_admin');
    await navigateTo(page, '/agents/agent-e2e-aaa');

    const transmission = page.getByTestId('posture-transmission');
    await expect(transmission.locator('dd')).toHaveCount(5);
    await expect(transmission.getByText(/last value received/)).toHaveCount(0);
    await expect(transmission.locator('dd[data-stale]')).toHaveCount(0);
  });

  test('the list shows the verdict', async ({ page }) => {
    await setupMocks(page, 'tenant_admin');
    await loginAs(page, 'tenant_admin');
    await navigateTo(page, '/agents');

    await expect(page.getByText('Partial', { exact: true })).toBeVisible();
  });
});

test.describe('Agent detail — security_analyst RBAC (AC4.1.3)', () => {
  test('NO isolate command button visible for analyst', async ({ page }) => {
    await setupMocks(page, 'security_analyst');
    await page.route('**/api/v1/dashboard/agents/agent-e2e-aaa', (r) =>
      r.fulfill({ status: 200, json: MOCK_AGENT }));

    await loginAs(page, 'security_analyst');
    await navigateTo(page, '/agents/agent-e2e-aaa');

    // Isolate button must not be in DOM
    await expect(page.getByRole('button', { name: /isolate/i })).not.toBeAttached();
  });
});

test.describe('Add agent — the double-click installer (ADR-027)', () => {
  const PROVISION = { otp: 'RG-CAM-2026-X7K9P3', expires_in_minutes: 30, installer_sha256: null };

  test('the GRID builds the installer for this code and this host; the browser saves it', async ({ page }) => {
    await setupMocks(page, 'tenant_admin');
    await page.route('**/api/v1/dashboard/agents/provision', (r) => r.fulfill({ status: 200, json: PROVISION }));
    let sent: unknown = null;
    await page.route('**/api/v1/dashboard/agents/installer', (r) => {
      sent = r.request().postDataJSON();
      return r.fulfill({
        status: 200,
        headers: {
          'content-type': 'application/vnd.microsoft.portable-executable',
          'content-disposition': 'attachment; filename="RansomGuard-CM-Installeur-localhost.exe"',
        },
        body: Buffer.from('MZ fake installer'),
      });
    });
    await loginAs(page, 'tenant_admin');
    await navigateTo(page, '/agents');

    await page.getByRole('button', { name: /add agent/i }).click();
    await expect(page.getByTestId('enrollment-otp')).toHaveText('RG-CAM-2026-X7K9P3');
    await expect(page.getByTestId('install-command')).toHaveCount(0); // the command is gone: nothing to type

    const downloadPromise = page.waitForEvent('download');
    await page.getByTestId('download-installer').click();
    const download = await downloadPromise;

    expect(download.suggestedFilename()).toBe('RansomGuard-CM-Installeur-localhost.exe');
    // The agent channel's port, not the console's: see AGENT_CHANNEL_PORT.
    expect(sent).toEqual({ otp: 'RG-CAM-2026-X7K9P3', host: 'localhost', port: 443 });
    await expect(page.getByText(/Unknown publisher/)).toBeVisible(); // SmartScreen, said as such

    const results = await new AxeBuilder({ page }).analyze();
    const critical = results.violations.filter((v) => v.impact === 'critical' || v.impact === 'serious');
    expect(critical, `Critical/serious a11y violations: ${JSON.stringify(critical, null, 2)}`).toEqual([]);
  });

  test('no installer published: the GRID says 404 and the console says what is missing', async ({ page }) => {
    await setupMocks(page, 'tenant_admin');
    await page.route('**/api/v1/dashboard/agents/provision', (r) => r.fulfill({ status: 200, json: PROVISION }));
    await page.route('**/api/v1/dashboard/agents/installer', (r) =>
      r.fulfill({ status: 404, json: { detail: 'No double-click installer published' } }));
    await loginAs(page, 'tenant_admin');
    await navigateTo(page, '/agents');

    await page.getByRole('button', { name: /add agent/i }).click();
    await page.getByTestId('download-installer').click();

    await expect(page.getByRole('alert')).toContainText('No installer is published');
  });

  test('a code no longer usable: 409, generate a new one', async ({ page }) => {
    await setupMocks(page, 'tenant_admin');
    await page.route('**/api/v1/dashboard/agents/provision', (r) => r.fulfill({ status: 200, json: PROVISION }));
    await page.route('**/api/v1/dashboard/agents/installer', (r) =>
      r.fulfill({ status: 409, json: { detail: 'Unknown or expired enrollment code' } }));
    await loginAs(page, 'tenant_admin');
    await navigateTo(page, '/agents');

    await page.getByRole('button', { name: /add agent/i }).click();
    await page.getByTestId('download-installer').click();

    await expect(page.getByRole('alert')).toContainText('can no longer be used');
  });
});
