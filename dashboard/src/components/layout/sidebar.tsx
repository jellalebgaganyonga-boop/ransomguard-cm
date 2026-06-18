import { NavLink } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { cn } from '@/lib/utils';
import { getNavItemsForRole } from '@/config/navigation';
import type { Role } from '@/api/me';

/**
 * Sidebar — Organism #36 (Atomic Design inventory, Design Phase Day 7-8 §1).
 * Visual spec: Design Phase Day 6 §4.8 (cmp.sidebar.*), Day 9-10 §1
 * Section 2 (Hi-Fi Mockup 1).
 *
 * Width: 260px fixed (cmp.sidebar.width). Active item: left border +
 * selected background + primary text color (Design Phase Day 9-10 §1).
 */

interface SidebarProps {
  role: Role;
  /** Badge counts per nav `to` path, e.g. { '/alerts': 3 }. Sprint 7: alerts only. */
  badgeCounts?: Record<string, number>;
}

export function Sidebar({ role, badgeCounts = {} }: SidebarProps) {
  const { t } = useTranslation();
  const items = getNavItemsForRole(role);

  return (
    <nav
      aria-label={t('nav.dashboard')}
      className="hidden w-[260px] shrink-0 flex-col gap-1 border-r border-border-subtle bg-surface-default px-2 py-4 md:flex"
    >
      {items.map((item) => {
        const Icon = item.icon;
        const badge = badgeCounts[item.to];
        return (
          <NavLink
            key={item.to}
            to={item.to}
            className={({ isActive }) =>
              cn(
                'flex h-10 items-center gap-3 rounded-md border-l-[3px] border-transparent px-4 text-sm text-text-secondary transition-colors',
                'hover:bg-hover-bg hover:text-text-primary',
                isActive &&
                  'border-primary bg-selected-bg font-medium text-primary hover:bg-selected-bg hover:text-primary'
              )
            }
          >
            <Icon className="size-5 shrink-0" aria-hidden="true" />
            <span className="flex-1">{t(item.labelKey)}</span>
            {badge !== undefined && badge > 0 && (
              <span
                className="flex h-[18px] min-w-[18px] items-center justify-center rounded-full bg-severity-critical px-1 text-[11px] font-bold text-white"
                aria-label={`${badge} ${t('nav.alerts')}`}
              >
                {badge}
              </span>
            )}
          </NavLink>
        );
      })}
    </nav>
  );
}
