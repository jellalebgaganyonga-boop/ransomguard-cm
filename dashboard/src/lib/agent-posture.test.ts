import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import i18n from '@/i18n';
import type { AgentDetail, AgentItem } from '@/api/agents';
import {
  formatBytes,
  formatCount,
  formatDuration,
  orderedModules,
  otherFindings,
  productName,
  uploadErrorLabel,
  verdictSentence,
} from './agent-posture';

const t = i18n.t.bind(i18n);

function agent(overrides: Partial<AgentItem> = {}): AgentItem {
  return {
    id: 'a1',
    hostname: 'DESKTOP-NODK1GK',
    fqdn: 'desktop-nodk1gk.local',
    os_version: '10.0.26300',
    agent_version: '0.4.0',
    status: 'active',
    last_heartbeat_at: new Date().toISOString(),
    protection_state: 'protected',
    reason_codes: [],
    signal_age_seconds: 30,
    ...overrides,
  };
}

describe('verdictSentence — the banner, one row of the table at a time (FR)', () => {
  beforeEach(async () => {
    await i18n.changeLanguage('fr');
  });

  it.each([
    ['protected', [], 30, 'Protégé'],
    // The criterion of LOT 2, scenario 2, word for word.
    ['partial', ['EXFIL_WATCH:inactive:admin_rights_missing'], 30,
      'Protection partielle — EXFIL WATCH inactif : droits administrateur manquants'],
    ['partial', ['ENTROPY:inactive:stopped_unexpectedly'], 30, 'Protection partielle — ENTROPY inactif : arrêt inattendu'],
    ['partial', ['SENTINEL:degraded:integrity_check_failing'], 30,
      "Protection partielle — SENTINEL dégradé : vérification d'intégrité en échec"],
    ['partial', ['USB_GUARD:disabled_by_config'], 30, 'Protection partielle — USB GUARD désactivé par la configuration'],
    ['partial', ['transmission_blocked'], 30, 'Protection partielle — transmission des alertes bloquée'],
    ['partial', ['transmission_unknown'], 30, 'Protection partielle — État de la transmission inconnu'],
    ['partial', ['restart_loop'], 30, "Protection partielle — Redémarrages répétés de l'agent"],
    ['partial', ['signal_lost'], 12 * 60 + 5, 'Signal perdu depuis 12 min'],
    ['unprotected', ['SENTINEL:inactive:no_watch_directory'], 30, 'Non protégé — SENTINEL inactif : aucun dossier surveillé'],
    ['unprotected', ['SENTINEL:inactive:not_reported'], 30, "Non protégé — SENTINEL inactif : non transmis par l'agent"],
    ['unprotected', ['signal_lost'], 2 * 3600 + 5 * 60, 'Non protégé — aucun signal depuis 2 h 05'],
    ['unprotected', ['no_detector_active'], 30, 'Non protégé — aucun détecteur actif'],
    ['unknown', ['never_connected'], null, 'Jamais connecté'],
    ['unknown', ['agent_update_required'], 30, 'Agent à mettre à jour — posture non transmise'],
    ['unknown', ['SENTINEL:starting', 'starting'], 30, 'Démarrage en cours'],
  ] as const)('%s %j -> %s', (state, reasons, age, expected) => {
    expect(
      verdictSentence(t, agent({ protection_state: state, reason_codes: [...reasons], signal_age_seconds: age })),
    ).toBe(expected);
  });

  it('counts the rejected alerts when the posture carries them', () => {
    const detail: AgentDetail = {
      ...agent({ protection_state: 'partial', reason_codes: ['alerts_rejected'] }),
      posture: null,
    };
    expect(verdictSentence(t, detail)).toBe('Protection partielle — alertes rejetées par le serveur');
  });
});

describe('verdictSentence (EN)', () => {
  beforeEach(async () => {
    await i18n.changeLanguage('en');
  });
  afterEach(async () => {
    await i18n.changeLanguage('fr');
  });

  it('composes the same sentence in English', () => {
    expect(
      verdictSentence(t, agent({ protection_state: 'partial', reason_codes: ['EXFIL_WATCH:inactive:admin_rights_missing'] })),
    ).toBe('Partial protection — EXFIL WATCH inactive: administrator rights missing');
  });

  it('restart loop, red', () => {
    expect(
      verdictSentence(t, agent({
        protection_state: 'unprotected',
        reason_codes: ['SENTINEL:inactive:init_failed', 'restart_loop'],
      })),
    ).toBe('Not protected — SENTINEL inactive: failed to start');
  });
});

describe('otherFindings — every reason after the headline', () => {
  beforeEach(async () => {
    await i18n.changeLanguage('fr');
  });

  it('lists the rest, worst first, as sentences', () => {
    expect(
      otherFindings(t, agent({
        protection_state: 'unprotected',
        reason_codes: ['SENTINEL:inactive:init_failed', 'restart_loop', 'EXFIL_WATCH:inactive:admin_rights_missing'],
      })),
    ).toEqual(["Redémarrages répétés de l'agent", 'EXFIL WATCH inactif : droits administrateur manquants']);
  });

  it('for a grey "starting" verdict, names the starting modules', () => {
    expect(
      otherFindings(t, agent({ protection_state: 'unknown', reason_codes: ['SENTINEL:starting', 'ENTROPY:starting', 'starting'] })),
    ).toEqual(['SENTINEL en démarrage', 'ENTROPY en démarrage']);
  });
});

