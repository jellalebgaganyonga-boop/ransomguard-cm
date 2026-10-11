/**
 * Agent posture — presentation only (R8).
 *
 * The verdict (protected / partial / unprotected / unknown) and its reason codes are computed by
 * the GRID (grid services/agent_posture.py), the one place the rule lives. This module only turns
 * them into words: product names, the banner sentence, translated codes. It never recomputes a
 * threshold.
 */

import type { TFunction } from 'i18next';
import type { AgentDetail, AgentItem, ModuleStatus, ProtectionState } from '@/api/agents';

// ── Product names: a closed table, an unknown code shown as is ──

const PRODUCT_NAMES: Record<string, string> = {
  SENTINEL: 'SENTINEL',
  ENTROPY: 'ENTROPY',
  GENEALOGY: 'GENEALOGY',
  USB_GUARD: 'USB GUARD',
  EXFIL_WATCH: 'EXFIL WATCH',
  ANTI_TAMPERING: 'ANTI-TAMPERING',
};

/** Display order of the six entries (the agent's ModuleCode.All). */
export const MODULE_DISPLAY_ORDER = Object.keys(PRODUCT_NAMES);

/** The product name ("EXFIL WATCH"), never the wire code; an unknown code verbatim, never blank. */
export function productName(code: string): string {
  return PRODUCT_NAMES[code] ?? code;
}

/** Modules in display order; codes the console does not know come last, as reported. */
export function orderedModules(modules: ModuleStatus[]): ModuleStatus[] {
  const rank = (code: string) => {
    const i = MODULE_DISPLAY_ORDER.indexOf(code);
    return i === -1 ? MODULE_DISPLAY_ORDER.length : i;
  };
  return [...modules].sort((a, b) => rank(a.code) - rank(b.code));
}

// ── Reason codes ──

export type ParsedReason =
  | { kind: 'module'; module: string; state: string; reason: string | null }
  | { kind: 'verdict'; code: string };

/** "<MODULE>:<state>[:<reason>]" or a verdict-level code ("signal_lost", ...). */
export function parseReason(code: string): ParsedReason {
  const parts = code.split(':');
  if (parts.length >= 2) {
    return { kind: 'module', module: parts[0]!, state: parts[1]!, reason: parts[2] ?? null };
  }
  return { kind: 'verdict', code };
}

/** "inactive" -> "inactif" (in a sentence); an unknown state verbatim. */
function stateInSentence(t: TFunction, state: string): string {
  return t(`posture.stateInSentence.${state}`, state);
}

/** A reason code -> its label ("droits administrateur manquants"); an unknown code verbatim. */
export function reasonLabel(t: TFunction, reason: string): string {
  return t(`posture.reason.${reason}`, reason);
}

export interface ReasonContext {
  verdict: ProtectionState;
  signalAgeSeconds: number | null;
  rejectedAlerts?: number | null;
}

/** One finding as a sentence: "EXFIL WATCH inactif : droits administrateur manquants". */
export function reasonSentence(t: TFunction, code: string, ctx: ReasonContext): string {
  const parsed = parseReason(code);
  if (parsed.kind === 'module') {
    const module = productName(parsed.module);
    const state = stateInSentence(t, parsed.state);
    return parsed.reason
      ? t('posture.sentence.moduleWithReason', { module, state, reason: reasonLabel(t, parsed.reason) })
      : t('posture.sentence.module', { module, state });
  }

  switch (parsed.code) {
    case 'signal_lost':
      return ctx.verdict === 'unprotected'
        ? t('posture.sentence.noSignalFor', { duration: formatDuration(t, ctx.signalAgeSeconds) })
        : t('posture.sentence.signalLost', { minutes: Math.floor((ctx.signalAgeSeconds ?? 0) / 60) });
    case 'alerts_rejected':
      return ctx.rejectedAlerts != null
        ? t('posture.sentence.alertsRejectedCount', { count: ctx.rejectedAlerts })
        : t('posture.sentence.alertsRejected');
    default:
      return t(`posture.verdictReason.${parsed.code}`, parsed.code);
  }
}

