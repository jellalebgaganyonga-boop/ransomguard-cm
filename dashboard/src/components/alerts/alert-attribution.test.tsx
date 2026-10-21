import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { cleanup, render, screen } from '@testing-library/react';
import i18n from '@/i18n';
import type { AlertAttribution as Attribution } from '@/api/alerts';
import { PENDING_TIMEOUT_MS, attributionView } from '@/lib/alert-attribution';
import { AlertAttribution } from './alert-attribution';

/**
 * "Qui l'a fait ?" (ADR-025): the alert arrives first, its attribution afterwards. Pending, then
 * one of three final states; a wrong process is worse than none, so "nobody" always says why.
 */

const NOW = Date.parse('2026-10-21T10:00:00Z');

describe('attributionView', () => {
  it('no attribution on the alert: nothing to show', () => {
    expect(attributionView(null, '2026-10-21T09:59:00Z', NOW)).toBeNull();
    expect(attributionView(undefined, '2026-10-21T09:59:00Z', NOW)).toBeNull();
  });

  it('pending within 10 minutes of the detection: in progress', () => {
    expect(attributionView({ state: 'pending' }, '2026-10-21T09:51:00Z', NOW)).toEqual({ kind: 'pending' });
  });

  it('pending past 10 minutes: not received, never "in progress" forever', () => {
    const detected = new Date(NOW - PENDING_TIMEOUT_MS).toISOString();
    expect(attributionView({ state: 'pending' }, detected, NOW)).toEqual({ kind: 'not_received' });
  });

  it('a naive server timestamp is read as UTC', () => {
    expect(attributionView({ state: 'pending' }, '2026-10-21T09:45:00', NOW)).toEqual({ kind: 'not_received' });
  });

  it('resolved: the process, its PID, its start and its image', () => {
    expect(
      attributionView(
        { state: 'resolved', process_id: 4242, process_name: 'evil', process_path: 'C:\\evil.exe', process_created_at: '2026-10-21T09:57:00+00:00' },
        '2026-10-21T09:59:00Z',
        NOW,
      ),
    ).toEqual({ kind: 'resolved', processName: 'evil', processId: 4242, createdAt: '2026-10-21T09:57:00+00:00', imagePath: 'C:\\evil.exe' });
  });

  it('unavailable keeps its reason; an unknown state names nobody either', () => {
    expect(attributionView({ state: 'unavailable', reason: 'system_pid' }, '2026-10-21T09:59:00Z', NOW)).toEqual({
      kind: 'unavailable',
      reason: 'system_pid',
    });
    expect(attributionView({ state: 'something_new' }, '2026-10-21T09:59:00Z', NOW)).toEqual({ kind: 'unavailable', reason: null });
  });

  it('ambiguous names nobody', () => {
    expect(attributionView({ state: 'ambiguous' }, '2026-10-21T09:59:00Z', NOW)).toEqual({ kind: 'ambiguous' });
  });
});

describe('AlertAttribution (French)', () => {
  beforeEach(async () => {
    await i18n.changeLanguage('fr');
  });
  afterEach(cleanup);

  const show = (attribution: Attribution | null) =>
    render(<AlertAttribution attribution={attribution} detectedAt={new Date().toISOString()} />);

  it('pending', () => {
    show({ state: 'pending' });
    expect(screen.getByText("Qui l'a fait ?")).toBeTruthy();
    expect(screen.getByText('Attribution en cours…')).toBeTruthy();
  });

  it('resolved', () => {
    show({ state: 'resolved', process_id: 4242, process_name: 'evil', process_path: 'C:\\Temp\\evil.exe', process_created_at: '2026-10-21T09:57:00+00:00' });
    expect(screen.getByText('evil')).toBeTruthy();
    expect(screen.getByText('— PID 4242')).toBeTruthy();
    expect(screen.getByText('C:\\Temp\\evil.exe')).toBeTruthy();
    expect(screen.getByText(/^démarré le /)).toBeTruthy();
  });

  it.each([
    ['no_write_event', 'aucune écriture vue par la surveillance noyau'],
    ['system_pid', 'écriture faite par le système (écriture différée ou fichier mappé)'],
    ['pid_reused', 'le numéro de processus a été réattribué à un autre programme'],
    ['process_exited', "le processus s'est terminé avant d'être vérifié"],
    ['etw_unavailable', 'surveillance noyau indisponible sur ce poste'],
  ])('unavailable, %s, says why in plain words', (reason, label) => {
    show({ state: 'unavailable', reason });
    expect(screen.getByText('Processus non identifié')).toBeTruthy();
    expect(screen.getByText(`— ${label}`)).toBeTruthy();
  });

  it('ambiguous', () => {
    show({ state: 'ambiguous' });
    expect(screen.getByText("Plusieurs processus ont écrit ce fichier : aucun n'est désigné")).toBeTruthy();
  });

  it('no attribution: the section is absent', () => {
    const { container } = show(null);
    expect(container.querySelector('[data-testid="alert-attribution"]')).toBeNull();
  });
});

describe('every attribution label exists in French and English', () => {
  it.each(['fr', 'en'])('%s', async (lang) => {
    await i18n.changeLanguage(lang);
    for (const key of ['title', 'pending', 'notReceived', 'unnamed', 'startedAt', 'ambiguous', 'unavailable']) {
      expect(i18n.exists(`alerts.attribution.${key}`), key).toBe(true);
    }
    for (const reason of ['no_write_event', 'system_pid', 'pid_reused', 'process_exited', 'etw_unavailable']) {
      expect(i18n.exists(`alerts.attribution.reason.${reason}`), reason).toBe(true);
    }
  });
});
