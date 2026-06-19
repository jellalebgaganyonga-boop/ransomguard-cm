// ============================================================
// src/api/dashboard.ts
// EPIC-DASHBOARD — API layer (Zod-validated)
//
// Endpoints consumed (from PRD v2 §7 Technical notes):
//   GET /api/v1/dashboard/metrics/summary → MetricsSummarySchema
//   GET /api/v1/dashboard/alerts          → AlertListSchema
//   GET /api/v1/dashboard/agents          → AgentListSchema
// ============================================================

import { z } from 'zod';
import { apiClient } from './client';

// ── MetricsSummary ─────────────────────────────────────────

export const MetricsSummarySchema = z.object({
  total_agents: z.number().int().nonnegative(),
  active_agents: z.number().int().nonnegative(),
  offline_agents: z.number().int().nonnegative(),
  stale_agents: z.number().int().nonnegative(),
  alerts_24h: z.number().int().nonnegative(),
  critical_24h: z.number().int().nonnegative(),
  last_critical_at: z.string().datetime().nullable(),
  compliance_score: z.number().min(0).max(100).nullable(),
});
export type MetricsSummary = z.infer<typeof MetricsSummarySchema>;

export async function fetchMetricsSummary(): Promise<MetricsSummary> {
  const res = await apiClient.get('/dashboard/metrics/summary');
  return MetricsSummarySchema.parse(res.data);
}

// ── Alert list ─────────────────────────────────────────────

export const AlertSeveritySchema = z.enum([
  'critical',
  'high',
  'medium',
  'low',
  'info',
]);
export type AlertSeverity = z.infer<typeof AlertSeveritySchema>;

export const AlertStatusSchema = z.enum([
  'new',
  'acknowledged',
  'closed',
  'false_positive',
]);
export type AlertStatus = z.infer<typeof AlertStatusSchema>;

export const AlertSummarySchema = z.object({
  id: z.string(),
  priority_score: z.number().int().min(0).max(100),
  severity: AlertSeveritySchema,
  status: AlertStatusSchema,
  agent_hostname: z.string(),
  module_name: z.string(),
  summary: z.string(),
  detected_at: z.string().datetime(),
  tenant_id: z.string(),
});
export type AlertSummary = z.infer<typeof AlertSummarySchema>;

export const AlertListResponseSchema = z.object({
  items: z.array(AlertSummarySchema),
  total: z.number().int().nonnegative(),
  page: z.number().int().positive(),
  page_size: z.number().int().positive(),
});
export type AlertListResponse = z.infer<typeof AlertListResponseSchema>;

export interface AlertListParams {
  page?: number;
  page_size?: number;
  severity?: AlertSeverity;
  status?: AlertStatus;
  module?: string;
  search?: string;
  hours?: number; // last N hours
}

export async function fetchAlerts(params: AlertListParams = {}): Promise<AlertListResponse> {
  const res = await apiClient.get('/dashboard/alerts', { params });
  return AlertListResponseSchema.parse(res.data);
}

// ── Agent list ─────────────────────────────────────────────

export const AgentStatusSchema = z.enum(['online', 'stale', 'offline', 'isolated']);
export type AgentStatus = z.infer<typeof AgentStatusSchema>;

export const AgentSummarySchema = z.object({
  id: z.string(),
  hostname: z.string(),
  os_info: z.string(),
  agent_version: z.string(),
  last_heartbeat_at: z.string().datetime().nullable(),
  status: AgentStatusSchema,
  tenant_id: z.string(),
});
export type AgentSummary = z.infer<typeof AgentSummarySchema>;

export const AgentListResponseSchema = z.object({
  items: z.array(AgentSummarySchema),
  total: z.number().int().nonnegative(),
});
export type AgentListResponse = z.infer<typeof AgentListResponseSchema>;

export async function fetchAgents(params: { page_size?: number } = {}): Promise<AgentListResponse> {
  const res = await apiClient.get('/dashboard/agents', { params });
  return AgentListResponseSchema.parse(res.data);
}
