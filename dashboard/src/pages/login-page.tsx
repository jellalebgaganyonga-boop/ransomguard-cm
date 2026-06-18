import { useState, type KeyboardEvent } from 'react';
import { useForm, Controller } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { z } from 'zod';
import { useTranslation } from 'react-i18next';
import { Eye, EyeOff } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { Checkbox } from '@/components/ui/checkbox';

/**
 * LoginPage — per Low-Fi Wireframe 1 (Design Phase Day 7-8 §4).
 *
 * Day 1 scope: form structure, client-side validation (Zod via
 * ADR-FE-005), password visibility toggle, Caps Lock detection.
 *
 * Day 2-3 EPIC-AUTH wires onSubmit to the `login` mutation (api/auth.ts),
 * maps 401 -> auth.login.errors.invalidCredentials (shake animation),
 * 5xx -> serverError toast, network errors -> networkError toast, and
 * navigates to the role-based dashboard on success.
 */

function buildLoginSchema(t: (key: string) => string) {
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

export function LoginPage() {
  const { t } = useTranslation();
  const [showPassword, setShowPassword] = useState(false);
  const [capsLockOn, setCapsLockOn] = useState(false);

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

  const onSubmit = (_values: LoginFormValues) => {
    // TODO(Day 2-3 EPIC-AUTH): wire to useMutation(login), handle
    // 401/5xx/network errors, set access token, navigate to dashboard.
    // Intentionally left as a no-op stub for Day 1 bootstrap so the
    // form's validation behavior can be reviewed/tested in isolation.
  };

  const handlePasswordKeyEvent = (e: KeyboardEvent<HTMLInputElement>) => {
    if (typeof e.getModifierState === 'function') {
      setCapsLockOn(e.getModifierState('CapsLock'));
    }
  };

  return (
    <form onSubmit={handleSubmit(onSubmit)} className="flex flex-col gap-4" noValidate>
      <h1 className="text-center text-xl font-semibold text-text-primary">
        {t('auth.login.title')}
      </h1>

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

      <div className="flex flex-col gap-1.5">
        <Label htmlFor="password">{t('auth.login.password')}</Label>
        <div className="relative">
          <Input
            id="password"
            type={showPassword ? 'text' : 'password'}
            autoComplete="current-password"
            hasError={!!errors.password}
            aria-describedby={errors.password ? 'password-error' : undefined}
            onKeyUp={handlePasswordKeyEvent}
            onKeyDown={handlePasswordKeyEvent}
            className="pr-10"
            {...register('password')}
          />
          <button
            type="button"
            onClick={() => setShowPassword((v) => !v)}
            className="absolute inset-y-0 right-0 flex w-10 items-center justify-center text-text-tertiary hover:text-text-primary"
            aria-label={
              showPassword ? t('auth.login.hidePassword') : t('auth.login.showPassword')
            }
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
        {capsLockOn && (
          <p className="flex items-center gap-1 text-xs text-warning" role="status">
            ⚠ Verr. Maj activé
          </p>
        )}
      </div>

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

      <Button type="submit" size="lg" isLoading={isSubmitting} className="w-full">
        {t('auth.login.submit')}
      </Button>
    </form>
  );
}
