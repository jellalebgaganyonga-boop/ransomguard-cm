/**
 * Settings (Fix C): four tabs under one /settings route, chosen by ?tab=… so the sidebar lights
 * "Settings" alone. Profile (one's own name), Language (kept on the GRID, applied at sign-in),
 * Notifications (e-mail preferences, on the GRID), Security (change password). Every role reaches
 * it: the sidebar entry is admin-only, the header menu brings the others here.
 */

import { useEffect, useState, type FormEvent } from 'react';
import { useTranslation } from 'react-i18next';
import { Navigate, useLocation, useSearchParams } from 'react-router-dom';
import { User as UserIcon, Globe, Bell, KeyRound } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { NotificationPreferencesPanel } from '@/components/settings/notification-preferences-panel';
import { useMe, useUpdateMe, useChangePassword } from '@/hooks/use-me';
import { useLanguage } from '@/hooks/use-language';
import { FRENCH_ENABLED } from '@/i18n';
import { useToast } from '@/lib/toast-context';
import type { MeResponse } from '@/api/me';
import { isLegacyNotificationsPath, tabFrom, type SettingsTab } from '@/lib/settings-tab';

export function SettingsPage() {
  const { t } = useTranslation();
  const location = useLocation();
  const [searchParams, setSearchParams] = useSearchParams();
  const tab = tabFrom(location.search, location.pathname);
  const { data: me } = useMe();

  // The former /settings/notifications page: same tab, canonical URL (the sidebar's NavLink has
  // `end`, so only /settings itself lights "Settings").
  if (isLegacyNotificationsPath(location.pathname)) {
    return <Navigate to="/settings?tab=notifications" replace />;
  }

  const select = (next: SettingsTab) => {
    const params = new URLSearchParams(searchParams);
    params.set('tab', next);
    setSearchParams(params, { replace: false });
  };

  const tabs: { id: SettingsTab; icon: typeof UserIcon; label: string }[] = [
    { id: 'profile', icon: UserIcon, label: t('settings.tabs.profile') },
    { id: 'language', icon: Globe, label: t('settings.tabs.language') },
    { id: 'notifications', icon: Bell, label: t('settings.tabs.notifications') },
    { id: 'security', icon: KeyRound, label: t('settings.tabs.security') },
  ];

  return (
    <div className="max-w-2xl space-y-6 p-4 sm:p-6">
      <div>
        <h1 className="mb-1 text-2xl font-bold text-text-primary">{t('settings.title')}</h1>
        <p className="text-text-muted">{t('settings.subtitle')}</p>
      </div>

      {/* Phones: the four tabs in two rows of two; wider: one row. */}
      <div role="tablist" aria-label={t('settings.title')} className="grid grid-cols-2 gap-1 border-b border-border-subtle sm:flex sm:flex-wrap">
        {tabs.map(({ id, icon: Icon, label }) => (
          <button
            key={id}
            type="button"
            role="tab"
            id={`settings-tab-${id}`}
            aria-selected={tab === id}
            aria-controls={`settings-panel-${id}`}
            onClick={() => select(id)}
            className={`flex min-h-[44px] items-center gap-2 rounded-t-md px-3 text-sm ${
              tab === id
                ? 'border-b-2 border-primary font-semibold text-text-primary'
                : 'text-text-secondary hover:text-text-primary'
            }`}
          >
            <Icon className="size-4" aria-hidden="true" />
            {label}
          </button>
        ))}
      </div>

      <section role="tabpanel" id={`settings-panel-${tab}`} aria-labelledby={`settings-tab-${tab}`} className="space-y-4">
        {tab === 'profile' && me && <ProfileTab me={me} />}
        {tab === 'language' && me && <LanguageTab me={me} />}
        {tab === 'notifications' && (
          <>
            <p className="text-sm text-text-muted">{t('settings.notifications.description')}</p>
            <NotificationPreferencesPanel />
          </>
        )}
        {tab === 'security' && <SecurityTab />}
      </section>
    </div>
  );
}

