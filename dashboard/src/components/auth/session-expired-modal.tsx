// ============================================================
// src/components/auth/session-expired-modal.tsx
// US1.2 AC1.2.3 — "Votre session a expiré" modal
//
// Shown when idle timer fires. Auto-redirects to /auth/login
// after 5 seconds or immediately on button click.
// ============================================================

import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Clock } from 'lucide-react';
import { Button } from '@/components/ui/button';

interface SessionExpiredModalProps {
  onDismiss: () => void; // triggers logout + redirect
}

export function SessionExpiredModal({ onDismiss }: SessionExpiredModalProps) {
  const { t } = useTranslation();
  const [countdown, setCountdown] = useState(5);

  // Auto-dismiss after 5 seconds
  useEffect(() => {
    const tick = setInterval(() => {
      setCountdown((c) => {
        if (c <= 1) {
          clearInterval(tick);
          onDismiss();
          return 0;
        }
        return c - 1;
      });
    }, 1000);
    return () => clearInterval(tick);
  }, [onDismiss]);

  return (
    // Scrim
    <div
      style={{
        position: 'fixed',
        inset: 0,
        zIndex: 'var(--ref-zindex-modal)',
        background: 'var(--sys-color-background-scrim)',
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'center',
        padding: 16,
      }}
      role="alertdialog"
      aria-modal="true"
      aria-labelledby="session-expired-title"
    >
      {/* Modal */}
      <div
        style={{
          background: 'var(--sys-color-background-elevated)',
          borderRadius: 'var(--ref-radius-xl)',
          padding: 32,
          maxWidth: 400,
          width: '100%',
          boxShadow: 'var(--sys-elevation-modal)',
          textAlign: 'center',
        }}
      >
        <div
          style={{
            width: 56,
            height: 56,
            borderRadius: 28,
            background: 'var(--sys-color-feedback-warning-subtle)',
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'center',
            margin: '0 auto 16px',
          }}
        >
          <Clock size={28} color="var(--sys-color-feedback-warning)" aria-hidden="true" />
        </div>

        <h2
          id="session-expired-title"
          style={{
            fontSize: 'var(--ref-font-size-xl)',
            fontWeight: 'var(--ref-font-weight-semibold)',
            color: 'var(--sys-color-text-primary)',
            margin: '0 0 8px',
          }}
        >
          {t('auth.sessionExpired.title', 'Votre session a expiré')}
        </h2>

        <p
          style={{
            fontSize: 'var(--ref-font-size-sm)',
            color: 'var(--sys-color-text-secondary)',
            margin: '0 0 24px',
            lineHeight: 'var(--ref-line-height-normal)',
          }}
        >
          {t(
            'auth.sessionExpired.body',
            "Vous avez été déconnecté après 30 minutes d'inactivité."
          )}
        </p>

        <Button variant="primary" onClick={onDismiss} className="w-full">
          {t('auth.sessionExpired.cta', 'Se reconnecter')} ({countdown}s)
        </Button>
      </div>
    </div>
  );
}
