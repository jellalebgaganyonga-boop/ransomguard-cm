/**
 * Toasts -- the console's feedback for an action: what was asked, whether it happened. The
 * inventory (INVENTAIRE_CONSOLE.md, US3.3) noted that a failed acknowledge or close showed
 * nothing; now every mutation says so, success or failure, in a corner that screen readers get
 * through aria-live. Nothing here claims an effect the server did not confirm: callers push a
 * toast from the mutation's result. The hook lives in lib/toast-context.ts.
 */

import { useCallback, useMemo, useRef, useState, type ReactNode } from 'react';
import { CheckCircle2, AlertTriangle, Info, X } from 'lucide-react';
import { cn } from '@/lib/utils';
import { ToastContext, type Toast, type ToastApi, type ToastKind } from '@/lib/toast-context';

/** How long a toast stays; an error stays longer, it has to be read. */
const DURATION_MS: Record<ToastKind, number> = { success: 5_000, info: 6_000, error: 10_000 };

export function ToastProvider({ children }: { children: ReactNode }) {
  const [toasts, setToasts] = useState<Toast[]>([]);
  const nextId = useRef(1);

  const dismiss = useCallback((id: number) => setToasts((all) => all.filter((t) => t.id !== id)), []);

  const push = useCallback(
    (kind: ToastKind, message: string) => {
      const id = nextId.current++;
      setToasts((all) => [...all.slice(-4), { id, kind, message }]); // five at most
      setTimeout(() => dismiss(id), DURATION_MS[kind]);
    },
    [dismiss],
  );

  const api = useMemo<ToastApi>(
    () => ({
      push,
      success: (m) => push('success', m),
      error: (m) => push('error', m),
      info: (m) => push('info', m),
    }),
    [push],
  );

  return (
    <ToastContext.Provider value={api}>
      {children}
      <div
        aria-live="polite"
        aria-atomic="false"
        className="pointer-events-none fixed inset-x-4 bottom-4 z-[1000] flex flex-col items-end gap-2 sm:inset-x-auto sm:right-4"
      >
        {toasts.map((toast) => (
          <ToastItem key={toast.id} toast={toast} onDismiss={() => dismiss(toast.id)} />
        ))}
      </div>
    </ToastContext.Provider>
  );
}

function ToastItem({ toast, onDismiss }: { toast: Toast; onDismiss: () => void }) {
  const Icon = toast.kind === 'success' ? CheckCircle2 : toast.kind === 'error' ? AlertTriangle : Info;
  return (
    <div
      role="status"
      data-testid="toast"
      data-kind={toast.kind}
      className={cn(
        'pointer-events-auto flex w-full max-w-sm items-start gap-2 rounded-lg border px-3 py-2 text-sm shadow-lg',
        toast.kind === 'success' && 'border-success/40 bg-canvas text-text-primary',
        toast.kind === 'error' && 'border-error/50 bg-canvas text-text-primary',
        toast.kind === 'info' && 'border-border-default bg-canvas text-text-primary',
      )}
    >
      <Icon
        className={cn(
          'mt-0.5 size-4 shrink-0',
          toast.kind === 'success' && 'text-success',
          toast.kind === 'error' && 'text-error',
          toast.kind === 'info' && 'text-text-secondary',
        )}
        aria-hidden="true"
      />
      <span className="flex-1">{toast.message}</span>
      <button
        type="button"
        onClick={onDismiss}
        aria-label="Fermer"
        className="shrink-0 rounded text-text-secondary hover:text-text-primary"
      >
        <X className="size-4" aria-hidden="true" />
      </button>
    </div>
  );
}
