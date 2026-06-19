/**
 * CloseModal — AC3.4.1
 *
 *   GIVEN an alert with status="acknowledged"
 *   WHEN the user clicks "Close"
 *   THEN a modal prompts for:
 *     - Resolution category (dropdown, REQUIRED): false_positive |
 *       true_positive_contained | true_positive_escalated | inconclusive
 *     - Resolution notes (text, REQUIRED, 20-2000 chars)
 *   AND upon submission: POST .../status { status: "closed", note: "<category>: <notes>" }
 */

import { useState, useEffect } from 'react';
import { useTranslation } from 'react-i18next';
import { z } from 'zod';
import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
  DialogFooter,
} from '@/components/ui/dialog';
import {
  Select,
  SelectTrigger,
  SelectValue,
  SelectContent,
  SelectItem,
} from '@/components/ui/select';
import { Textarea } from '@/components/ui/textarea';
import { Label } from '@/components/ui/label';
import { Button } from '@/components/ui/button';
import { useUpdateAlertStatus } from '@/hooks/use-alerts';
import { formatClosingNote, type ResolutionCategory } from '@/api/alerts';

const notesSchema = z.string().min(20).max(2000);

interface CloseModalProps {
  alertId: string;
  open: boolean;
  onOpenChange: (open: boolean) => void;
  /** Pre-select a resolution category (e.g., "Mark as false positive" shortcut). */
  preselectCategory?: ResolutionCategory;
}

const CATEGORIES: { value: ResolutionCategory; labelKey: string; fallback: string }[] = [
  { value: 'false_positive', labelKey: 'alerts.closeModal.category.fp', fallback: 'Faux positif' },
  {
    value: 'true_positive_contained',
    labelKey: 'alerts.closeModal.category.tpContained',
    fallback: 'Vrai positif — Contenu',
  },
  {
    value: 'true_positive_escalated',
    labelKey: 'alerts.closeModal.category.tpEscalated',
    fallback: 'Vrai positif — Escaladé',
  },
  {
    value: 'inconclusive',
    labelKey: 'alerts.closeModal.category.inconclusive',
    fallback: 'Non concluant',
  },
];

export function CloseModal({ alertId, open, onOpenChange, preselectCategory }: CloseModalProps) {
  const { t } = useTranslation();
  const [category, setCategory] = useState<ResolutionCategory | ''>(preselectCategory ?? '');
  const [notes, setNotes] = useState('');
  const mutation = useUpdateAlertStatus(alertId);

  // Re-apply preselection if the modal is reopened with a different shortcut
  // (e.g., user closes via "Mark FP" after having opened via plain "Close").
  useEffect(() => {
    if (open && preselectCategory) {
      setCategory(preselectCategory);
    }
  }, [open, preselectCategory]);

  const notesValid = notesSchema.safeParse(notes).success;
  const categoryValid = category !== '';
  const formValid = notesValid && categoryValid;

  const handleConfirm = async () => {
    // Narrowing category out of '' before passing to formatClosingNote
    if (category === '' || !notesValid) return;
    await mutation.mutateAsync({
      status: 'closed',
      note: formatClosingNote(category, notes),
    });
    setCategory('');
    setNotes('');
    onOpenChange(false);
  };

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{t('alerts.closeModal.title', "Fermer l'incident")}</DialogTitle>
        </DialogHeader>

        <div className="flex flex-col gap-4 p-5">
          <div>
            <Label htmlFor="close-category">
              {t('alerts.closeModal.categoryLabel', 'Catégorie de résolution')}{' '}
              <span className="text-error">*</span>
            </Label>
            <Select
              value={category}
              onValueChange={(v) => setCategory(v as ResolutionCategory)}
            >
              <SelectTrigger id="close-category" hasError={!categoryValid && category !== ''} className="mt-1.5">
                <SelectValue
                  placeholder={t('alerts.closeModal.categoryPlaceholder', 'Sélectionner...')}
                />
              </SelectTrigger>
              <SelectContent>
                {CATEGORIES.map((c) => (
                  <SelectItem key={c.value} value={c.value}>
                    {t(c.labelKey, c.fallback)}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>

          <div>
            <Label htmlFor="close-notes">
              {t('alerts.closeModal.notesLabel', 'Notes de résolution')}{' '}
              <span className="text-error">*</span>{' '}
              <span className="font-normal text-text-tertiary">
                ({t('alerts.closeModal.notesHint', '20-2000 caractères')})
              </span>
            </Label>
            <Textarea
              id="close-notes"
              value={notes}
              onChange={(e) => setNotes(e.target.value)}
              placeholder={t(
                'alerts.closeModal.notesPlaceholder',
                "Détail de l'investigation et des actions prises..."
              )}
              hasError={notes.length > 0 && !notesValid}
              className="mt-1.5"
              maxLength={2000}
            />
            <div className="mt-1 flex justify-between text-xs">
              <span className={notes.length > 0 && notes.length < 20 ? 'text-warning' : 'text-text-tertiary'}>
                {notes.length < 20 && notes.length > 0
                  ? t('alerts.closeModal.minChars', { count: 20 - notes.length, defaultValue: `${20 - notes.length} caractères minimum` })
                  : ''}
              </span>
              <span className="text-text-tertiary">{notes.length} / 2000</span>
            </div>
          </div>
        </div>

        <DialogFooter>
          <Button variant="secondary" onClick={() => onOpenChange(false)}>
            {t('common.cancel', 'Annuler')}
          </Button>
          <Button
            variant="primary"
            onClick={handleConfirm}
            isLoading={mutation.isPending}
            disabled={!formValid}
          >
            {t('alerts.closeModal.submit', "Fermer l'incident")}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
