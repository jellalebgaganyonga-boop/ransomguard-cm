/**
 * NOTIFICATIONS — API layer (Sprint 8)
 *
 * Endpoints:
 *   GET /dashboard/notifications/preferences → NotificationPreference
 *   PUT /dashboard/notifications/preferences → NotificationPreference
 */

import { z } from 'zod';
import { apiClient } from './client';

export const NotificationPreferenceSchema = z.object({
  id: z.string(),
  email_critical: z.boolean(),
  email_high: z.boolean(),
  email_medium: z.boolean(),
  email_low: z.boolean(),
});
export type NotificationPreference = z.infer<typeof NotificationPreferenceSchema>;

export interface UpdateNotificationPreferencePayload {
  email_critical: boolean;
  email_high: boolean;
  email_medium: boolean;
  email_low: boolean;
}

export async function fetchNotificationPreferences(): Promise<NotificationPreference> {
  const res = await apiClient.get('/dashboard/notifications/preferences');
  return NotificationPreferenceSchema.parse(res.data);
}

export async function updateNotificationPreferences(
  payload: UpdateNotificationPreferencePayload,
): Promise<NotificationPreference> {
  const res = await apiClient.put('/dashboard/notifications/preferences', payload);
  return NotificationPreferenceSchema.parse(res.data);
}
