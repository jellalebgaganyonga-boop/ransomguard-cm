// ============================================================
// src/lib/format-date.ts
// Date formatting utilities — i18n aware (FR/EN)
// Used by: AlertRow, AgentRow, AlertDetail, AuditLog
// ============================================================

import i18n from '@/i18n';

/**
 * Parse a datetime string from the backend as UTC.
 * FastAPI returns naive datetimes (no timezone suffix). Without a "Z",
 * JavaScript's Date constructor treats them as LOCAL time — causing
 * wrong age calculations for users in non-UTC timezones (e.g. Africa/Douala UTC+1).
 * We append "Z" if no timezone indicator is present.
 */
function parseUtc(isoString: string): Date {
  const normalized =
    isoString.endsWith('Z') || /[+-]\d{2}:\d{2}$/.test(isoString)
      ? isoString
      : isoString + 'Z';
  return new Date(normalized);
}

/**
 * Format ISO datetime as relative time ("il y a 14 min" / "14 minutes ago").
 * Falls back to absolute date if timestamp is invalid.
 */
export function formatDistanceToNow(isoString: string): string {
  const date = parseUtc(isoString);
  if (isNaN(date.getTime())) return '—';

  const diffMs = Date.now() - date.getTime();
  const diffSec = Math.floor(diffMs / 1000);
  const diffMin = Math.floor(diffSec / 60);
  const diffHour = Math.floor(diffMin / 60);
  const diffDay = Math.floor(diffHour / 24);

  const lang = i18n.language?.startsWith('en') ? 'en' : 'fr';

  if (lang === 'fr') {
    if (diffSec < 60) return `il y a ${diffSec}s`;
    if (diffMin < 60) return `il y a ${diffMin} min`;
    if (diffHour < 24) return `il y a ${diffHour} h`;
    return `il y a ${diffDay} j`;
  } else {
    if (diffSec < 60) return `${diffSec}s ago`;
    if (diffMin < 60) return `${diffMin} min ago`;
    if (diffHour < 24) return `${diffHour}h ago`;
    return `${diffDay}d ago`;
  }
}

/**
 * Format ISO datetime as short locale date+time string.
 * "08/06/2026 06:42" (FR) or "06/08/2026 06:42 AM" (EN)
 */
export function formatDateTime(isoString: string): string {
  const date = parseUtc(isoString);
  if (isNaN(date.getTime())) return '—';

  const lang = i18n.language?.startsWith('en') ? 'en-US' : 'fr-FR';
  return new Intl.DateTimeFormat(lang, {
    dateStyle: 'short',
    timeStyle: 'short',
    timeZone: 'Africa/Douala',
  }).format(date);
}

/**
 * Format ISO date only (for audit log dates, compliance dates).
 */
export function formatDate(isoString: string): string {
  const date = parseUtc(isoString);
  if (isNaN(date.getTime())) return '—';

  const lang = i18n.language?.startsWith('en') ? 'en-US' : 'fr-FR';
  return new Intl.DateTimeFormat(lang, {
    dateStyle: 'medium',
    timeZone: 'Africa/Douala',
  }).format(date);
}
