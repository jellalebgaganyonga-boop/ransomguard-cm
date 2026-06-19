import { describe, it, expect } from 'vitest';
import { z } from 'zod';

/**
 * These schemas mirror the inline validation in acknowledge-modal.tsx
 * and close-modal.tsx. Testing them in isolation catches off-by-one
 * errors at the exact boundary values specified in the PRD.
 */

// AC3.3.1: note facultative, 0-500 chars
const ackNoteSchema = z.string().max(500);

// AC3.4.1: notes obligatoires, 20-2000 chars
const closeNotesSchema = z.string().min(20).max(2000);

describe('Acknowledge note validation (AC3.3.1)', () => {
  it('accepts empty string (note is optional)', () => {
    expect(ackNoteSchema.safeParse('').success).toBe(true);
  });

  it('accepts exactly 500 chars', () => {
    expect(ackNoteSchema.safeParse('a'.repeat(500)).success).toBe(true);
  });

  it('rejects 501 chars', () => {
    expect(ackNoteSchema.safeParse('a'.repeat(501)).success).toBe(false);
  });
});

describe('Close resolution notes validation (AC3.4.1)', () => {
  it('rejects empty string (notes are required)', () => {
    expect(closeNotesSchema.safeParse('').success).toBe(false);
  });

  it('rejects 19 chars (one below minimum)', () => {
    expect(closeNotesSchema.safeParse('a'.repeat(19)).success).toBe(false);
  });

  it('accepts exactly 20 chars (minimum boundary)', () => {
    expect(closeNotesSchema.safeParse('a'.repeat(20)).success).toBe(true);
  });

  it('accepts exactly 2000 chars (maximum boundary)', () => {
    expect(closeNotesSchema.safeParse('a'.repeat(2000)).success).toBe(true);
  });

  it('rejects 2001 chars (one above maximum)', () => {
    expect(closeNotesSchema.safeParse('a'.repeat(2001)).success).toBe(false);
  });
});

// ── formatClosingNote (AC3.4.1: "<category>: <notes>") ──────

import { formatClosingNote } from '@/api/alerts';

describe('formatClosingNote (AC3.4.1 format)', () => {
  it('formats false_positive with French label', () => {
    expect(formatClosingNote('false_positive', 'Test légitime confirmé')).toBe(
      'Faux positif: Test légitime confirmé'
    );
  });

  it('formats true_positive_contained', () => {
    expect(
      formatClosingNote('true_positive_contained', 'Ransomware isolé et nettoyé')
    ).toBe('Vrai positif — Contenu: Ransomware isolé et nettoyé');
  });

  it('formats true_positive_escalated', () => {
    expect(formatClosingNote('true_positive_escalated', 'Escaladé au CERT')).toBe(
      'Vrai positif — Escaladé: Escaladé au CERT'
    );
  });

  it('formats inconclusive', () => {
    expect(formatClosingNote('inconclusive', 'Preuves insuffisantes')).toBe(
      'Non concluant: Preuves insuffisantes'
    );
  });
});
