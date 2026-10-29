/**
 * e2e/suspended-processes.spec.ts — "Suspended processes" on the agent page (ADR-026, B1.6).
 *
 * The list shows what the agent reports as frozen. Resuming asks through the alert command, where
 * the GRID takes the PID from the alert's attribution: the console names the suspension, never a
 * process of its own choosing. A resume that failed stays listed and says nothing can unfreeze it.
 */

import { test, expect, AxeBuilder, setupMocks, loginAs, navigateTo, MOCK_AGENT } from './fixtures/index';
import type { Page } from '@playwright/test';

const SUSPENSION = {
  suspension_id: '11111111-2222-3333-4444-555555555555',
  alert_id: 'alert-e2e-001',
  process_id: 4242,
  process_name: 'evil',
  canary_path: String.raw`C:\Users\alice\Documents\0001_dossier_patient_ab12cd34.txt`,
  suspended_at: new Date(Date.now() - 12 * 60 * 1000).toISOString(),
  resume_failed: false,
  escalations: 0,
};

async function mockAgent(page: Page, suspensions: unknown[]): Promise<void> {
  await page.route('**/api/v1/dashboard/agents/agent-e2e-aaa', (r) =>
    r.fulfill({ status: 200, json: MOCK_AGENT }));
  await page.route('**/api/v1/dashboard/agents/agent-e2e-aaa/suspensions', (r) =>
    r.fulfill({ status: 200, json: suspensions }));
}

test.describe('Agent detail — suspended processes', () => {
  test('a frozen process is listed with its PID and the decoy file it wrote', async ({ page }) => {
    await setupMocks(page, 'tenant_admin');
    await mockAgent(page, [SUSPENSION]);
    await loginAs(page, 'tenant_admin');
    await navigateTo(page, '/agents/agent-e2e-aaa');

    const section = page.getByTestId('suspended-processes');
    await expect(section).toBeVisible();
    await expect(section.getByText('evil (PID 4242)')).toBeVisible();
    await expect(section.getByText(/0001_dossier_patient_ab12cd34\.txt/)).toBeVisible();

    const results = await new AxeBuilder({ page }).include('[data-testid="suspended-processes"]').analyze();
    const critical = results.violations.filter((v) => v.impact === 'critical' || v.impact === 'serious');
    expect(critical, `Critical/serious a11y violations: ${JSON.stringify(critical, null, 2)}`).toEqual([]);
  });

  test('nothing frozen: no section at all, not an empty box', async ({ page }) => {
    await setupMocks(page, 'tenant_admin');
    await mockAgent(page, []);
    await loginAs(page, 'tenant_admin');
    await navigateTo(page, '/agents/agent-e2e-aaa');

    await expect(page.getByText(MOCK_AGENT.hostname).first()).toBeVisible();
    await expect(page.getByTestId('suspended-processes')).toHaveCount(0);
  });

  test('Resume asks for a confirmation, then sends the command naming the suspension', async ({ page }) => {
    await setupMocks(page, 'tenant_admin');
    await mockAgent(page, [SUSPENSION]);
    let sent: unknown = null;
    await page.route('**/api/v1/dashboard/commands', (r) => {
      sent = r.request().postDataJSON();
      return r.fulfill({
        status: 201,
        json: {
          id: 'cmd-e2e-resume', agent_id: 'agent-e2e-aaa', command_type: 'resume_process',
          status: 'Pending', issued_at: new Date().toISOString(),
        },
      });
    });
    await loginAs(page, 'tenant_admin');
    await navigateTo(page, '/agents/agent-e2e-aaa');

    await page.getByTestId('resume-process').click();

    // The warning is explicit about what resuming can mean.
    const confirm = page.getByTestId('resume-confirm');
    await expect(confirm).toContainText('it will carry on');
    expect(sent, 'nothing is sent before the confirmation').toBeNull();

    await confirm.getByRole('button', { name: 'Resume the process' }).click();

    // The console names the suspension and its alert; no PID of its own choosing.
    expect(sent).toEqual({
      agent_id: 'agent-e2e-aaa',
      command_type: 'resume_process',
      parameters: { alert_id: 'alert-e2e-001', suspension_id: SUSPENSION.suspension_id },
    });
    await expect(page.getByText('Resume asked; the agent answers on the alert.')).toBeVisible();
  });

  test('a resume that failed stays listed, says why, and offers no button', async ({ page }) => {
    await setupMocks(page, 'tenant_admin');
    await mockAgent(page, [{ ...SUSPENSION, resume_failed: true, escalations: 3 }]);
    await loginAs(page, 'tenant_admin');
    await navigateTo(page, '/agents/agent-e2e-aaa');

    const section = page.getByTestId('suspended-processes');
    await expect(section.getByTestId('resume-failed')).toContainText('nothing can unfreeze it');
    await expect(section.getByText('3', { exact: true })).toBeVisible(); // reminders sent with no decision
    await expect(section.getByTestId('resume-process')).toHaveCount(0);
  });

  test('an analyst sees the frozen process and gets no Resume button', async ({ page }) => {
    await setupMocks(page, 'security_analyst');
    await mockAgent(page, [SUSPENSION]);
    await loginAs(page, 'security_analyst');
    await navigateTo(page, '/agents/agent-e2e-aaa');

    const section = page.getByTestId('suspended-processes');
    await expect(section.getByText('evil (PID 4242)')).toBeVisible();
    await expect(section.getByTestId('resume-process')).toHaveCount(0);
  });

  test('a failed request is said, and nothing claims the process resumed', async ({ page }) => {
    await setupMocks(page, 'tenant_admin');
    await mockAgent(page, [SUSPENSION]);
    await page.route('**/api/v1/dashboard/commands', (r) =>
      r.fulfill({ status: 503, json: { detail: 'down' } }));
    await loginAs(page, 'tenant_admin');
    await navigateTo(page, '/agents/agent-e2e-aaa');

    await page.getByTestId('resume-process').click();
    await page.getByTestId('resume-confirm').getByRole('button', { name: 'Resume the process' }).click();

    await expect(page.getByText('The request could not be sent.')).toBeVisible();
    await expect(page.getByText('Resume asked; the agent answers on the alert.')).toHaveCount(0);
    await expect(page.getByTestId('suspended-processes')).toBeVisible(); // still frozen
  });
});
