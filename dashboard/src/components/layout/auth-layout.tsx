import { Outlet } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { LanguageSwitch } from '@/components/layout/language-switch';

/**
 * AuthLayout — Template #47 (Atomic Design inventory, Design Phase
 * Day 7-8 §1). Centered card layout for /auth/* routes (login,
 * forgot-password in Sprint 8).
 *
 * Visual spec: Design Phase Day 7-8 §4 (Low-Fi Wireframe 1 — Login).
 * - Centered card, max-width 400px
 * - Background: sys.color.surface.subtle
 * - Card: sys.color.background.elevated + sys.elevation.card
 * - Footer: version + language switcher
 */

export function AuthLayout() {
  const { t } = useTranslation();

  return (
    <div className="flex min-h-screen items-center justify-center bg-surface-subtle px-4">
      <div className="w-full max-w-[400px] rounded-lg border border-border-subtle bg-elevated p-8 shadow-sm">
        <div className="mb-6 text-center">
          <p className="flex items-center justify-center gap-2 text-lg font-semibold text-text-primary">
            <span aria-hidden="true">🛡</span>
            {t('app.name')}
          </p>
          <p className="mt-1 text-sm text-text-secondary">{t('app.tagline')}</p>
        </div>

        <Outlet />

        <div className="mt-6 flex items-center justify-between border-t border-border-subtle pt-4 text-xs text-text-secondary">
          <span>Version {import.meta.env.VITE_APP_VERSION}</span>
          {/* Visible, not inside a menu: somebody who needs French should not have to read
              English to find the way to French. The choice is this browser's until there is an
              account to attach it to. */}
          <LanguageSwitch localOnly />
        </div>
      </div>
    </div>
  );
}
