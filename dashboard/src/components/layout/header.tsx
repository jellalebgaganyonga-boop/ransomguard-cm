// ============================================================
// src/components/layout/header.tsx  — REMPLACEMENT
// US1.2 AC1.2.1 — Logout button wirée backend
// US1.2 AC1.2.4 — Logout always succeeds client-side
// ============================================================

import { Bell, Globe, LogOut, User as UserIcon, Menu } from 'lucide-react';
import { useState } from 'react';
import { useNavigate } from 'react-router-dom';
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
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog';
import { useUIStore } from '@/stores/ui.store';
import { useLogout } from '@/hooks/use-auth';
import type { MeResponse } from '@/api/me';

interface HeaderProps {
  me: MeResponse;
}

function initials(fullName: string): string {
  const parts = fullName.trim().split(/\s+/);
  const first = parts[0]?.[0] ?? '';
  const last = parts.length > 1 ? (parts[parts.length - 1]?.[0] ?? '') : '';
  return (first + last).toUpperCase();
}

export function Header({ me }: HeaderProps) {
  const { t, i18n } = useTranslation();
  const toggleMobileSidebar = useUIStore((s) => s.toggleMobileSidebar);
  const setLanguage = useUIStore((s) => s.setLanguage);
  const logoutMutation = useLogout();
  const navigate = useNavigate();

  const handleLanguageChange = (lang: 'fr' | 'en') => {
    void i18n.changeLanguage(lang);
    setLanguage(lang);
  };

  // AC1.2.1 + AC1.2.4: logout wired to backend, always clears client state
  const handleLogout = () => logoutMutation.mutate();
  // US1.2.2: every session on every device, after an explicit confirmation.
  const [confirmLogoutAll, setConfirmLogoutAll] = useState(false);

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
        {/* Notification preferences, for every role (Fix C.4). The badge it used to carry always
            read 0: nothing fed it; nothing pretends a count now. */}
        <Button
          variant="ghost"
          size="icon"
          aria-label={t('header.notifications')}
          onClick={() => navigate('/settings?tab=notifications')}
          data-testid="notifications-link"
        >
          <Bell className="size-5" aria-hidden="true" />
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
            <DropdownMenuItem
              onSelect={() => setConfirmLogoutAll(true)}
              className="text-error focus:text-error"
            >
              <LogOut className="size-4" aria-hidden="true" />
              {t('header.logoutAll')}
            </DropdownMenuItem>
          </DropdownMenuContent>
        </DropdownMenu>
      </div>

      <Dialog open={confirmLogoutAll} onOpenChange={setConfirmLogoutAll}>
        <DialogContent>
          <DialogHeader>
            <DialogTitle>{t('header.logoutAllTitle')}</DialogTitle>
          </DialogHeader>
          <DialogDescription className="px-5 text-sm text-text-primary">
            {t('header.logoutAllBody')}
          </DialogDescription>
          <DialogFooter>
            <Button variant="secondary" onClick={() => setConfirmLogoutAll(false)}>
              {t('common.cancel')}
            </Button>
            <Button
              variant="primary"
              className="bg-error text-white hover:bg-error/90"
              isLoading={logoutMutation.isPending}
              onClick={() => logoutMutation.mutate({ everywhere: true })}
            >
              {t('header.logoutAllConfirm')}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </header>
  );
}
