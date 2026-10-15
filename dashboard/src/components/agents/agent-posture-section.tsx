/**
 * The agent page's posture (R8, ex-7ad1fc2): the three questions of LOT2_HEARTBEAT_DEPUIS_ECRAN.md.
 *   1. Is this endpoint protected, now?        -> the verdict banner, one sentence
 *   2. If not, why, and what should I do?      -> the finding, then the module table
 *   3. Did everything it detected reach us?    -> transmission
 *
 * Everything decided here was decided by the GRID (protection_state, reason_codes, ages measured
 * on the server clock); this component translates and colours. A null measure reads
 * "indisponible" / "unavailable", never 0.
 */

import type { ReactNode } from 'react';
import { useTranslation } from 'react-i18next';
import type { AgentDetail } from '@/api/agents';
import { Badge } from '@/components/ui/badge';
import { PROTECTION_BANNER_CLASS, protectionVariant } from '@/lib/agent-status';
import {
  formatBytes,
  formatCount,
  formatDuration,
  orderedModules,
  otherFindings,
  productName,
  reasonLabel,
  uploadErrorLabel,
  verdictSentence,
} from '@/lib/agent-posture';
import { formatDateTime, formatDistanceToNow } from '@/lib/format-date';

const STATE_VARIANT: Record<string, 'low' | 'high' | 'medium' | 'neutral'> = {
  active: 'low',
  degraded: 'medium',
  inactive: 'high',
  disabled_by_config: 'neutral',
};

function Field({ label, children, title }: { label: string; children: ReactNode; title?: string }) {
  return (
    <div>
      <dt className="text-[10px] font-semibold uppercase text-text-secondary">{label}</dt>
      <dd className="mt-0.5 text-sm text-text-primary" title={title}>
        {children}
      </dd>
    </div>
  );
}

