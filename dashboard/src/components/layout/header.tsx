// ============================================================
// src/components/layout/header.tsx  — REMPLACEMENT
// US1.2 AC1.2.1 — Logout button wirée backend
// US1.2 AC1.2.4 — Logout always succeeds client-side
// ============================================================

import { Bell, Globe, LogOut, Menu, Moon, Settings, Sun } from 'lucide-react';
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
import { useLanguage } from '@/hooks/use-language';
import { FRENCH_ENABLED } from '@/i18n';
import { LanguageSwitch } from '@/components/layout/language-switch';
import { useTheme } from '@/hooks/use-theme';
import { formatDistanceToNow } from '@/lib/format-date';
import type { MeResponse } from '@/api/me';

interface HeaderProps {
  me: MeResponse;
}

const LANGUAGE_NAMES = { en: 'English', fr: 'Français' } as const;

function initials(fullName: string): string {
  const parts = fullName.trim().split(/\s+/);
  const first = parts[0]?.[0] ?? '';
  const last = parts.length > 1 ? (parts[parts.length - 1]?.[0] ?? '') : '';
  return (first + last).toUpperCase();
}

export function Header({ me }: HeaderProps) {
  const { t } = useTranslation();
  const toggleMobileSidebar = useUIStore((s) => s.toggleMobileSidebar);
  const logoutMutation = useLogout();
  const navigate = useNavigate();
  const language = useLanguage();
  const theme = useTheme();

  // AC1.2.1 + AC1.2.4: logout wired to backend, always clears client state
  const handleLogout = () => logoutMutation.mutate();
  // US1.2.2: every session on every device, after an explicit confirmation.
  const [confirmLogoutAll, setConfirmLogoutAll] = useState(false);

  // The server returns an array of role strings; the first one is the highest-ranked by
  // convention (tenant_admin > security_analyst > read_only_auditor). Shown as plain text,
  // not translated, because it matches what the GRID knows.
  const primaryRole = me.roles[0] ?? '';
  const languageLabel = me.preferences.language
    ? LANGUAGE_NAMES[me.preferences.language]
    : t('header.profileSystemDefault');

  return (
    <header className="flex h-16 shrink-0 items-center justify-between gap-2 border-b border-border-subtle bg-canvas px-3 shadow-sm sm:px-6">
      {/* Left cluster: shrinks first (the brand truncates), the buttons on the right never do. */}
      <div className="flex min-w-0 items-center gap-2 sm:gap-3">
        <Button
          variant="ghost"
          size="icon"
          className="md:hidden"
          onClick={toggleMobileSidebar}
          aria-label="Menu"
        >
          <Menu className="size-5" aria-hidden="true" />
        </Button>
        <span className="flex min-w-0 items-center gap-2 text-base font-semibold text-text-primary sm:text-lg">
          <span aria-hidden="true">&#x1F6E1;</span>
          <span className="truncate">{t('app.name')}</span>
        </span>
        <span className="hidden rounded-full bg-primary-subtle px-3 py-1 text-xs font-medium text-primary sm:inline-block">
          {me.tenant.name}
        </span>
      </div>

      {/* Right cluster */}
      <div className="flex shrink-0 items-center gap-1 sm:gap-2">
        {/* The console is bilingual and English by default. Visible in the bar rather than
            buried in the account menu: a user who needs French needs to find it without
            reading English first. */}
        {FRENCH_ENABLED && <LanguageSwitch className="hidden sm:flex" />}

        {/* Light/dark toggle. The resolved theme (not the choice) drives the icon so a user on
            'system' sees whichever theme is currently in effect. One click flips to the opposite
            explicit choice, overriding 'system'. */}
        <Button
          variant="ghost"
          size="icon"
          aria-label={theme.resolved === 'dark' ? t('theme.toggleLight') : t('theme.toggleDark')}
          onClick={theme.toggle}
          data-testid="theme-toggle"
        >
          {theme.resolved === 'dark' ? (
            <Sun className="size-5" aria-hidden="true" />
          ) : (
            <Moon className="size-5" aria-hidden="true" />
          )}
        </Button>

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

        {/* User menu (Fix D): my settings, the other language, sign out. The language used to be a
            dropdown of its own that changed this browser only; the choice is stored on the GRID now
            (use-language.ts), the same as in Settings. */}
        <DropdownMenu>
          <DropdownMenuTrigger asChild>
            <button
              className="flex min-h-[44px] items-center gap-2 rounded-md px-2 py-1 text-sm text-text-primary transition-colors hover:bg-hover-bg"
              aria-label={t('header.userMenu')}
              data-testid="user-menu"
            >
              <Avatar>
                <AvatarFallback>{initials(me.full_name)}</AvatarFallback>
              </Avatar>
              <span className="hidden max-w-[140px] truncate font-medium md:inline">
                {me.full_name}
              </span>
            </button>
          </DropdownMenuTrigger>
          <DropdownMenuContent align="end" className="min-w-[280px]" data-testid="user-menu-content">
            {/* Name + email: the two lines are visible to screen readers via the Label role. */}
            <DropdownMenuLabel className="font-normal">
              <p className="text-sm font-medium text-text-primary" data-testid="profile-name">{me.full_name}</p>
              <p className="truncate text-xs text-text-secondary" data-testid="profile-email">{me.email}</p>
            </DropdownMenuLabel>
            {/* Profile fields the GRID knows about me (role, tenant, language, last sign-in). As a
                plain styled container, not another menu-level widget, so Radix's menu role tree stays
                clean (axe-core's aria-required-children). All values come from /dashboard/me, no
                fabrication; the labels are translated. */}
            <div className="px-3 pb-2 text-sm" data-testid="profile-fields">
              <div className="grid grid-cols-[auto_1fr] gap-x-3 gap-y-1">
                <span className="text-text-secondary">{t('header.profileRole')}</span>
                <span className="font-mono text-text-primary" data-testid="profile-role">{primaryRole}</span>
                <span className="text-text-secondary">{t('header.profileTenant')}</span>
                <span className="truncate text-text-primary" data-testid="profile-tenant">{me.tenant.name}</span>
                <span className="text-text-secondary">{t('header.profileLanguage')}</span>
                <span className="text-text-primary" data-testid="profile-language">{languageLabel}</span>
                <span className="text-text-secondary">{t('header.profileLastLogin')}</span>
                <span className="text-text-primary" data-testid="profile-last-login">
                  {me.last_login_at ? formatDistanceToNow(me.last_login_at) : t('header.profileNever')}
                </span>
              </div>
            </div>
            <DropdownMenuSeparator />
            <DropdownMenuItem onSelect={() => navigate('/settings')}>
              <Settings className="size-4" aria-hidden="true" />
              {t('header.mySettings')}
            </DropdownMenuItem>
            {/* The other language is only offered when the build enables French. */}
            {FRENCH_ENABLED && (
              <DropdownMenuItem onSelect={() => void language.choose(language.other)} disabled={language.isPending}>
                <Globe className="size-4" aria-hidden="true" />
                {t('header.switchLanguage', { language: LANGUAGE_NAMES[language.other] })}
              </DropdownMenuItem>
            )}
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
