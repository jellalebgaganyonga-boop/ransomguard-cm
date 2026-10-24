/**
 * The attribution of a SENTINEL alert (ADR-025, section 3), as the console shows it. The alert
 * reaches the GRID at once; who wrote the canary arrives afterwards, as a complement. Until then
 * the attribution is pending; after PENDING_TIMEOUT_MS without it, the console says it was not
 * received rather than "in progress" forever.
 */

import type { AlertAttribution } from '@/api/alerts';

/** Ten minutes: past this, a pending attribution is shown as not received. */
export const PENDING_TIMEOUT_MS = 10 * 60 * 1000;

export type AttributionView =
  | { kind: 'pending' }
  | { kind: 'not_received' }
  | { kind: 'resolved'; processName: string | null; processId: number | null; createdAt: string | null; imagePath: string | null }
  | { kind: 'unavailable'; reason: string | null }
  | { kind: 'ambiguous' };

/** What to show for an alert's attribution; null when the alert carries none (not SENTINEL, or older). */
export function attributionView(
  attribution: AlertAttribution | null | undefined,
  detectedAt: string,
  now: number = Date.now(),
): AttributionView | null {
  if (!attribution) return null;
  switch (attribution.state) {
    case 'pending': {
      const detected = Date.parse(detectedAt.endsWith('Z') || /[+-]\d{2}:\d{2}$/.test(detectedAt) ? detectedAt : `${detectedAt}Z`);
      return Number.isNaN(detected) || now - detected < PENDING_TIMEOUT_MS ? { kind: 'pending' } : { kind: 'not_received' };
    }
    case 'resolved':
      return {
        kind: 'resolved',
        processName: attribution.process_name ?? null,
        processId: attribution.process_id ?? null,
        createdAt: attribution.process_created_at ?? null,
        imagePath: attribution.process_path ?? null,
      };
    case 'ambiguous':
      return { kind: 'ambiguous' };
    default:
      // unavailable, or a state this console does not know: nobody is named.
      return { kind: 'unavailable', reason: attribution.reason ?? null };
  }
}
