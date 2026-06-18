import type { ReactNode } from 'react';
import { Outlet, NavLink } from 'react-router-dom';
import * as Dialog from '@radix-ui/react-dialog';
import { X } from 'lucide-react';
import { useTranslation } from 'react-i18next';
import { Header } from './header';
import { Sidebar } from './sidebar';
import { useUIStore } from '@/stores/ui.store';
import { resolvePrimaryRole } from '@/hooks/use-me';
import type { MeResponse } from '@/api/me';
import { getNavItemsForRole } from '@/config/navigation';
import { cn } from '@/lib/utils';

/**
 * AppShellLayout — Template #48 (Atomic Design inventory, Design Phase
 * Day 7-8 §1). Composes Header (Organism #35) + Sidebar (Organism #36)
 * + routed content (<Outlet />).
 *
 * Desktop (md+): Sidebar always visible at 260px (cmp.sidebar.width).
 * Mobile (<md): Sidebar collapses into a Radix Dialog drawer triggered
 * by the hamburger button in Header (Definition Phase Day 4 §1 — IA
 * supports 320px minimum width).
 *
 * Per ADR-FE-009: this layout is only reached via ProtectedRoute
 * (App.tsx), which guarantees `me` (GET /dashboard/me) has already
 * resolved successfully — no loading/error states needed here.
 */

interface AppShellLayoutProps {
  me: MeResponse;
  /** Sprint 7: alert count badge for sidebar "Alertes" item. */
  alertCount?: number;
  /**
   * Optional override for the content area. When omitted (normal case),
   * renders <Outlet /> for nested route content. ProtectedRoute passes
   * an explicit `children` (ForbiddenPage) when the resolved role is not
   * authorized for the current route — bypassing the route tree's
   * <Outlet /> for that one case.
   */
  children?: ReactNode;
}

export function AppShellLayout({ me, alertCount = 0, children }: AppShellLayoutProps) {
  const { t } = useTranslation();
  const role = resolvePrimaryRole(me.roles);
  const isMobileSidebarOpen = useUIStore((s) => s.isMobileSidebarOpen);
  const closeMobileSidebar = useUIStore((s) => s.closeMobileSidebar);

  const badgeCounts = { '/alerts': alertCount };
  const navItems = getNavItemsForRole(role);

  return (
    <div className="flex h-screen flex-col">
      <Header me={me} notificationCount={alertCount} />

      <div className="flex flex-1 overflow-hidden">
        {/* Desktop sidebar */}
        <Sidebar role={role} badgeCounts={badgeCounts} />

        {/* Mobile sidebar drawer */}
        <Dialog.Root open={isMobileSidebarOpen} onOpenChange={closeMobileSidebar}>
          <Dialog.Portal>
            <Dialog.Overlay className="fixed inset-0 z-modal bg-scrim data-[state=open]:animate-in data-[state=open]:fade-in-0 md:hidden" />
            <Dialog.Content
              className="fixed inset-y-0 left-0 z-modal flex w-[260px] flex-col bg-surface-default p-4 shadow-xl data-[state=open]:animate-in data-[state=open]:slide-in-from-left md:hidden"
              aria-describedby={undefined}
            >
              <div className="mb-4 flex items-center justify-between">
                <Dialog.Title className="text-lg font-semibold text-text-primary">
                  {t('app.name')}
                </Dialog.Title>
                <Dialog.Close asChild>
                  <button
                    className="rounded-md p-1 text-text-secondary hover:bg-hover-bg"
                    aria-label={t('common.close')}
                  >
                    <X className="size-5" />
                  </button>
                </Dialog.Close>
              </div>
              <nav className="flex flex-col gap-1">
                {navItems.map((item) => {
                  const Icon = item.icon;
                  return (
                    <NavLink
                      key={item.to}
                      to={item.to}
                      onClick={closeMobileSidebar}
                      className={({ isActive }) =>
                        cn(
                          'flex h-10 items-center gap-3 rounded-md border-l-[3px] border-transparent px-4 text-sm text-text-secondary transition-colors',
                          'hover:bg-hover-bg hover:text-text-primary',
                          isActive &&
                            'border-primary bg-selected-bg font-medium text-primary'
                        )
                      }
                    >
                      <Icon className="size-5 shrink-0" aria-hidden="true" />
                      {t(item.labelKey)}
                    </NavLink>
                  );
                })}
              </nav>
            </Dialog.Content>
          </Dialog.Portal>
        </Dialog.Root>

        {/* Routed content */}
        <main className="flex-1 overflow-y-auto bg-canvas">
          <div className="mx-auto max-w-[1280px] px-4 py-6 sm:px-6 lg:px-8">
            {children ?? <Outlet />}
          </div>
        </main>
      </div>
    </div>
  );
}
