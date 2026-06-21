/**
 * AlertDetailPage — US3.2
 *
 * AC3.2.1: header — ID (copyable), severity, status, age, agent link, module
 * AC3.2.2: detection narrative — summary, confidence, raw payload (collapsible JSON)
 * AC3.2.3: affected artifacts list
 * AC3.2.4: status change timeline
 * AC3.2.5: role-gated action buttons (delegated to lib/alert-permissions.ts)
 * AC3.2.6: 404 for cross-tenant access
 *
 * Visual contract: inverted-pyramid narrative validated in Design Phase
 * Hi-Fi Mockup 2 (Day 9-10) — "Que s'est-il passé?" → "Pourquoi important?"
 * → "Actions recommandées" → collapsible technical details.
 */

import { useState } from 'react';
import { useParams, useNavigate, Link } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { Copy, ChevronLeft, ChevronDown, ChevronUp, FileQuestion } from 'lucide-react';
import { SeverityBadge } from '@/components/ui/severity-badge';
import { Button } from '@/components/ui/button';
import { useAlertDetail } from '@/hooks/use-alerts';
import { useMe, resolvePrimaryRole } from '@/hooks/use-me';
import { getAlertActionPermissions } from '@/lib/alert-permissions';
import { formatDistanceToNow, formatDateTime } from '@/lib/format-date';
import { AcknowledgeModal } from '@/components/alerts/acknowledge-modal';
import { CloseModal } from '@/components/alerts/close-modal';

// ── Not found (AC3.2.6) ─────────────────────────────────────

function AlertNotFound() {
  const { t } = useTranslation();
  const navigate = useNavigate();
  return (
    <div className="flex flex-col items-center justify-center gap-3 py-20 text-center">
      <FileQuestion className="size-10 text-text-tertiary" aria-hidden="true" />
      <p className="font-semibold text-text-primary">
        {t('alerts.notFound', 'Alerte non trouvée')}
      </p>
      <Button variant="secondary" onClick={() => navigate('/alerts')}>
        {t('alerts.backToList', 'Retour aux alertes')}
      </Button>
    </div>
  );
}

// ── Page ──────────────────────────────────────────────────

