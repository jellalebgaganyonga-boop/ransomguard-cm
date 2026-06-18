import { Bell, Globe, LogOut, User as UserIcon, Menu } from 'lucide-react';
import { useTranslation } from 'react-i18next';
import { Avatar, AvatarFallback } from '@/components/ui/avatar';
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu';
import { Button } from '@/components/ui/button';
import { useUIStore } from '@/stores/ui.store';
import { useAuthStore } from '@/stores/auth.store';
import type { MeResponse } from '@/api/me';

/**
 * Header — Organism #35 (Atomic Design inventory, Design Phase Day 7-8 §1).
 * Visual spec: Design Phase Day 6 §4.9 (cmp.header.*), Day 9-10 §1
 * Section 1 (Hi-Fi Mockup 1 header bar specification).
 *
 * Left cluster: logo + tenant pill.
 * Right cluster: notification bell + language switcher + user menu.
 *
 * Sprint 7 scope: notification bell shows a static count (real-time
 * polling wired in Day 4-5 EPIC-ALERTS). Language switcher toggles
 * i18next language client-side only (persisted preference is Sprint 8).
 */

interface HeaderProps {
  me: MeResponse;
  /** Sprint 7: number of new/unacknowledged alerts. 0 for read_only_auditor. */
  notificationCount?: number;
}

function initials(fullName: string): string {
  const parts = fullName.trim().split(/\s+/);
  const first = parts[0]?.[0] ?? '';
  const last = parts.length > 1 ? parts[parts.length - 1]?.[0] ?? '' : '';
  return (first + last).toUpperCase();
}

export function Header({ me, notificationCount = 0 }: HeaderProps) {
  const { t, i18n } = useTranslation();
  const toggleMobileSidebar = useUIStore((s) => s.toggleMobileSidebar);
  const setLanguage = useUIStore((s) => s.setLanguage);
  const clearAccessToken = useAuthStore((s) => s.clearAccessToken);

  const handleLanguageChange = (lang: 'fr' | 'en') => {
    void i18n.changeLanguage(lang);
    setLanguage(lang);
  };

  const handleLogout = () => {
    // Sprint 7: client-side logout only (clear in-memory token).
    // Day 2-3 EPIC-AUTH wires POST /auth/logout to revoke the refresh
    // cookie server-side (Redis blacklist) before clearing local state.
    clearAccessToken();
  };

  return (
    <header className="flex h-16 shrink-0 items-center justify-between border-b border-border-subtle bg-canvas px-6 shadow-sm">
      {/* Left cluster */}
      <div className="flex items-center gap-3">
        <Button
          variant="ghost"
          size="icon"
          className="md:hidden"
          onClick={toggleMobileSidebar}
          aria-label={t('nav.dashboard')}
        >
          <Menu className="size-5" aria-hidden="true" />
        </Button>

        <span className="flex items-center gap-2 text-lg font-semibold text-text-primary">
          <span aria-hidden="true">🛡</span>
          {t('app.name')}
        </span>

        <span className="hidden rounded-full bg-primary-subtle px-3 py-1 text-xs font-medium text-primary sm:inline-block">
          {me.tenant.name}
        </span>
      </div>

      {/* Right cluster */}
      <div className="flex items-center gap-2">
        {/* Notification bell — Molecule #34 */}
        <Button variant="ghost" size="icon" aria-label={t('header.notifications')} className="relative">
          <Bell className="size-5" aria-hidden="true" />
          {notificationCount > 0 && (
            <span
              className="absolute right-1 top-1 flex size-[18px] items-center justify-center rounded-full bg-severity-critical text-[11px] font-bold text-white"
              aria-hidden="true"
            >
              {notificationCount}
            </span>
          )}
        </Button>

        {/* Language switcher */}
        <DropdownMenu>
          <DropdownMenuTrigger asChild>
            <Button variant="ghost" size="sm" aria-label={t('header.language')}>
              <Globe className="size-4" aria-hidden="true" />
              {i18n.language?.toUpperCase().slice(0, 2) ?? 'FR'}
            </Button>
          </DropdownMenuTrigger>
          <DropdownMenuContent align="end">
            <DropdownMenuItem onSelect={() => handleLanguageChange('fr')}>
              Français
            </DropdownMenuItem>
            <DropdownMenuItem onSelect={() => handleLanguageChange('en')}>
              English
            </DropdownMenuItem>
          </DropdownMenuContent>
        </DropdownMenu>

        {/* User menu — Molecule #33 */}
        <DropdownMenu>
          <DropdownMenuTrigger asChild>
            <button
              className="flex items-center gap-2 rounded-md px-2 py-1 text-sm text-text-primary transition-colors hover:bg-hover-bg"
              aria-label={t('header.profile')}
            >
              <Avatar>
                <AvatarFallback>{initials(me.full_name)}</AvatarFallback>
              </Avatar>
              <span className="hidden max-w-[140px] truncate font-medium md:inline">
                {me.full_name}
              </span>
            </button>
          </DropdownMenuTrigger>
          <DropdownMenuContent align="end">
            <DropdownMenuLabel>{me.email}</DropdownMenuLabel>
            <DropdownMenuSeparator />
            <DropdownMenuItem>
              <UserIcon className="size-4" aria-hidden="true" />
              {t('header.profile')}
            </DropdownMenuItem>
            <DropdownMenuItem onSelect={handleLogout}>
              <LogOut className="size-4" aria-hidden="true" />
              {t('header.logout')}
            </DropdownMenuItem>
          </DropdownMenuContent>
        </DropdownMenu>
      </div>
    </header>
  );
}
