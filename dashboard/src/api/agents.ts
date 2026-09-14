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
 */

import { z } from 'zod';
import { apiClient } from './client';

// ── AgentStatus — lowercase to match Python enum ────────────

export const AgentStatusSchema = z.enum([
  'provisioned',
  'active',
  'disconnected',
  'decommissioned',
]);
export type AgentStatus = z.infer<typeof AgentStatusSchema>;

// ── AgentItem ───────────────────────────────────────────────

export const AgentItemSchema = z.object({
  id: z.string(),
  hostname: z.string(),
  fqdn: z.string(),
  os_version: z.string(),
  agent_version: z.string(),
  status: AgentStatusSchema,
  last_heartbeat_at: z.string().nullable(),
});
export type AgentItem = z.infer<typeof AgentItemSchema>;

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

export async function fetchAgentDetail(id: string): Promise<AgentItem> {
  const res = await apiClient.get(`/dashboard/agents/${id}`);
  return AgentItemSchema.parse(res.data);
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
}

export async function provisionAgent(): Promise<ProvisionAgentResponse> {
  const res = await apiClient.post('/dashboard/agents/provision');
  return res.data as ProvisionAgentResponse;
}

export async function decommissionAgent(agentId: string): Promise<void> {
  await apiClient.post(`/dashboard/agents/${agentId}/decommission`);
}