export function AgentPostureSection({ agent }: { agent: AgentDetail }) {
  const { t } = useTranslation();
  const posture = agent.posture;
  const findings = otherFindings(t, agent);
  const signalLost = agent.reason_codes.includes('signal_lost');

  return (
    <section aria-labelledby="posture-verdict" className="space-y-6">
      {/* 1 — The verdict, one sentence */}
      <div
        className={`rounded-lg border p-5 ${PROTECTION_BANNER_CLASS[agent.protection_state]}`}
        data-testid="posture-banner"
        data-state={agent.protection_state}
      >
        <div className="flex flex-wrap items-center gap-3">
          <Badge variant={protectionVariant(agent.protection_state)}>
            {t(`posture.verdictShort.${agent.protection_state}`)}
          </Badge>
          <h2 id="posture-verdict" className="text-base font-semibold text-text-primary">
            {verdictSentence(t, agent)}
          </h2>
        </div>
        {findings.length > 0 && (
          <div className="mt-3">
            <p className="text-xs font-semibold uppercase text-text-secondary">
              {t('posture.section.otherFindings')}
            </p>
            <ul className="mt-1 list-disc space-y-0.5 pl-5 text-sm text-text-primary">
              {findings.map((finding) => (
                <li key={finding}>{finding}</li>
              ))}
            </ul>
          </div>
        )}
        {posture && signalLost && (
          <p className="mt-3 text-xs text-text-secondary">
            {t('posture.section.lastPosture', { when: formatDateTime(posture.received_at) })}
          </p>
        )}
      </div>

      {!posture ? (
        <p className="rounded-lg border border-border-subtle bg-elevated p-6 text-sm text-text-secondary shadow-sm">
          {t('posture.section.noPosture')}
        </p>
      ) : (
        <>
          {/* 2 — Modules: the six entries, product names, state and reason */}
          <div className="rounded-lg border border-border-subtle bg-elevated shadow-sm">
            <h3
              id="posture-modules-title"
              className="border-b border-border-subtle px-4 py-3 text-sm font-semibold uppercase tracking-wide text-text-primary"
            >
              {t('posture.section.modules')}
            </h3>
            {/* Scrolls horizontally on a narrow screen: a named region the keyboard can reach
                (axe scrollable-region-focusable, WCAG 2.1.1). jsx-a11y does not know this pattern. */}
            {/* eslint-disable-next-line jsx-a11y/no-noninteractive-tabindex */}
            <div className="overflow-x-auto" tabIndex={0} role="region" aria-labelledby="posture-modules-title">
              <table className="w-full text-sm">
                <thead>
                  <tr className="border-b border-border-subtle text-left text-xs font-semibold uppercase tracking-wide text-text-secondary">
                    <th className="px-4 py-2">{t('posture.field.module')}</th>
                    <th className="px-4 py-2">{t('posture.field.state')}</th>
                    <th className="px-4 py-2">{t('posture.field.reason')}</th>
                    <th className="px-4 py-2">{t('posture.field.since')}</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-border-subtle">
                  {orderedModules(posture.modules).map((module) => (
                    <tr key={module.code}>
                      <td className="px-4 py-2 font-semibold text-text-primary">{productName(module.code)}</td>
                      <td className="px-4 py-2">
                        <Badge variant={STATE_VARIANT[module.state] ?? 'neutral'}>
                          {t(`posture.stateLabel.${module.state}`, module.state)}
                        </Badge>
                      </td>
                      <td
                        className="px-4 py-2 text-text-primary"
                        title={module.reason_code ? t('posture.field.rawCode', { code: module.reason_code }) : undefined}
                      >
                        {module.reason_code ? reasonLabel(t, module.reason_code) : '—'}
                      </td>
                      <td className="px-4 py-2 text-xs text-text-secondary">{formatDateTime(module.changed_at)}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </div>

          <div className="grid gap-6 md:grid-cols-2">
            {/* 3 — Transmission: did everything arrive? */}
            <div className="rounded-lg border border-border-subtle bg-elevated p-5 shadow-sm">
              <h3 className="mb-3 text-sm font-semibold uppercase tracking-wide text-text-primary">
                {t('posture.section.transmission')}
              </h3>
              <dl className="grid grid-cols-1 gap-3 sm:grid-cols-2">
                <Field label={t('posture.field.pendingAlerts')}>{formatCount(t, posture.pending_alerts)}</Field>
                <Field label={t('posture.field.lastUpload')}>
                  {posture.last_upload_age_seconds != null
                    ? t('posture.field.ago', { duration: formatDuration(t, posture.last_upload_age_seconds) })
                    : posture.pending_alerts == null
                      ? t('posture.unavailable')
                      : t('posture.noneSinceStart')}
                </Field>
                <Field
                  label={t('posture.field.lastUploadError')}
                  {...(posture.last_upload_error_code
                    ? { title: t('posture.field.rawCode', { code: posture.last_upload_error_code }) }
                    : {})}
                >
                  {posture.last_upload_error_code
                    ? uploadErrorLabel(t, posture.last_upload_error_code)
                    : t('posture.none')}
                </Field>
                <Field label={t('posture.field.rejectedAlerts')}>{formatCount(t, posture.rejected_alerts)}</Field>
                <Field label={t('posture.field.auditWriteFailures')}>{formatCount(t, posture.audit_write_failures)}</Field>
              </dl>
            </div>

            {/* The endpoint */}
            <div className="rounded-lg border border-border-subtle bg-elevated p-5 shadow-sm">
              <h3 className="mb-3 text-sm font-semibold uppercase tracking-wide text-text-primary">
                {t('posture.section.endpoint')}
              </h3>
              <dl className="grid grid-cols-1 gap-3 sm:grid-cols-2">
                <Field label={t('posture.field.agentVersion')}>{agent.agent_version}</Field>
                <Field label={t('posture.field.osVersion')}>{agent.os_version}</Field>
                <Field label={t('posture.field.systemFree')}>{formatBytes(t, posture.system_free_bytes)}</Field>
                <Field label={t('posture.field.dbFree')}>{formatBytes(t, posture.db_volume_free_bytes)}</Field>
                <Field label={t('posture.field.lastDetection')}>
                  {posture.last_detection_at ? formatDistanceToNow(posture.last_detection_at) : t('posture.none')}
                </Field>
                <Field label={t('posture.field.threatIntel')}>
                  {posture.threat_intel_version ?? t('posture.unavailable')}
                </Field>
              </dl>
            </div>
          </div>
        </>
      )}
    </section>
  );
}