/**
 * The banner sentence (LOT2_DECISIONS_ET_CORRECTIFS.md, decision 1):
 *   protected   -> "Protégé"
 *   partial     -> "Protection partielle — EXFIL WATCH inactif : droits administrateur manquants"
 *                  "Signal perdu depuis 12 min"
 *   unprotected -> "Non protégé — SENTINEL inactif : arrêt inattendu"
 *                  "Non protégé — aucun signal depuis 2 h 05"
 *   unknown     -> "Jamais connecté" / "Agent à mettre à jour — posture non transmise" /
 *                  "Démarrage en cours"
 */
export function verdictSentence(t: TFunction, agent: AgentItem | AgentDetail): string {
  const state = agent.protection_state;
  const reasons = agent.reason_codes;
  const ctx: ReasonContext = {
    verdict: state,
    signalAgeSeconds: agent.signal_age_seconds,
    rejectedAlerts: 'posture' in agent ? agent.posture?.rejected_alerts ?? null : null,
  };

  if (state === 'protected') {
    return t('posture.verdict.protected');
  }
  if (state === 'unknown') {
    const code = reasons.includes('starting') ? 'starting' : reasons[0] ?? 'never_connected';
    return reasonSentence(t, code, ctx);
  }

  const first = reasons[0];
  if (!first) {
    return t(`posture.verdict.${state}`);
  }
  if (state === 'partial' && first === 'signal_lost') {
    return reasonSentence(t, first, ctx);
  }
  return t('posture.sentence.verdictWithReason', {
    verdict: t(`posture.verdict.${state}`),
    reason: reasonSentence(t, first, ctx),
  });
}

/** Every finding after the first, as sentences (the banner shows the first). */
export function otherFindings(t: TFunction, agent: AgentItem | AgentDetail): string[] {
  const ctx: ReasonContext = {
    verdict: agent.protection_state,
    signalAgeSeconds: agent.signal_age_seconds,
    rejectedAlerts: 'posture' in agent ? agent.posture?.rejected_alerts ?? null : null,
  };
  const reasons = agent.reason_codes.filter(
    // "starting" is the headline of a grey verdict; its per-module lines say which modules.
    (code) => !(agent.protection_state === 'unknown' && code === 'starting'),
  );
  const rest = agent.protection_state === 'unknown' ? reasons : reasons.slice(1);
  return rest.map((code) => reasonSentence(t, code, ctx));
}

// ── Transmission errors: a stable code, translated; the raw code stays in a tooltip ──

export function uploadErrorLabel(t: TFunction, code: string): string {
  if (code === 'NETWORK_UNREACHABLE' || code === 'TIMEOUT' || code === 'UNKNOWN') {
    return t(`posture.uploadError.${code}`);
  }
  if (code === 'HTTP_401' || code === 'HTTP_403') {
    return t('posture.uploadError.auth');
  }
  if (/^HTTP_4\d\d$/.test(code)) {
    return t('posture.uploadError.http4xx');
  }
  if (/^HTTP_5\d\d$/.test(code)) {
    return t('posture.uploadError.http5xx');
  }
  return t('posture.uploadError.UNKNOWN');
}

// ── Numbers: null is "unavailable", never 0 ──

export function formatBytes(t: TFunction, bytes: number | null): string {
  if (bytes == null) {
    return t('posture.unavailable');
  }
  // "5,0 Go" in French, "5.0 GB" in English.
  const number = new Intl.NumberFormat(t('posture.locale'), { minimumFractionDigits: 1, maximumFractionDigits: 1 });
  const gib = bytes / 1024 ** 3;
  if (gib >= 1) {
    return t('posture.bytes.gib', { value: number.format(gib) });
  }
  return t('posture.bytes.mib', { value: Math.round(bytes / 1024 ** 2) });
}

export function formatCount(t: TFunction, value: number | null): string {
  return value == null ? t('posture.unavailable') : String(value);
}

/** "2 h 05", "3 j 4 h", "45 min" — for ages measured by the server. */
export function formatDuration(t: TFunction, seconds: number | null): string {
  if (seconds == null) {
    return t('posture.unavailable');
  }
  const minutes = Math.floor(seconds / 60);
  if (minutes < 60) {
    return t('posture.duration.minutes', { minutes });
  }
  const hours = Math.floor(minutes / 60);
  if (hours < 24) {
    return t('posture.duration.hours', { hours, minutes: String(minutes % 60).padStart(2, '0') });
  }
  return t('posture.duration.days', { days: Math.floor(hours / 24), hours: hours % 24 });
}
