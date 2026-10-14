/**
 * e2e/agents.spec.ts — EPIC-AGENTS happy paths + RBAC
 * AC4.x: List, detail, isolate command (admin-only).
 */

import { test, expect, AxeBuilder, setupMocks, loginAs, navigateTo, MOCK_AGENT, MOCK_COMMAND } from './fixtures/index';

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
    // Status badge "Actif" visible for the mock agent
    await expect(page.getByText(/actif/i).first()).toBeVisible();
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

test.describe('Agent detail — tenant_admin isolate command', () => {
  test('Isolate flow: reason 20+ chars + ack checkbox → command issued (AC4.3.x)', async ({ page }) => {
    await setupMocks(page, 'tenant_admin');
    await page.route('**/api/v1/dashboard/agents/agent-e2e-aaa', (r) =>
      r.fulfill({ status: 200, json: MOCK_AGENT }));
    await page.route('**/api/v1/dashboard/commands', (r) =>
      r.fulfill({ status: 201, json: MOCK_COMMAND }));

    await loginAs(page, 'tenant_admin');
    await navigateTo(page, '/agents/agent-e2e-aaa');

    // Isolate button visible for admin
    const isolateBtn = page.getByRole('button', { name: /isoler/i });
    await expect(isolateBtn).toBeVisible();
    await isolateBtn.click();

    // Modal opens
    const modal = page.getByRole('dialog');
    await expect(modal).toBeVisible();

    // Fill reason (min chars required)
    const reason = 'Suspicion de compromission ransomware — processus chiffrant détecté en cours d\'exécution.';
    await modal.getByRole('textbox').fill(reason);

    // Check acknowledgement checkbox
    await modal.getByRole('checkbox').check();

    // Submit button becomes enabled
    const submitBtn = modal.getByRole('button', { name: /isoler/i }).last();
    await expect(submitBtn).toBeEnabled();
    await submitBtn.click();

    // Until the agent executes commands (Sprint 9), the console says the command is recorded,
    // never that the endpoint is isolated, and shows no status made up in the browser.
    await expect(modal.getByRole('status')).toHaveText(
      "La commande est enregistrée ; son exécution sur le poste n'est pas encore disponible. Le poste n'est pas isolé.",
    );
    await expect(page.getByText(/en attente|pending/i)).toHaveCount(0);
  });

  test('Isolate submit disabled with short reason or unchecked ack', async ({ page }) => {
    await setupMocks(page, 'tenant_admin');
    await page.route('**/api/v1/dashboard/agents/agent-e2e-aaa', (r) =>
      r.fulfill({ status: 200, json: MOCK_AGENT }));

    await loginAs(page, 'tenant_admin');
    await navigateTo(page, '/agents/agent-e2e-aaa');

    await page.getByRole('button', { name: /isoler/i }).click();
    const modal = page.getByRole('dialog');

    // Short reason, no ack → disabled
    await modal.getByRole('textbox').fill('Court');
    const submitBtn = modal.getByRole('button', { name: /isoler/i }).last();
    await expect(submitBtn).toBeDisabled();
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
        name: 'Protection partielle — EXFIL WATCH inactif : droits administrateur manquants',
      }),
    ).toBeVisible();
    for (const name of ['SENTINEL', 'ENTROPY', 'GENEALOGY', 'USB GUARD', 'EXFIL WATCH', 'ANTI-TAMPERING']) {
      await expect(page.getByRole('cell', { name, exact: true })).toBeVisible();
    }
    await expect(page.getByText('Alertes non transmises')).toBeVisible();
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

  test('the list shows the verdict', async ({ page }) => {
    await setupMocks(page, 'tenant_admin');
    await loginAs(page, 'tenant_admin');
    await navigateTo(page, '/agents');

    await expect(page.getByText('Partielle', { exact: true })).toBeVisible();
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
    await expect(page.getByRole('button', { name: /isoler/i })).not.toBeAttached();
  });
});

test.describe('Add agent — install command (GRID-SEC-006)', () => {
  const SHA = 'C0FFEE'.repeat(10) + 'BEEF';

  test('one command: TLS validated, under %TEMP%, SHA-256 of the served installer checked first', async ({ page }) => {
    await setupMocks(page, 'tenant_admin');
    await page.route('**/api/v1/dashboard/agents/provision', (r) =>
      r.fulfill({ status: 200, json: { otp: 'RG-CAM-2026-X7K9P3', expires_in_minutes: 30, installer_sha256: SHA } }));
    await loginAs(page, 'tenant_admin');
    await navigateTo(page, '/agents');

    await page.getByRole('button', { name: /ajouter un agent/i }).click();

    const command = page.getByTestId('install-command');
    await expect(command).toContainText(`(Get-FileHash -Algorithm SHA256 $f).Hash -ne '${SHA}'`);
    await expect(page.getByTestId('installer-sha256')).toHaveText(SHA);
    const text = (await command.textContent()) ?? '';
    expect(text).not.toMatch(/(^|\s)-k(\s|$)/);
    expect(text).not.toMatch(/C:\\/i);
    expect(text).not.toMatch(/SkipCertificateCheck/i);
    expect(text).toContain('Join-Path $env:TEMP');

    const results = await new AxeBuilder({ page }).analyze();
    const critical = results.violations.filter((v) => v.impact === 'critical' || v.impact === 'serious');
    expect(critical, `Critical/serious a11y violations: ${JSON.stringify(critical, null, 2)}`).toEqual([]);
  });

  test('no installer published: no command at all', async ({ page }) => {
    await setupMocks(page, 'tenant_admin');
    await page.route('**/api/v1/dashboard/agents/provision', (r) =>
      r.fulfill({ status: 200, json: { otp: 'RG-CAM-2026-X7K9P3', expires_in_minutes: 30, installer_sha256: null } }));
    await loginAs(page, 'tenant_admin');
    await navigateTo(page, '/agents');

    await page.getByRole('button', { name: /ajouter un agent/i }).click();

    await expect(page.getByRole('alert')).toContainText("Aucun programme d'installation n'est publié");
    await expect(page.getByTestId('install-command')).toHaveCount(0);
  });
});
