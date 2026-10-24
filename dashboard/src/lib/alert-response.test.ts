import { describe, expect, it } from 'vitest';
import type { AlertDetail } from '@/api/alerts';
import { hasOpenCommand, responseView } from './alert-response';

/**
 * The response section (ADR-026, J1 § 1.3): the agent's word only. A command shows the status the
 * GRID holds for it; a suspension is one the agent reported; the buttons follow the role and the
 * state, and vanish while a command waits for its answer.
 */

const BASE: AlertDetail = {
  id: 'a1', detected_at: '2026-10-21T09:57:00Z', ingested_at: '2026-10-21T09:57:01Z', severity: 'Critical',
  status: 'New', agent_id: 'ag1', alert_type: 'SentinelCanaryModified', mitre_technique_id: 'T1486', summary: 'x',
};
const RESOLVED = { state: 'resolved', process_id: 4242, process_name: 'evil', process_created_at: '2026-10-21T09:56:00Z' };
const SUSPENDED = {
  alert_id: 'r1', alert_type: 'SentinelProcessSuspended', response: 'process_suspended',
  detected_at: '2026-10-21T09:57:05Z', suspension_id: 'susp-1', process_id: 4242, process_name: 'evil',
};
const command = (over: Partial<NonNullable<AlertDetail['commands']>[number]>) => ({
  id: 'c1', command_type: 'suspend_process', status: 'Pending', issued_at: '2026-10-21T10:00:00Z', ...over,
});

describe('responseView', () => {
  it('nothing reported, nothing asked: no state, and tenant_admin may suspend a resolved writer', () => {
    const view = responseView({ ...BASE, attribution: RESOLVED }, 'tenant_admin');
    expect(view.state).toBe('none');
    expect(view.canSuspend).toBe(true);
    expect(view.canResume).toBe(false);
  });

  it('no resolved attribution: nobody to suspend, no button', () => {
    expect(responseView({ ...BASE, attribution: { state: 'unavailable', reason: 'system_pid' } }, 'tenant_admin').canSuspend).toBe(false);
    expect(responseView({ ...BASE, attribution: { state: 'pending' } }, 'tenant_admin').canSuspend).toBe(false);
  });

  it('a suspension the agent reported: suspended, resume offered to tenant_admin only', () => {
    const admin = responseView({ ...BASE, attribution: RESOLVED, responses: [SUSPENDED] }, 'tenant_admin');
    expect(admin.state).toBe('suspended');
    expect(admin.suspensionId).toBe('susp-1');
    expect(admin.processName).toBe('evil');
    expect(admin.canResume).toBe(true);
    expect(admin.canSuspend).toBe(false);

    const analyst = responseView({ ...BASE, attribution: RESOLVED, responses: [SUSPENDED] }, 'security_analyst');
    expect(analyst.state).toBe('suspended');
    expect(analyst.canResume).toBe(false);
    expect(analyst.canSuspend).toBe(false);
  });

  it('a command waiting for the agent hides the buttons and reads sent, then received', () => {
    const sent = responseView({ ...BASE, attribution: RESOLVED, commands: [command({ status: 'Pending' })] }, 'tenant_admin');
    expect(sent.lastCommand).toMatchObject({ type: 'suspend_process', status: 'sent', reason: null });
    expect(sent.canSuspend).toBe(false);

    const received = responseView({ ...BASE, attribution: RESOLVED, commands: [command({ status: 'Dispatched' })] }, 'tenant_admin');
    expect(received.lastCommand?.status).toBe('received');
  });

  it("the agent's answer: executed, or not executed with the agent's reason", () => {
    const done = responseView({ ...BASE, attribution: RESOLVED, commands: [command({ status: 'Completed', success: true })] }, 'tenant_admin');
    expect(done.lastCommand?.status).toBe('executed');

    const refused = responseView(
      { ...BASE, attribution: RESOLVED, commands: [command({ status: 'Completed', success: false, result: { reason: 'pid_reused' } })] },
      'tenant_admin',
    );
    expect(refused.lastCommand).toMatchObject({ status: 'not_executed', reason: 'pid_reused' });
    expect(refused.canSuspend).toBe(true); // answered: a new command may be sent
  });

  it('an executed resume after the suspension: the process runs again', () => {
    const view = responseView({
      ...BASE, attribution: RESOLVED, responses: [SUSPENDED],
      commands: [command({ id: 'c2', command_type: 'resume_process', status: 'Completed', success: true, issued_at: '2026-10-21T10:05:00Z' })],
    }, 'tenant_admin');
    expect(view.state).toBe('resumed');
    expect(view.canResume).toBe(false);
    expect(view.canSuspend).toBe(true);
  });

  it('a failed suspension and an isolation recommendation keep their reason', () => {
    const failed = responseView({ ...BASE, responses: [{ ...SUSPENDED, response: 'suspend_failed', reason: 'access_denied' }] }, 'security_analyst');
    expect(failed.state).toBe('suspend_failed');
    expect(failed.reason).toBe('access_denied');
    expect(responseView({ ...BASE, responses: [{ ...SUSPENDED, response: 'isolation_recommended' }] }, 'security_analyst').state).toBe('isolation_recommended');
  });

  it('the latest command is the one shown', () => {
    const view = responseView({
      ...BASE, attribution: RESOLVED,
      commands: [command({ id: 'old', status: 'Failed', issued_at: '2026-10-21T09:58:00Z' }), command({ id: 'new', status: 'Pending', issued_at: '2026-10-21T10:00:00Z' })],
    }, 'tenant_admin');
    expect(view.lastCommand?.id).toBe('new');
  });
});

describe('hasOpenCommand', () => {
  it('true while a command waits for the agent', () => {
    expect(hasOpenCommand({ ...BASE, commands: [command({ status: 'Pending' })] })).toBe(true);
    expect(hasOpenCommand({ ...BASE, commands: [command({ status: 'Dispatched' })] })).toBe(true);
    expect(hasOpenCommand({ ...BASE, commands: [command({ status: 'Completed', success: true })] })).toBe(false);
    expect(hasOpenCommand({ ...BASE })).toBe(false);
    expect(hasOpenCommand(undefined)).toBe(false);
  });
});
