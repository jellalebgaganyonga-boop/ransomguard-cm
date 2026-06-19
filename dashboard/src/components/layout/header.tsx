// ============================================================
// src/components/layout/header.tsx  — REMPLACEMENT
// US1.2 AC1.2.1 — Logout button wirée backend
// US1.2 AC1.2.4 — Logout always succeeds client-side
// ============================================================

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
import { useLogout } from '@/hooks/use-auth';
import type { MeResponse } from '@/api/me';

interface HeaderProps {
  me: MeResponse;
  notificationCount?: number;
}

function initials(fullName: string): string {
  const parts = fullName.trim().split(/\s+/);
  const first = parts[0]?.[0] ?? '';
  const last = parts.length > 1 ? (parts[parts.length - 1]?.[0] ?? '') : '';
  return (first + last).toUpperCase();
}

export function Header({ me, notificationCount = 0 }: HeaderProps) {
  const { t, i18n } = useTranslation();
  const toggleMobileSidebar = useUIStore((s) => s.toggleMobileSidebar);
  const setLanguage = useUIStore((s) => s.setLanguage);
  const logoutMutation = useLogout();

  const handleLanguageChange = (lang: 'fr' | 'en') => {
    void i18n.changeLanguage(lang);
    setLanguage(lang);
  };

  // AC1.2.1 + AC1.2.4: logout wired to backend, always clears client state
  const handleLogout = () => logoutMutation.mutate(false);

  return (
    <header className="flex h-16 shrink-0 items-center justify-between border-b border-border-subtle bg-canvas px-6 shadow-sm">
      {/* Left cluster */}
      <div className="flex items-center gap-3">
        <Button
          variant="ghost"
          size="icon"
          className="md:hidden"
          onClick={toggleMobileSidebar}
          aria-label="Menu"
        >
          <Menu className="size-5" aria-hidden="true" />
        </Button>
        <span className="flex items-center gap-2 text-lg font-semibold text-text-primary">
          <span aria-hidden="true">&#x1F6E1;</span>
          {t('app.name')}
        </span>
        <span className="hidden rounded-full bg-primary-subtle px-3 py-1 text-xs font-medium text-primary sm:inline-block">
          {me.tenant.name}
        </span>
      </div>

      {/* Right cluster */}
      <div className="flex items-center gap-2">
        {/* Notification bell */}
        <Button
          variant="ghost"
          size="icon"
          aria-label={t('header.notifications')}
          className="relative"
        >
          <Bell className="size-5" aria-hidden="true" />
          {notificationCount > 0 && (
            <span
              className="absolute right-1 top-1 flex size-[18px] items-center justify-center rounded-full bg-severity-critical text-[11px] font-bold text-white"
              aria-label={`${notificationCount} notifications`}
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
              {(i18n.language ?? 'fr').toUpperCase().slice(0, 2)}
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

        {/* User menu */}
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
          <DropdownMenuContent align="end" className="min-w-[200px]">
            <DropdownMenuLabel className="font-normal">
              <p className="text-sm font-medium text-text-primary">{me.full_name}</p>
              <p className="truncate text-xs text-text-tertiary">{me.email}</p>
            </DropdownMenuLabel>
            <DropdownMenuSeparator />
            <DropdownMenuItem>
              <UserIcon className="size-4" aria-hidden="true" />
              {t('header.profile')}
            </DropdownMenuItem>
            <DropdownMenuSeparator />
            {/* AC1.2.1: single session logout */}
            <DropdownMenuItem
              onSelect={handleLogout}
              className="text-error focus:text-error"
            >
              <LogOut className="size-4" aria-hidden="true" />
              {t('header.logout')}
            </DropdownMenuItem>
          </DropdownMenuContent>
        </DropdownMenu>
      </div>
    </header>
  );
}