export function AlertDetailPage() {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const { id } = useParams<{ id: string }>();
  const { data: me } = useMe();
  const { data: alert, isLoading, isError, error } = useAlertDetail(id);

  const [showRaw, setShowRaw] = useState(false);
  const [ackModalOpen, setAckModalOpen] = useState(false);
  const [closeModalOpen, setCloseModalOpen] = useState(false);
  const [closePreselect, setClosePreselect] = useState<'false_positive' | undefined>(undefined);
  const [copied, setCopied] = useState(false);

  const role = me ? resolvePrimaryRole(me.roles) : 'read_only_auditor';

  // AC3.2.6: 404 → friendly message, no enumeration hint
  const is404 =
    isError && (error as { response?: { status?: number } })?.response?.status === 404;

  if (isLoading) {
    return <div className="py-20 text-center text-sm text-text-tertiary">{t('common.loading')}</div>;
  }

  if (is404 || !alert) {
    return <AlertNotFound />;
  }

  const perms = getAlertActionPermissions(role, alert.status);

  const handleCopyId = () => {
    void navigator.clipboard.writeText(alert.id);
    setCopied(true);
    setTimeout(() => setCopied(false), 2000);
  };

  return (
    <div>
      {/* Breadcrumb */}
      <button
        className="mb-4 flex items-center gap-1.5 text-sm text-primary hover:underline"
        onClick={() => navigate('/alerts')}
      >
        <ChevronLeft className="size-3.5" /> {t('alerts.backToList', 'Retour aux alertes')}
      </button>

      {/* AC3.2.1: Header */}
      <div className="mb-1 flex items-center gap-1.5 font-mono text-xs text-text-tertiary">
        <span>{t('alerts.id', 'Alerte')} #{alert.id}</span>
        <button onClick={handleCopyId} aria-label={t('alerts.copyId', "Copier l'ID")}>
          <Copy className="size-3" />
        </button>
        {copied && <span className="text-success">{t('common.copied', 'Copié !')}</span>}
      </div>

      <div className="mb-4 flex flex-wrap items-center gap-3">
        <SeverityBadge severity={alert.severity.toLowerCase() as 'critical' | 'high' | 'medium' | 'low'} />
        <span className="rounded bg-surface-subtle px-2 py-0.5 text-xs font-semibold uppercase text-text-secondary">
          {t(`status.${alert.status.toLowerCase()}`, alert.status)}
        </span>
        <span className="ml-auto text-xs text-text-tertiary">
          {t('alerts.detectedAt', 'Détecté')} {formatDistanceToNow(alert.detected_at)}
        </span>
      </div>

      <div className="mb-6 grid grid-cols-2 gap-4">
        <div>
          <span className="text-[10px] font-semibold uppercase text-text-tertiary">
            {t('alerts.agent', 'Agent')}
          </span>
          <p className="mt-0.5">
            <Link
              to={`/agents/${alert.agent_id}`}
              className="font-mono font-semibold text-primary hover:underline"
            >
              {alert.agent_id}
            </Link>
          </p>
        </div>
        <div>
          <span className="text-[10px] font-semibold uppercase text-text-tertiary">
            {t('alerts.module', 'Type')}
          </span>
          <p className="mt-0.5 font-semibold text-text-primary">{alert.alert_type}</p>
        </div>
      </div>

      {/* AC3.2.5: role-gated action buttons */}
      {(perms.canAcknowledge || perms.canClose) && (
        <div className="mb-6 rounded-lg border border-severity-high-border bg-severity-high-subtle p-4">
          <div className="flex gap-2">
            {perms.canAcknowledge && (
              <Button variant="primary" onClick={() => setAckModalOpen(true)}>
                {t('alerts.acknowledge', 'Prendre en charge')}
              </Button>
            )}
            {perms.canClose && (
              <>
                {/*
                  AC3.2.5 lists [Close] and [Mark as false positive] as two
                  separate buttons. Per AC3.4.1's resolved status model,
                  "false positive" is a RESOLUTION CATEGORY within the
                  close flow, not a distinct backend status — so both
                  buttons open the same CloseModal; "Mark as false
                  positive" pre-selects that category for a faster path.
                */}
                <Button
                  variant="primary"
                  onClick={() => {
                    setClosePreselect(undefined);
                    setCloseModalOpen(true);
                  }}
                >
                  {t('alerts.close', "Fermer l'incident")}
                </Button>
                <Button
                  variant="secondary"
                  onClick={() => {
                    setClosePreselect('false_positive');
                    setCloseModalOpen(true);
                  }}
                >
                  {t('alerts.markFalsePositive', 'Marquer faux positif')}
                </Button>
              </>
            )}
          </div>
        </div>
      )}

      {/* AC3.3.3 / role read_only_auditor — explicit notice */}
      {!perms.canAcknowledge && !perms.canClose && alert.status !== 'Resolved' && alert.status !== 'FalsePositive' && alert.status !== 'Suppressed' && (
        <div className="mb-6 flex items-center gap-2 rounded-lg bg-primary-subtle p-3 text-sm text-text-secondary">
          {t('alerts.readOnlyNotice', 'Mode lecture seule — aucune action disponible')}
        </div>
      )}

      {/* AC3.2.2: Inverted-pyramid narrative */}
      <div className="rounded-lg border border-border-subtle bg-elevated p-6 shadow-sm">
        <h2 className="mb-3 text-sm font-semibold uppercase tracking-wide text-text-primary">
          {t('alerts.whatHappened', "QUE S'EST-IL PASSÉ ?")}
        </h2>
        <p className="mb-4 max-w-2xl leading-relaxed text-text-primary">{alert.summary}</p>

        {alert.confidence_score != null && (
          <div className="mb-6 flex items-center gap-3">
            <span className="text-xs text-text-tertiary">
              {t('alerts.confidence', 'Confiance de détection')}: {alert.confidence_score}%
            </span>
            <div className="h-1.5 w-40 overflow-hidden rounded-full bg-surface-muted">
              <div
                className="h-full rounded-full bg-success"
                style={{ width: `${alert.confidence_score}%` }}
              />
            </div>
          </div>
        )}

        {/* AC3.2.3: Artifacts */}
        {(alert.artifacts?.length ?? 0) > 0 && (
          <div className="mb-6 border-t border-border-subtle pt-5">
            <h3 className="mb-2 text-xs font-semibold uppercase tracking-wide text-text-secondary">
              {t('alerts.artifacts', 'Artefacts associés')}
            </h3>
            <ul className="space-y-1">
              {(alert.artifacts ?? []).map((a, i) => (
                <li key={i} className="font-mono text-xs text-text-secondary">
                  <span className="text-text-tertiary">{a.type}:</span> {a.value}
                </li>
              ))}
            </ul>
          </div>
        )}

        {/* AC3.2.2: Raw payload — collapsible */}
        {alert.raw_payload && (
          <div className="border-t border-border-subtle pt-5">
            <button
              className="flex items-center gap-1 text-xs font-semibold uppercase tracking-wide text-text-secondary"
              onClick={() => setShowRaw((v) => !v)}
            >
              {showRaw ? <ChevronUp className="size-3" /> : <ChevronDown className="size-3" />}
              {t('alerts.technicalDetails', 'Détails techniques')}
            </button>
            {showRaw && (
              <pre className="mt-2 overflow-x-auto rounded bg-surface-inverse p-3 text-xs text-text-inverse">
                {JSON.stringify(alert.raw_payload, null, 2)}
              </pre>
            )}
          </div>
        )}
      </div>

      {/* AC3.2.4: Status change timeline (optional — not yet in backend) */}
      {(alert.status_history?.length ?? 0) > 0 && (
      <div className="mt-6 rounded-lg border border-border-subtle bg-elevated p-6 shadow-sm">
        <h2 className="mb-4 text-sm font-semibold uppercase tracking-wide text-text-primary">
          {t('alerts.timeline', 'Chronologie')}
        </h2>
        <ol className="space-y-4">
          {(alert.status_history ?? []).map((change, i) => (
            <li key={i} className="flex gap-3 text-sm">
              <span className="mt-0.5 size-2 shrink-0 rounded-full bg-primary" />
              <div>
                <p className="text-text-primary">
                  {change.from_status
                    ? t('alerts.statusChange', {
                        from: t(`status.${change.from_status.toLowerCase()}`, change.from_status),
                        to: t(`status.${change.to_status.toLowerCase()}`, change.to_status),
                        defaultValue: `${change.from_status} → ${change.to_status}`,
                      })
                    : t('alerts.created', 'Alerte créée')}
                  {change.actor_name && (
                    <span className="text-text-tertiary"> — {change.actor_name}</span>
                  )}
                </p>
                {change.note && (
                  <p className="mt-1 text-xs italic text-text-secondary">"{change.note}"</p>
                )}
                <p className="mt-0.5 text-xs text-text-tertiary">
                  {formatDateTime(change.changed_at)}
                </p>
              </div>
            </li>
          ))}
        </ol>
      </div>
      )}

      {/* Modals */}
      <AcknowledgeModal
        alertId={alert.id}
        open={ackModalOpen}
        onOpenChange={setAckModalOpen}
      />
      <CloseModal
        alertId={alert.id}
        open={closeModalOpen}
        onOpenChange={setCloseModalOpen}
        {...(closePreselect !== undefined && { preselectCategory: closePreselect })}
      />
    </div>
  );
}
