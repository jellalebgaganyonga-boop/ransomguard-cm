// ============================================================
// src/pages/login-page.tsx  — REMPLACEMENT COMPLET
// US1.1 AC1.1.1-6 — Login wirée backend
// US1.3 AC1.3.4 — app boot sequence déjà dans App.tsx
//
// AC1.1.1: valid credentials → access_token in memory + redirect
// AC1.1.2: 401 → inline generic error (no enumeration)
// AC1.1.3: disabled account → same 401 message
// AC1.1.4: client-side validation (Zod) before any API call
// AC1.1.5: bilingual FR/EN
// AC1.1.6: login → /me fetch → role-based redirect
// ============================================================

import { useState, type KeyboardEvent } from 'react';
import { useForm, Controller } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { z } from 'zod';
import { useTranslation } from 'react-i18next';
import { Eye, EyeOff, AlertCircle } from 'lucide-react';
import { isAxiosError } from 'axios';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { Checkbox } from '@/components/ui/checkbox';
import { useLogin } from '@/hooks/use-auth';

// ── Zod schema (AC1.1.4 — client-side validation) ─────────

function buildLoginSchema(t: (k: string) => string) {
  return z.object({
    email: z
      .string()
      .min(1, t('auth.login.errors.emailRequired'))
      .email(t('auth.login.errors.emailInvalid')),
    password: z.string().min(1, t('auth.login.errors.passwordRequired')),
    rememberMe: z.boolean().optional(),
  });
}

type LoginFormValues = z.infer<ReturnType<typeof buildLoginSchema>>;

// ── Component ──────────────────────────────────────────────

export function LoginPage() {
  const { t } = useTranslation();
  const [showPassword, setShowPassword] = useState(false);
  const [capsLock, setCapsLock] = useState(false);
  const [serverError, setServerError] = useState<string | null>(null);

  const loginMutation = useLogin();

  const schema = buildLoginSchema(t);
  const {
    register,
    handleSubmit,
    control,
    formState: { errors, isSubmitting },
  } = useForm<LoginFormValues>({
    resolver: zodResolver(schema),
    defaultValues: { email: '', password: '', rememberMe: false },
  });

  const onSubmit = async (values: LoginFormValues) => {
    setServerError(null);
    try {
      await loginMutation.mutateAsync({
        email: values.email,
        password: values.password,
      });
      // AC1.1.1 + AC1.1.6: on success, useLogin redirects automatically
    } catch (err) {
      // AC1.1.2: 401 or any error → same generic message (no enumeration)
      if (isAxiosError(err) && err.response?.status === 401) {
        setServerError(t('auth.login.errors.invalidCredentials'));
      } else if (isAxiosError(err) && !err.response) {
        setServerError(t('auth.login.errors.networkError'));
      } else {
        setServerError(t('auth.login.errors.serverError'));
      }
    }
  };

  const handlePasswordKey = (e: KeyboardEvent<HTMLInputElement>) => {
    if (typeof e.getModifierState === 'function') {
      setCapsLock(e.getModifierState('CapsLock'));
    }
  };

  return (
    <form
      onSubmit={handleSubmit(onSubmit)}
      className="flex flex-col gap-4"
      noValidate
      aria-label={t('auth.login.title')}
    >
      <h1 className="text-center text-xl font-semibold text-text-primary">
        {t('auth.login.title')}
      </h1>

      {/* ── Email field ── */}
      <div className="flex flex-col gap-1.5">
        <Label htmlFor="email">{t('auth.login.email')}</Label>
        <Input
          id="email"
          type="email"
          autoComplete="email"
          hasError={!!errors.email}
          aria-describedby={errors.email ? 'email-error' : undefined}
          {...register('email')}
        />
        {errors.email && (
          <p id="email-error" className="text-xs text-error" role="alert">
            {errors.email.message}
          </p>
        )}
      </div>

      {/* ── Password field ── */}
      <div className="flex flex-col gap-1.5">
        <Label htmlFor="password">{t('auth.login.password')}</Label>
        <div className="relative">
          <Input
            id="password"
            type={showPassword ? 'text' : 'password'}
            autoComplete="current-password"
            hasError={!!errors.password}
            aria-describedby={errors.password ? 'password-error' : undefined}
            onKeyUp={handlePasswordKey}
            onKeyDown={handlePasswordKey}
            className="pr-10"
            {...register('password')}
          />
          <button
            type="button"
            onClick={() => setShowPassword((v) => !v)}
            aria-label={
              showPassword
                ? t('auth.login.hidePassword')
                : t('auth.login.showPassword')
            }
            className="absolute inset-y-0 right-0 flex w-10 items-center justify-center text-text-tertiary hover:text-text-primary"
            tabIndex={-1}
          >
            {showPassword ? (
              <EyeOff className="size-4" aria-hidden="true" />
            ) : (
              <Eye className="size-4" aria-hidden="true" />
            )}
          </button>
        </div>
        {errors.password && (
          <p id="password-error" className="text-xs text-error" role="alert">
            {errors.password.message}
          </p>
        )}
        {/* Caps Lock warning */}
        {capsLock && (
          <p className="flex items-center gap-1 text-xs text-warning" role="status">
            <AlertCircle className="size-3" aria-hidden="true" />
            {t('auth.login.capsLockWarning', 'Verrouillage majuscules activé')}
          </p>
        )}
      </div>

      {/* ── Remember me ── */}
      <div className="flex items-center gap-2">
        <Controller
          name="rememberMe"
          control={control}
          render={({ field }) => (
            <Checkbox
              id="rememberMe"
              checked={field.value ?? false}
              onCheckedChange={field.onChange}
            />
          )}
        />
        <Label htmlFor="rememberMe" className="cursor-pointer font-normal">
          {t('auth.login.rememberMe')}
        </Label>
      </div>

      {/* ── Server error (AC1.1.2 — generic, no enumeration) ── */}
      {serverError && (
        <div
          className="flex items-center gap-2 rounded-md border border-error-border bg-error-subtle px-3 py-2"
          role="alert"
          aria-live="polite"
        >
          <AlertCircle className="size-4 shrink-0 text-error" aria-hidden="true" />
          <span className="text-sm text-error">{serverError}</span>
        </div>
      )}

      {/* ── Submit ── */}
      <Button
        type="submit"
        size="lg"
        isLoading={isSubmitting || loginMutation.isPending}
        className="w-full"
      >
        {t('auth.login.submit')}
      </Button>
    </form>
  );
}
