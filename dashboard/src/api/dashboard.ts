// ============================================================
// src/api/dashboard.ts
// EPIC-DASHBOARD — Metrics API layer (Zod-validated)
//
// This file contains ONLY what is unique to the metrics endpoint.
// Alert list  → use api/alerts.ts  (fetchAlertList)
// Agent list  → use api/agents.ts  (fetchAgentList)
//
// Backend reality (verified against real GRID server, 2026-06-21):
//   GET /api/v1/dashboard/metrics/summary returns exactly:
//     total_agents, active_agents, alerts_24h, critical_alerts_24h
//
// NOT present (PRD v2 §7 assumptions were wrong, BUG-01 fixed here):
//   offline_agents, stale_agents, last_critical_at, compliance_score
// ============================================================

import { z } from 'zod';
import { apiClient } from './client';

// ── MetricsSummary ─────────────────────────────────────────

export const MetricsSummarySchema = z.object({
  total_agents: z.number().int().nonnegative(),
  active_agents: z.number().int().nonnegative(),
  alerts_24h: z.number().int().nonnegative(),
  critical_alerts_24h: z.number().int().nonnegative(),
});
export type MetricsSummary = z.infer<typeof MetricsSummarySchema>;

export async function fetchMetricsSummary(): Promise<MetricsSummary> {
  const res = await apiClient.get('/dashboard/metrics/summary');
  return MetricsSummarySchema.parse(res.data);
}
