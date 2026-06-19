/**
 * AcknowledgeModal — AC3.3.1
 *
 *   GIVEN an alert with status="new"
 *   WHEN the user clicks "Acknowledge"
 *   THEN a confirmation modal asks for an optional note (0-500 chars)
 *   AND upon confirmation, POST .../status { status: "acknowledged", note }
 */

import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { z } from 'zod';
import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
  DialogDescription,
  DialogFooter,
} from '@/components/ui/dialog';
import { Textarea } from '@/components/ui/textarea';
import { Label } from '@/components/ui/label';
import { Button } from '@/components/ui/button';
import { useUpdateAlertStatus } from '@/hooks/use-alerts';

const noteSchema = z.string().max(500);

interface AcknowledgeModalProps {
  alertId: string;
  open: boolean;
  onOpenChange: (open: boolean) => void;
}

export function AcknowledgeModal({ alertId, open, onOpenChange }: AcknowledgeModalProps) {
  const { t } = useTranslation();
  const [note, setNote] = useState('');
  const mutation = useUpdateAlertStatus(alertId);

  const noteValid = noteSchema.safeParse(note).success;

  const handleConfirm = async () => {
    if (!noteValid) return;
    const trimmed = note.trim();
    await mutation.mutateAsync(
      trimmed ? { status: 'acknowledged', note: trimmed } : { status: 'acknowledged' }
    );
    setNote('');
    onOpenChange(false);
  };

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>
            {t('alerts.ackModal.title', 'Prendre en charge cette alerte ?')}
          </DialogTitle>
          <DialogDescription>
            {t(
              'alerts.ackModal.description',
              'Vous allez vous assigner cette alerte.'
            )}
          </DialogDescription>
        </DialogHeader>

        <div className="p-5">
          <Label htmlFor="ack-note">
            {t('alerts.ackModal.noteLabel', 'Note (facultative, 0-500 caractères)')}
          </Label>
          <Textarea
            id="ack-note"
            value={note}
            onChange={(e) => setNote(e.target.value)}
            placeholder={t('alerts.ackModal.notePlaceholder', 'Investigation en cours...')}
            hasError={!noteValid}
            className="mt-1.5"
            maxLength={500}
          />
          <div className="mt-1 text-right text-xs text-text-tertiary">
            {note.length} / 500
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
            disabled={!noteValid}
          >
            {t('common.confirm', 'Confirmer')}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
