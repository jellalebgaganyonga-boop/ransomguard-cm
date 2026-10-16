import { useTranslation } from 'react-i18next';
import { Link } from 'react-router-dom';
import { Button } from '@/components/ui/button';
import { FileQuestion } from 'lucide-react';

export function NotFoundPage() {
  const { t } = useTranslation();

  return (
    <div className="flex min-h-[60vh] flex-col items-center justify-center gap-4 text-center">
      <FileQuestion className="size-12 text-text-tertiary" aria-hidden="true" />
      <h1 className="text-xl font-semibold text-text-primary">
        {t('errors.notFound.title')}
      </h1>
      <p className="max-w-sm text-sm text-text-secondary">
        {t('errors.notFound.description')}
      </p>
      <Button asChild>
        <Link to="/dashboard">{t('errors.notFound.backToDashboard')}</Link>
      </Button>
    </div>
  );
}
