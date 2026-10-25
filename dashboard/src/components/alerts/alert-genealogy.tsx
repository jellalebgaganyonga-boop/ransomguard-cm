/**
 * "What started it" — the chain of processes behind the write (GENEALOGY).
 *
 * The attribution above it names the one process that wrote the file. This says where that
 * process came from: the shell that launched it, and the shell that launched that. On the bench
 * the chain reads explorer -> cmd -> powershell, which is the difference between "powershell
 * wrote to a decoy" and "somebody opened a command prompt and ran a script".
 *
 * The chain arrives after the alert, because it is captured while the writer is still running.
 * An alert whose writer was never attributed has no chain at all, and this says so rather than
 * drawing an empty tree.
 */

import { useTranslation } from 'react-i18next';
import { ChevronRight, ShieldAlert } from 'lucide-react';
import type { AlertGenealogy as Genealogy, GenealogyProcess } from '@/api/alerts';
import { chainOrder } from '@/lib/alert-genealogy';

function ProcessRow({ process, isWriter }: { process: GenealogyProcess; isWriter: boolean }) {
  const { t } = useTranslation();
  return (
    <li className="flex flex-col gap-0.5">
      <div className="flex items-center gap-2">
        <span
          className={
            isWriter
              ? 'font-semibold text-text-primary'
              : 'text-text-secondary'
          }
        >
          {process.name}
        </span>
        <span className="font-mono text-xs text-text-muted">PID {process.pid}</span>
        {isWriter && (
          <span className="rounded bg-[color:var(--ref-palette-orange-30)]/15 px-1.5 py-0.5 text-xs font-medium text-[color:var(--ref-palette-orange-30)]">
            {t('alerts.genealogy.writer', 'wrote the file')}
          </span>
        )}
      </div>
      {process.path && (
        <span className="break-all font-mono text-xs text-text-muted">{process.path}</span>
      )}
    </li>
  );
}

export function AlertGenealogy({ genealogy }: { genealogy: Genealogy | null | undefined }) {
  const { t } = useTranslation();

  if (!genealogy) {
    return (
      <section aria-labelledby="genealogy-heading" className="mt-6">
        <h2 id="genealogy-heading" className="mb-2 text-sm font-semibold uppercase tracking-wide text-text-secondary">
          {t('alerts.genealogy.title', 'What started it')}
        </h2>
        <p className="text-sm text-text-secondary" data-testid="genealogy-absent">
          {t(
            'alerts.genealogy.absent',
            'No process chain for this alert. The chain is built from the process that wrote the file, so there is none when no process could be named.',
          )}
        </p>
      </section>
    );
  }

  const order = chainOrder(genealogy);

  return (
    <section aria-labelledby="genealogy-heading" className="mt-6">
      <h2 id="genealogy-heading" className="mb-2 text-sm font-semibold uppercase tracking-wide text-text-secondary">
        {t('alerts.genealogy.title', 'What started it')}
      </h2>

      {/* The one-line reading, for someone scanning the page. */}
      <p className="mb-3 flex flex-wrap items-center gap-1 text-sm" data-testid="genealogy-chain">
        {order.map((process, index) => (
          <span key={`${process.pid}-${index}`} className="flex items-center gap-1">
            {index > 0 && <ChevronRight className="size-3 text-text-muted" aria-hidden="true" />}
            <span className={index === order.length - 1 ? 'font-semibold text-text-primary' : 'text-text-secondary'}>
              {process.name}
            </span>
          </span>
        ))}
      </p>

      <ol className="space-y-2 border-l border-border-subtle pl-4" data-testid="genealogy-tree">
        {order.map((process, index) => (
          <ProcessRow
            key={`${process.pid}-${index}`}
            process={process}
            isWriter={index === order.length - 1}
          />
        ))}
      </ol>

      {genealogy.patterns.length > 0 && (
        <div className="mt-3 space-y-2" data-testid="genealogy-patterns">
          {genealogy.patterns.map((pattern) => (
            <div
              key={pattern.technique_id}
              className="flex items-start gap-2 rounded-md border border-border-subtle p-2"
            >
              <ShieldAlert className="mt-0.5 size-4 shrink-0 text-[color:var(--ref-palette-orange-30)]" aria-hidden="true" />
              <div>
                <p className="text-sm font-medium text-text-primary">
                  <span className="font-mono">{pattern.technique_id}</span> {pattern.technique_name}
                </p>
                <p className="text-xs text-text-secondary">{pattern.description}</p>
              </div>
            </div>
          ))}
        </div>
      )}
    </section>
  );
}
