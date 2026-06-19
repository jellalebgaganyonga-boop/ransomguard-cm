/**
 * EPIC-ALERTS — API layer
 *
 * Endpoints (PRD v2 §8 "Endpoints consumed — real, from audit"):
 *   GET  /api/v1/dashboard/alerts                   — list with filters
 *   GET  /api/v1/dashboard/alerts/{alert_id}         — detail
 *   POST /api/v1/dashboard/alerts/{alert_id}/status  — status transition
 *
 * Status enum resolved per AC3.4.1: "False positive" is a RESOLUTION
 * CATEGORY sent inside the closing note, not a distinct backend status.
 * Backend status values used by the frontend: new, acknowledged, closed.
 * (PRD §8 technical note flags `false_positive` as a 4th enum value to
 * verify against the backend OpenAPI — if the real backend exposes it
 * as a distinct status, this is the single file to update.)
 */

import { z } from 'zod';
import { apiClient } from './client';

// ── Enums ───────────────────────────────────────────────────

export const AlertSeveritySchema = z.enum(['critical', 'high', 'medium', 'low', 'info']);
export type AlertSeverity = z.infer<typeof AlertSeveritySchema>;

export const AlertStatusSchema = z.enum(['new', 'acknowledged', 'closed']);
export type AlertStatus = z.infer<typeof AlertStatusSchema>;

export const ResolutionCategorySchema = z.enum([
  'false_positive',
  'true_positive_contained',
  'true_positive_escalated',
  'inconclusive',
]);
export type ResolutionCategory = z.infer<typeof ResolutionCategorySchema>;

export const DetectionModuleSchema = z.enum([
  'SENTINEL',
  'ENTROPY',
  'GENEALOGY',
  'USB_GUARD',
  'EXFIL_WATCH',
  'IRONCLAD',
]);
export type DetectionModule = z.infer<typeof DetectionModuleSchema>;

// ── List item (AC3.1.1 columns) ────────────────────────────

export const AlertListItemSchema = z.object({
  id: z.string(),
  created_at: z.string().datetime(),
  severity: AlertSeveritySchema,
  status: AlertStatusSchema,
  priority_score: z.number().int().min(0).max(100),
  agent_id: z.string(),
  agent_hostname: z.string(),
  module: z.string(), // kept loose (z.string) — backend may add modules beyond DetectionModuleSchema
  summary: z.string(),
});
export type AlertListItem = z.infer<typeof AlertListItemSchema>;

export const AlertListResponseSchema = z.object({
  items: z.array(AlertListItemSchema),
  total: z.number().int().nonnegative(),
  page: z.number().int().positive(),
  page_size: z.number().int().positive(),
});
export type AlertListResponse = z.infer<typeof AlertListResponseSchema>;

// ── List params (AC3.1.2 filters, URL-shareable) ───────────

export interface AlertListParams {
  page?: number;
  page_size?: number;
  date_from?: string; // ISO date
  date_to?: string;
  severity?: AlertSeverity;
  status?: AlertStatus;
  agent?: string; // hostname text search
  module?: string;
  sort_by?: 'created_at' | 'severity' | 'status';
  sort_dir?: 'asc' | 'desc';
}

export async function fetchAlertList(params: AlertListParams = {}): Promise<AlertListResponse> {
  const res = await apiClient.get('/dashboard/alerts', { params });
  return AlertListResponseSchema.parse(res.data);
}

// ── Detail (AC3.2.1-4) ─────────────────────────────────────

export const AlertArtifactSchema = z.object({
  type: z.string(), // "file_path" | "process_name" | "network_destination" — kept loose, backend-defined
  value: z.string(),
});
export type AlertArtifact = z.infer<typeof AlertArtifactSchema>;

export const AlertStatusChangeSchema = z.object({
  from_status: AlertStatusSchema.nullable(), // null for the initial creation entry
  to_status: AlertStatusSchema,
  changed_at: z.string().datetime(),
  actor_user_id: z.string().nullable(), // null for system-generated (e.g., initial "new")
  actor_name: z.string().nullable(),
  note: z.string().nullable(),
});
export type AlertStatusChange = z.infer<typeof AlertStatusChangeSchema>;

export const AlertDetailSchema = z.object({
  id: z.string(),
  created_at: z.string().datetime(),
  severity: AlertSeveritySchema,
  status: AlertStatusSchema,
  priority_score: z.number().int().min(0).max(100),
  confidence_score: z.number().min(0).max(100).nullable(),
  agent_id: z.string(),
  agent_hostname: z.string(),
  module: z.string(),
  summary: z.string(),
  raw_payload: z.record(z.string(), z.unknown()).nullable(),
  artifacts: z.array(AlertArtifactSchema),
  status_history: z.array(AlertStatusChangeSchema),
  tenant_id: z.string(),
});
export type AlertDetail = z.infer<typeof AlertDetailSchema>;

export async function fetchAlertDetail(alertId: string): Promise<AlertDetail> {
  const res = await apiClient.get(`/dashboard/alerts/${alertId}`);
  return AlertDetailSchema.parse(res.data);
}

// ── Status transition (AC3.3.1, AC3.4.1) ───────────────────

export interface UpdateAlertStatusPayload {
  status: 'acknowledged' | 'closed';
  note?: string;
}

export async function updateAlertStatus(
  alertId: string,
  payload: UpdateAlertStatusPayload
): Promise<AlertDetail> {
  const res = await apiClient.post(`/dashboard/alerts/${alertId}/status`, payload);
  return AlertDetailSchema.parse(res.data);
}

/**
 * AC3.4.1: closing note format is "<category>: <notes>".
 * Centralized here so the format is defined exactly once.
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
