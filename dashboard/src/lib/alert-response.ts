/**
 * The response to an alert (ADR-026, J1 § 1.3): what the agent did, what the console asked, and
 * where that request stands -- always the agent's own word, never a status made up here.
 */

import type { AlertCommand, AlertDetail, AlertResponse } from '@/api/alerts';
import type { Role } from '@/api/me';

export type CommandStatusView = 'sent' | 'received' | 'executed' | 'not_executed';

export interface CommandView {
  id: string;
  type: string;
  status: CommandStatusView;
  /** The agent's closed reason when not executed (not_implemented, pid_reused, …). */
  reason: string | null;
  issuedAt: string;
}

export type ResponseState = 'suspended' | 'resumed' | 'suspend_failed' | 'isolation_recommended' | 'none';

export interface ResponseView {
  state: ResponseState;
  suspensionId: string | null;
  processId: number | null;
  processName: string | null;
  reason: string | null;
  lastCommand: CommandView | null;
  canSuspend: boolean;
  canResume: boolean;
}

/** Commands only tenant_admin may issue (the GRID enforces it: 403 otherwise). */
const COMMAND_ROLES: readonly Role[] = ['tenant_admin'] as const;

function commandView(command: AlertCommand): CommandView {
  let status: CommandStatusView;
  switch (command.status) {
    case 'Pending':
      status = 'sent';
      break;
    case 'Dispatched':
    case 'Acknowledged':
      status = 'received';
      break;
    case 'Completed':
      status = command.success === false ? 'not_executed' : 'executed';
      break;
    default:
      status = 'not_executed';
  }
  const reason = command.result?.reason;
  return {
    id: command.id,
    type: command.command_type,
    status,
    reason: typeof reason === 'string' ? reason : null,
    issuedAt: command.issued_at,
  };
}

export function responseView(alert: AlertDetail, role: Role): ResponseView {
  const responses = [...(alert.responses ?? [])].sort((a, b) => a.detected_at.localeCompare(b.detected_at));
  const commands = [...(alert.commands ?? [])].sort((a, b) => a.issued_at.localeCompare(b.issued_at));
  const last = commands[commands.length - 1];
  const lastCommand = last ? commandView(last) : null;

  const latest: AlertResponse | undefined = responses[responses.length - 1];
  let state: ResponseState = 'none';
  let suspensionId: string | null = null;
  let processId: number | null = null;
  let processName: string | null = null;
  let reason: string | null = null;

  if (latest) {
    suspensionId = latest.suspension_id ?? null;
    processId = latest.process_id ?? null;
    processName = latest.process_name ?? null;
    reason = latest.reason ?? null;
    switch (latest.response) {
      case 'process_suspended':
        state = 'suspended';
        break;
      case 'suspend_failed':
        state = 'suspend_failed';
        break;
      case 'isolation_recommended':
        state = 'isolation_recommended';
        break;
      default:
        state = 'none';
    }
  }

  // A resume the agent executed after the suspension: the process runs again. The agent sends no
  // alert for a resume; its answer to the command is the only word on it.
  if (state === 'suspended') {
    const resumed = commands.find(
      (c) => c.command_type === 'resume_process' && c.status === 'Completed' && c.success !== false
        && (!latest || c.issued_at >= latest.detected_at),
    );
    if (resumed) state = 'resumed';
  }

  const open = lastCommand !== null && (lastCommand.status === 'sent' || lastCommand.status === 'received');
  const mayCommand = COMMAND_ROLES.includes(role) && !open;
  const resolved = alert.attribution?.state === 'resolved' && alert.attribution.process_id != null;

  return {
    state,
    suspensionId,
    processId,
    processName,
    reason,
    lastCommand,
    canSuspend: mayCommand && resolved && state !== 'suspended',
    canResume: mayCommand && state === 'suspended' && suspensionId !== null,
  };
}

/** True while a command of this alert waits for the agent's answer: the page polls. */
export function hasOpenCommand(alert: AlertDetail | undefined): boolean {
  return (alert?.commands ?? []).some((c) => c.status === 'Pending' || c.status === 'Dispatched' || c.status === 'Acknowledged');
}
