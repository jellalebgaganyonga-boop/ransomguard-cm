/**
 * EPIC-AGENTS — API layer
 *
 * Backend contracts confirmed from grid/src/ransomguard_grid/api/v1/schemas/dashboard.py
 * and grid/src/ransomguard_grid/db/models/enums.py:
 *
 *   AgentStatus enum is LOWERCASE: provisioned / active / disconnected / decommissioned
 *   "isolated" does NOT exist as an AgentStatus value.
 *   CommandStatus IS PascalCase: Pending / Dispatched / Acknowledged / Completed / Failed
 *
 *   IssueCommandRequest: { agent_id, command_type, parameters: dict }
 *   NO "justification" field — reason goes in parameters.reason.
 *
 *   AgentItem does NOT expose enrolled_at (model has it, schema omits it — GAP-01).
 *
 *   Since R7 every agent carries protection_state / reason_codes / signal_age_seconds, and the
 *   agent page (GET /dashboard/agents/{id}) its posture.
 */

import { z } from 'zod';
import { apiClient } from './client';
import { installerFilename } from '@/lib/installer-download';

// ── AgentStatus — lowercase to match Python enum ────────────

export const AgentStatusSchema = z.enum([
  'provisioned',
  'active',
  'disconnected',
  'decommissioned',
]);
export type AgentStatus = z.infer<typeof AgentStatusSchema>;

// ── Protection verdict (computed by the GRID at read time) ──
//
// The rule lives in one place, the server (grid services/agent_posture.py): the console only
// translates and colours. reason_codes, worst first, read either a verdict-level code
// ("signal_lost", "transmission_blocked", ...) or "<MODULE>:<state>[:<reason_code>]".

export const ProtectionStateSchema = z.enum(['protected', 'partial', 'unprotected', 'unknown']);
export type ProtectionState = z.infer<typeof ProtectionStateSchema>;

// ── AgentItem ───────────────────────────────────────────────

export const AgentItemSchema = z.object({
  id: z.string(),
  hostname: z.string(),
  fqdn: z.string(),
  os_version: z.string(),
  agent_version: z.string(),
  status: AgentStatusSchema,
  last_heartbeat_at: z.string().nullable(),
  protection_state: ProtectionStateSchema.default('unknown'),
  reason_codes: z.array(z.string()).default([]),
  signal_age_seconds: z.number().int().nonnegative().nullable().default(null),
});
export type AgentItem = z.infer<typeof AgentItemSchema>;

// ── Posture (agent page only) ───────────────────────────────
// Null numbers mean "unavailable" (the agent could not measure them), never 0.

export const ModuleStatusSchema = z.object({
  code: z.string(),
  state: z.string(),
  reason_code: z.string().nullable(),
  changed_at: z.string(),
});
export type ModuleStatus = z.infer<typeof ModuleStatusSchema>;

export const AgentPostureSchema = z.object({
  schema_version: z.number().int(),
  received_at: z.string(),
  agent_uptime_seconds: z.number().int().nonnegative(),
  threat_intel_version: z.string().nullable(),
  last_detection_at: z.string().nullable(),
  system_free_bytes: z.number().int().nonnegative().nullable(),
  db_volume_free_bytes: z.number().int().nonnegative().nullable(),
  pending_alerts: z.number().int().nonnegative().nullable(),
  last_upload_ok_at: z.string().nullable(),
  last_upload_age_seconds: z.number().int().nonnegative().nullable(),
  last_upload_error_code: z.string().nullable(),
  rejected_alerts: z.number().int().nonnegative().nullable(),
  // AGT-AUD-002: audit entries the agent could not record since it started; null = not reported.
  audit_write_failures: z.number().int().nonnegative().nullable(),
  modules: z.array(ModuleStatusSchema),
});
export type AgentPosture = z.infer<typeof AgentPostureSchema>;

export const AgentDetailSchema = AgentItemSchema.extend({
  posture: AgentPostureSchema.nullable().default(null),
});
export type AgentDetail = z.infer<typeof AgentDetailSchema>;

export const PaginatedAgentResponseSchema = z.object({
  items: z.array(AgentItemSchema),
  total: z.number().int().nonnegative(),
  offset: z.number().int().nonnegative(),
  limit: z.number().int().positive(),
});
export type PaginatedAgentResponse = z.infer<typeof PaginatedAgentResponseSchema>;

// ── List params ─────────────────────────────────────────────

export interface AgentListParams {
  status?: AgentStatus;
  offset?: number;
  limit?: number;
}

export async function fetchAgentList(
  params: AgentListParams = {}
): Promise<PaginatedAgentResponse> {
  const res = await apiClient.get('/dashboard/agents', { params });
  return PaginatedAgentResponseSchema.parse(res.data);
}

// ── Detail ──────────────────────────────────────────────────

export async function fetchAgentDetail(id: string): Promise<AgentDetail> {
  const res = await apiClient.get(`/dashboard/agents/${id}`);
  return AgentDetailSchema.parse(res.data);
}

// ── CommandStatus — PascalCase matching backend enum ────────

export const CommandStatusSchema = z.enum([
  'Pending',
  'Dispatched',
  'Acknowledged',
  'Completed',
  'Failed',
]);
export type CommandStatus = z.infer<typeof CommandStatusSchema>;

// ── CommandItem ─────────────────────────────────────────────

export const CommandItemSchema = z.object({
  id: z.string(),
  agent_id: z.string(),
  command_type: z.string(),
  status: CommandStatusSchema,
  issued_at: z.string(),
});
export type CommandItem = z.infer<typeof CommandItemSchema>;

// ── IssueCommandPayload ─────────────────────────────────────
// Backend field: { agent_id, command_type, parameters: dict }
// Reason goes in parameters.reason (NOT a top-level justification field).

export interface IssueCommandPayload {
  agent_id: string;
  command_type: string;
  parameters: Record<string, unknown>;
}

export async function issueCommand(payload: IssueCommandPayload): Promise<CommandItem> {
  const res = await apiClient.post('/dashboard/commands', payload);
  return CommandItemSchema.parse(res.data);
}

// ── Provisioning ─────────────────────────────────────────────

export interface ProvisionAgentResponse {
  otp: string;
  expires_in_minutes: number;
  /** SHA-256 of the install.ps1 the GRID serves (upper-case hex); null when none is published. */
  installer_sha256: string | null;
}

export async function provisionAgent(): Promise<ProvisionAgentResponse> {
  const res = await apiClient.post('/dashboard/agents/provision');
  return res.data as ProvisionAgentResponse;
}

/** The double-click installer for one enrollment code (ADR-027): the GRID builds it, the browser saves it. */
export interface InstallerDownload {
  blob: Blob;
  filename: string;
}

export async function downloadInstaller(body: { otp: string; host: string; port: number }): Promise<InstallerDownload> {
  const res = await apiClient.post('/dashboard/agents/installer', body, { responseType: 'blob' });
  const disposition = (res.headers as Record<string, string | undefined>)['content-disposition'];
  return { blob: res.data as Blob, filename: installerFilename(disposition, body.host) };
}

export async function decommissionAgent(agentId: string): Promise<void> {
  await apiClient.post(`/dashboard/agents/${agentId}/decommission`);
}