function ProfileTab({ me }: { me: MeResponse }) {
  const { t } = useTranslation();
  const toast = useToast();
  const mutation = useUpdateMe();
  const [editing, setEditing] = useState(false);
  const [name, setName] = useState(me.full_name);
  useEffect(() => setName(me.full_name), [me.full_name]);

  const save = async (event: FormEvent) => {
    event.preventDefault();
    const trimmed = name.trim();
    if (!trimmed) return;
    try {
      await mutation.mutateAsync({ full_name: trimmed });
      toast.success(t('settings.profile.saved'));
      setEditing(false);
    } catch {
      toast.error(t('settings.profile.saveFailed'));
    }
  };

  return (
    <dl className="grid gap-4 rounded-lg border border-border-subtle bg-canvas p-4 sm:grid-cols-[160px_1fr]">
      <dt className="text-sm font-medium text-text-secondary">{t('settings.profile.name')}</dt>
      <dd>
        {editing ? (
          <form onSubmit={(e) => void save(e)} className="flex flex-wrap items-center gap-2">
            <Label htmlFor="full-name" className="sr-only">{t('settings.profile.name')}</Label>
            <Input id="full-name" value={name} onChange={(e) => setName(e.target.value)} maxLength={200} className="max-w-xs" />
            <Button type="submit" size="sm" disabled={mutation.isPending || !name.trim()}>{t('common.save', 'Save')}</Button>
            <Button type="button" size="sm" variant="secondary" onClick={() => { setEditing(false); setName(me.full_name); }}>
              {t('common.cancel', 'Cancel')}
            </Button>
          </form>
        ) : (
          <div className="flex flex-wrap items-center gap-3">
            <span className="text-sm text-text-primary" data-testid="profile-name">{me.full_name}</span>
            <Button type="button" size="sm" variant="secondary" onClick={() => setEditing(true)}>{t('settings.profile.edit')}</Button>
          </div>
        )}
      </dd>
      <dt className="text-sm font-medium text-text-secondary">{t('settings.profile.email')}</dt>
      <dd className="text-sm text-text-primary">{me.email}</dd>
      <dt className="text-sm font-medium text-text-secondary">{t('settings.profile.roles')}</dt>
      <dd className="text-sm text-text-primary">{me.roles.map((r) => t(`roles.${r}`, r)).join(', ')}</dd>
      <dt className="text-sm font-medium text-text-secondary">{t('settings.profile.tenant')}</dt>
      <dd className="text-sm text-text-primary">{me.tenant.name}</dd>
    </dl>
  );
}

function LanguageTab({ me }: { me: MeResponse }) {
  const { t } = useTranslation();
  const { current, choose, isPending } = useLanguage();

  return (
    <div className="space-y-3 rounded-lg border border-border-subtle bg-canvas p-4">
      <p className="text-sm text-text-muted">{t('settings.language.description')}</p>
      <div className="flex flex-wrap items-center gap-3">
        <Label htmlFor="language">{t('settings.language.title')}</Label>
        <select
          id="language"
          value={me.preferences.language ?? current}
          disabled={isPending}
          onChange={(e) => void choose(e.target.value as 'en' | 'fr')}
          className="min-h-[44px] rounded-md border border-border-default bg-canvas px-3 text-sm text-text-primary"
        >
          <option value="en">English</option>
          {/* Only offered when the build enables French; the console is English. */}
          {FRENCH_ENABLED && <option value="fr">Français</option>}
        </select>
      </div>
      <p className="text-xs text-text-muted">
        {me.preferences.language ? t('settings.language.storedOnGrid') : t('settings.language.notChosenYet')}
      </p>
    </div>
  );
}

function SecurityTab() {
  const { t } = useTranslation();
  const toast = useToast();
  const mutation = useChangePassword();
  const [current, setCurrent] = useState('');
  const [next, setNext] = useState('');
  const [confirm, setConfirm] = useState('');
  const [problem, setProblem] = useState<string | null>(null);

  const tooShort = next.length > 0 && next.length < 8;
  const mismatch = confirm.length > 0 && next !== confirm;
  const canSubmit = current.length > 0 && next.length >= 8 && next === confirm && !mutation.isPending;

  const submit = async (event: FormEvent) => {
    event.preventDefault();
    if (!canSubmit) return;
    setProblem(null);
    try {
      await mutation.mutateAsync({ current_password: current, new_password: next });
      toast.success(t('settings.security.changed'));
      setCurrent(''); setNext(''); setConfirm('');
    } catch (error) {
      const status = (error as { response?: { status?: number } })?.response?.status;
      setProblem(status === 403 ? t('settings.security.wrongCurrent') : status === 422 ? t('settings.security.rejected') : t('settings.security.failed'));
    }
  };

  return (
    <form onSubmit={(e) => void submit(e)} className="space-y-4 rounded-lg border border-border-subtle bg-canvas p-4">
      <p className="text-sm text-text-muted">{t('settings.security.description')}</p>
      <div className="grid gap-3 sm:max-w-sm">
        <div>
          <Label htmlFor="current-password">{t('settings.security.current')}</Label>
          <Input id="current-password" type="password" autoComplete="current-password" value={current} onChange={(e) => setCurrent(e.target.value)} />
        </div>
        <div>
          <Label htmlFor="new-password">{t('settings.security.new')}</Label>
          <Input id="new-password" type="password" autoComplete="new-password" value={next} onChange={(e) => setNext(e.target.value)} aria-invalid={tooShort} />
          {tooShort && <p className="mt-1 text-xs text-error">{t('settings.security.tooShort')}</p>}
        </div>
        <div>
          <Label htmlFor="confirm-password">{t('settings.security.confirm')}</Label>
          <Input id="confirm-password" type="password" autoComplete="new-password" value={confirm} onChange={(e) => setConfirm(e.target.value)} aria-invalid={mismatch} />
          {mismatch && <p className="mt-1 text-xs text-error">{t('settings.security.mismatch')}</p>}
        </div>
      </div>
      {problem && <p role="alert" className="text-sm text-error">{problem}</p>}
      <Button type="submit" disabled={!canSubmit}>{t('settings.security.submit')}</Button>
    </form>
  );
}