describe('product names', () => {
  it.each([
    ['USB_GUARD', 'USB GUARD'],
    ['EXFIL_WATCH', 'EXFIL WATCH'],
    ['ANTI_TAMPERING', 'ANTI-TAMPERING'],
    ['SENTINEL', 'SENTINEL'],
    ['MOON_LASER', 'MOON_LASER'],
  ])('%s -> %s (an unknown code verbatim, never blank)', (code, name) => {
    expect(productName(code)).toBe(name);
  });

  it('orders the entries, unknown codes last, and drops ANTI_TAMPERING', () => {
    const codes = orderedModules(
      ['MOON_LASER', 'ANTI_TAMPERING', 'EXFIL_WATCH', 'SENTINEL', 'GENEALOGY', 'USB_GUARD', 'ENTROPY'].map((code) => ({
        code, state: 'active', reason_code: null, changed_at: '2026-10-11T10:00:00Z',
      })),
    ).map((m) => m.code);

    // ANTI_TAMPERING is not shown: it was a row on every workstation that always read
    // "not implemented". An agent older than 2026-10-26 still sends it, which is why this is
    // filtered here and not only at the agent. MOON_LASER stays: a code the console does not
    // know is still something happening on somebody's machine, and is shown as reported.
    expect(codes).toEqual(['SENTINEL', 'ENTROPY', 'GENEALOGY', 'USB_GUARD', 'EXFIL_WATCH', 'MOON_LASER']);
  });
});

describe('transmission error codes, translated (the raw code stays in a tooltip)', () => {
  beforeEach(async () => {
    await i18n.changeLanguage('fr');
  });

  it.each([
    ['NETWORK_UNREACHABLE', 'Serveur injoignable'],
    ['TIMEOUT', 'Délai dépassé'],
    ['HTTP_401', 'Authentification refusée'],
    ['HTTP_403', 'Authentification refusée'],
    ['HTTP_422', 'Rejeté par le serveur'],
    ['HTTP_400', 'Rejeté par le serveur'],
    ['HTTP_503', 'Erreur du serveur'],
    ['UNKNOWN', 'Erreur inconnue'],
    ['SOMETHING_NEW', 'Erreur inconnue'],
  ])('%s -> %s', (code, label) => {
    expect(uploadErrorLabel(t, code)).toBe(label);
  });
});

describe('null is "indisponible", never 0', () => {
  beforeEach(async () => {
    await i18n.changeLanguage('fr');
  });

  it('counts, bytes and durations', () => {
    expect(formatCount(t, null)).toBe('indisponible');
    expect(formatCount(t, 0)).toBe('0');
    expect(formatBytes(t, null)).toBe('indisponible');
    expect(formatBytes(t, 5 * 1024 ** 3)).toBe('5,0 Go');
    expect(formatDuration(t, null)).toBe('indisponible');
    expect(formatDuration(t, 45 * 60)).toBe('45 min');
    expect(formatDuration(t, 3 * 86400 + 4 * 3600)).toBe('3 j 4 h');
  });
});

describe('every code the GRID can send has a French and an English label', () => {
  // grid services/agent_posture.py: REASON_CODES, the verdict-level codes, MODULE_STATES.
  const reasons = [
    'admin_rights_missing', 'disabled_by_config', 'init_failed', 'stopped_unexpectedly', 'not_implemented',
    'attribution_unreliable', 'actions_not_implemented', 'starting', 'no_watch_directory',
    'integrity_check_failing', 'not_reported', 'etw_session_stopped_externally', 'learning_phase',
  ];
  const verdictReasons = [
    'never_connected', 'agent_update_required', 'starting', 'no_detector_active', 'transmission_blocked',
    'transmission_unknown', 'restart_loop', 'audit_write_failed',
  ];
  const states = ['active', 'inactive', 'disabled_by_config', 'degraded'];

  it.each(['fr', 'en'])('%s', async (lang) => {
    await i18n.changeLanguage(lang);
    for (const r of reasons) expect(i18n.exists(`posture.reason.${r}`), r).toBe(true);
    for (const r of verdictReasons) expect(i18n.exists(`posture.verdictReason.${r}`), r).toBe(true);
    for (const s of states) {
      expect(i18n.exists(`posture.stateLabel.${s}`), s).toBe(true);
      expect(i18n.exists(`posture.stateInSentence.${s}`), s).toBe(true);
    }
    for (const v of ['protected', 'partial', 'unprotected', 'unknown']) {
      expect(i18n.exists(`posture.verdict.${v}`), v).toBe(true);
      expect(i18n.exists(`posture.verdictShort.${v}`), v).toBe(true);
    }
  });
});
