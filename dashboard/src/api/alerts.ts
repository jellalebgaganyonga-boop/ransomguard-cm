/**
 * EPIC-ALERTS — API layer
 *
 * Schemas match real backend OpenAPI (verified via smoke test, 2026-06-19).
 * Key differences from PRD v2 draft assumptions:
 *   - AlertStatus: PascalCase enum (New/Investigating/Resolved/FalsePositive/Suppressed)
 *   - AlertSeverity: PascalCase, no "Info" variant
 *   - Pagination: offset/limit, not page/page_size
 *   - Status update: { new_status, justification(min=5) }, not { status, note }
 *   - AlertItem: no agent_hostname, no priority_score, no module; has alert_type/detected_at/ingested_at
 *   - AlertDetail: confidence_score, artifacts, status_history are optional (not yet in backend)
 */

import { z } from 'zod';
import { apiClient } from './client';

// ── Enums ───────────────────────────────────────────────────

/** PascalCase — matches backend enum exactly (case-sensitive). */
export const AlertSeveritySchema = z.enum(['Low', 'Medium', 'High', 'Critical']);
export type AlertSeverity = z.infer<typeof AlertSeveritySchema>;

/** PascalCase — matches backend AlertStatus enum exactly. */
export const AlertStatusSchema = z.enum([
  'New',
  'Investigating',
  'Resolved',
  'FalsePositive',
  'Suppressed',
]);
export type AlertStatus = z.infer<typeof AlertStatusSchema>;

/** Frontend-only concept: resolution category structures the justification text in the close flow. */
export const ResolutionCategorySchema = z.enum([
  'false_positive',
  'true_positive_contained',
  'true_positive_escalated',
  'inconclusive',
]);
export type ResolutionCategory = z.infer<typeof ResolutionCategorySchema>;

// ── List item (AC3.1.1 columns) ────────────────────────────

export const AlertListItemSchema = z.object({
  id: z.string(),
  detected_at: z.string().datetime({ offset: true }),
  ingested_at: z.string().datetime({ offset: true }),
  severity: AlertSeveritySchema,
  status: AlertStatusSchema,
  agent_id: z.string(),
  alert_type: z.string(),
  mitre_technique_id: z.string().nullable(),
  summary: z.string(),
});
export type AlertListItem = z.infer<typeof AlertListItemSchema>;

export const AlertListResponseSchema = z.object({
  items: z.array(AlertListItemSchema),
  total: z.number().int().nonnegative(),
  offset: z.number().int().nonnegative(),
  limit: z.number().int().positive(),
});
export type AlertListResponse = z.infer<typeof AlertListResponseSchema>;

// ── List params (AC3.1.2 filters, URL-shareable) ───────────

export interface AlertListParams {
  offset?: number;
  limit?: number;
  date_from?: string; // ISO date
  date_to?: string;
  severity?: AlertSeverity;
  status?: AlertStatus;
  agent?: string; // agent_id or hostname text search
  sort_by?: 'detected_at' | 'severity' | 'status';
  sort_dir?: 'asc' | 'desc';
}

export async function fetchAlertList(params: AlertListParams = {}): Promise<AlertListResponse> {
  // Map frontend param names to actual backend param names.
  // Backend: detected_after/detected_before (not date_from/date_to)
  // Backend: agent_id (not agent)
  // Backend does NOT support sort_by/sort_dir — always sorts detected_at desc.
  const { date_from, date_to, agent, sort_by: _sb, sort_dir: _sd, ...rest } = params;
  const backendParams: Record<string, unknown> = { ...rest };
  if (date_from)  backendParams['detected_after']  = date_from;
  if (date_to)    backendParams['detected_before'] = date_to;
  if (agent)      backendParams['agent_id']        = agent;

  const res = await apiClient.get('/dashboard/alerts', { params: backendParams });
  return AlertListResponseSchema.parse(res.data);
}

// ── Detail (AC3.2.1-4) ─────────────────────────────────────
// Fields not yet implemented in backend are optional so the schema
// validates real responses without hard-crashing.

export const AlertArtifactSchema = z.object({
  type: z.string(),
  value: z.string(),
});
export type AlertArtifact = z.infer<typeof AlertArtifactSchema>;

export const AlertStatusChangeSchema = z.object({
  from_status: AlertStatusSchema.nullable(),
  to_status: AlertStatusSchema,
  changed_at: z.string().datetime({ offset: true }),
  actor_user_id: z.string().nullable(),
  actor_name: z.string().nullable(),
  note: z.string().nullable(),
});
export type AlertStatusChange = z.infer<typeof AlertStatusChangeSchema>;

export const AlertDetailSchema = z.object({
  id: z.string(),
  detected_at: z.string().datetime({ offset: true }),
  ingested_at: z.string().datetime({ offset: true }),
  severity: AlertSeveritySchema,
  status: AlertStatusSchema,
  agent_id: z.string(),
  alert_type: z.string(),
  mitre_technique_id: z.string().nullable(),
  summary: z.string(),
  // Optional fields — not yet exposed by backend:
  confidence_score: z.number().min(0).max(100).nullable().optional(),
  raw_payload: z.record(z.string(), z.unknown()).nullable().optional(),
  artifacts: z.array(AlertArtifactSchema).optional(),
  status_history: z.array(AlertStatusChangeSchema).optional(),
});
export type AlertDetail = z.infer<typeof AlertDetailSchema>;

export async function fetchAlertDetail(alertId: string): Promise<AlertDetail> {
  const res = await apiClient.get(`/dashboard/alerts/${alertId}`);
  return AlertDetailSchema.parse(res.data);
}

// ── Status transition (AC3.3.1, AC3.4.1) ───────────────────

export interface UpdateAlertStatusPayload {
  new_status: AlertStatus;
  /** Minimum 5 characters (backend enforces). */
  justification: string;
}

export async function updateAlertStatus(
  alertId: string,
  payload: UpdateAlertStatusPayload
): Promise<AlertDetail> {
  const res = await apiClient.post(`/dashboard/alerts/${alertId}/status`, payload);
  return AlertDetailSchema.parse(res.data);
}

/**
 * AC3.4.1: formats the justification string sent to the backend on close.
 * Format: "<category>: <notes>"
 */
export function formatClosingNote(category: ResolutionCategory, notes: string): string {
  const categoryLabels: Record<ResolutionCategory, string> = {
    false_positive: 'Faux positif',
    true_positive_contained: 'Vrai positif — Contenu',
    true_positive_escalated: 'Vrai positif — Escaladé',
    inconclusive: 'Non concluant',
  };
  return `${categoryLabels[category]}: ${notes}`;
}
