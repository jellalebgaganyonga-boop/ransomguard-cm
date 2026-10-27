import { describe, expect, it } from 'vitest';
import en from './locales/en.json';
import fr from './locales/fr.json';

/**
 * The console is bilingual. A key that exists in one language and not the other is a screen
 * that falls back to the other language mid-sentence, and nobody notices until it is on a
 * projector.
 *
 * This fails on a missing key in either direction, so adding an English string without its
 * French translation breaks the build rather than the demonstration.
 */

type Tree = { [key: string]: string | Tree };

function flatten(tree: Tree, prefix = ''): Map<string, string> {
  const out = new Map<string, string>();
  for (const [key, value] of Object.entries(tree)) {
    const path = prefix ? `${prefix}.${key}` : key;
    if (typeof value === 'string') out.set(path, value);
    else for (const [k, v] of flatten(value, path)) out.set(k, v);
  }
  return out;
}

const english = flatten(en as Tree);
const french = flatten(fr as Tree);

/**
 * Words that are the same in both languages. A test that demanded every string differ would be
 * wrong: "Agents", "Score", "Version", "CSV" are not translations waiting to happen.
 */
const SAME_IN_BOTH = new Set([
  'Agents', 'Score', 'Version', 'Options', 'Note', 'Action', 'Actions', 'Date', 'Messages',
  'Protection', 'Configuration', 'Documents', 'Important', 'Instructions', 'Transport',
  'Information', 'Support', 'Format', 'Interface', 'Documentation', 'Installation', 'Port',
  'RansomGuard-CM', 'Notifications', 'Email', 'E-mail', 'Info', 'AGENTS', 'Agent', 'Module',
  'Modules', 'FQDN', 'Heartbeat',
  // Units and words French spells the same way.
  'Page {page} / {totalPages}', '{minutes} min', '{hours} h {minutes}',
]);

describe('translations', () => {
  it('has every English key in French', () => {
    const missing = [...english.keys()].filter((k) => !french.has(k));
    expect(missing, `missing from fr.json: ${missing.join(', ')}`).toEqual([]);
  });

  it('has every French key in English', () => {
    const missing = [...french.keys()].filter((k) => !english.has(k));
    expect(missing, `missing from en.json: ${missing.join(', ')}`).toEqual([]);
  });

  it('leaves no English string sitting untranslated in the French file', () => {
    const untranslated = [...english.entries()]
      .filter(([key, value]) => {
        const other = french.get(key);
        if (other === undefined || other !== value) return false;
        if (value.length <= 3) return false;              // EN, FR, CSV, ID
        if (SAME_IN_BOTH.has(value)) return false;
        // Nothing but placeholders and punctuation: "{from} → {to}", "Page {page} / {totalPages}".
        // There is no word in it to translate.
        if (!/\p{L}/u.test(value.replace(/\{[^}]*\}/g, ''))) return false;
        return true;
      })
      .map(([key, value]) => `${key} = "${value}"`);

    expect(untranslated, `same text in both languages:\n  ${untranslated.join('\n  ')}`).toEqual([]);
  });

  it('keeps the same ICU placeholders in both languages', () => {
    // Only the argument names. A naive /\{[^}]+\}/ stops at the first closing brace, which in
    // "{count, plural, one {# alert} other {# alerts}}" is the one inside the plural form -- so
    // it reported two correct translations as mismatched the first time this test ran.
    const placeholders = (s: string) =>
      [...s.matchAll(/\{\s*([A-Za-z0-9_]+)/g)].map((m) => m[1]).sort().join(',');
    const mismatched = [...english.entries()]
      .filter(([key, value]) => {
        const other = french.get(key);
        return other !== undefined && placeholders(value) !== placeholders(other);
      })
      .map(([key]) => key);

    // A French string that drops {count} renders a sentence with a hole in it.
    expect(mismatched, `placeholders differ: ${mismatched.join(', ')}`).toEqual([]);
  });
});
