/**
 * The console is English. Nothing French may reach a user unless they deliberately switch
 * language (owner's decision, 2026-10-25).
 *
 * The risk this guards is specific. Every `t('key', 'default')` carries a default, and that
 * default is what React renders when the key is missing from the loaded translations. Those
 * defaults were written in French, so they shipped inside the English bundle and would have
 * surfaced in an English session the moment a key went missing -- the one moment nobody is
 * watching. The same goes for a label typed straight into JSX, which never passes through i18n
 * at all.
 *
 * So this reads the source rather than the rendered output: a French default is invisible until
 * something breaks, and by then it is in front of a jury.
 *
 * fr.json is exempt, obviously, and so is the French language's own name: a language is called
 * what it calls itself, in any interface.
 */

import { describe, expect, it } from 'vitest';
import { readFileSync, readdirSync, statSync } from 'node:fs';
import { join, relative } from 'node:path';

// vitest runs from dashboard/, and import.meta.url is not a file URL under this config.
const SRC = join(process.cwd(), 'src');

const ACCENTS = /[àâäçéèêëîïôöûùüÿœÀÂÄÇÉÈÊËÎÏÔÖÛÙÜŸŒ]/;

/** French words with no English meaning. Short on purpose: a list that cries wolf is ignored. */
const FRENCH_WORDS =
  /\b(aucun|aucune|veuillez|introuvable|utilisateur|utilisateurs|fichier|fichiers|votre|vous|nouveau|nouvelle|chargement|connexion|enregistrer|annuler|rechercher|afficher|masquer|fermer|ouvrir|essayer|plusieurs|processus|modifier)\b/i;

/** The French language's own name, which is allowed to be French. */
const ENDONYM = /Français/g;

function sourceFiles(dir: string): string[] {
  const found: string[] = [];
  for (const entry of readdirSync(dir)) {
    const full = join(dir, entry);
    if (statSync(full).isDirectory()) {
      if (entry === 'locales' || entry === 'node_modules') continue;
      found.push(...sourceFiles(full));
      continue;
    }
    if (!/\.(ts|tsx)$/.test(entry)) continue;
    if (/\.test\.(ts|tsx)$/.test(entry)) continue;
    if (entry.endsWith('.d.ts')) continue;
    found.push(full);
  }
  return found;
}

/** `t('key', 'default')` and `t("key", "default")`: only the delimiter ends the default. */
const T_CALL =
  /t\(\s*(['"])[A-Za-z0-9_.]+\1\s*,\s*(['"])((?:\\.|(?!\2)[^\\])*)\2/gs;

/** A `defaultValue:` written as a template literal, which t() renders the same way. */
const DEFAULT_VALUE = /defaultValue:\s*`([^`]*)`/gs;

function isFrench(text: string): boolean {
  const withoutEndonym = text.replace(ENDONYM, '');
  return ACCENTS.test(withoutEndonym) || FRENCH_WORDS.test(withoutEndonym);
}

describe('the console shows no French of its own', () => {
  const files = sourceFiles(SRC);

  it('has source files to check at all', () => {
    // A traversal that quietly finds nothing would make every test below vacuously pass.
    expect(files.length).toBeGreaterThan(50);
  });

  it('gives every t() default an English string', () => {
    const offenders: string[] = [];

    for (const file of files) {
      const text = readFileSync(file, 'utf8');
      for (const match of text.matchAll(T_CALL)) {
        const fallback = match[3] ?? '';
        if (isFrench(fallback)) {
          offenders.push(`${relative(SRC, file)}: ${fallback.slice(0, 70)}`);
        }
      }
      for (const match of text.matchAll(DEFAULT_VALUE)) {
        const fallback = match[1] ?? '';
        if (isFrench(fallback)) {
          offenders.push(`${relative(SRC, file)}: ${fallback.slice(0, 70)}`);
        }
      }
    }

    expect(offenders, `French defaults would show in an English session:\n${offenders.join('\n')}`)
      .toEqual([]);
  });

  it('puts no French text straight into JSX', () => {
    const offenders: string[] = [];
    // Text between tags, with no braces in it: a literal somebody typed rather than translated.
    const jsxText = /&gt;([^<>{}\n]{3,})</g;

    for (const file of files.filter((f) => f.endsWith('.tsx'))) {
      const text = readFileSync(file, 'utf8');
      for (const match of text.replace(/>/g, '&gt;').matchAll(jsxText)) {
        const literal = (match[1] ?? '').trim();
        if (literal && isFrench(literal)) {
          offenders.push(`${relative(SRC, file)}: ${literal.slice(0, 70)}`);
        }
      }
    }

    expect(offenders, `French typed straight into JSX:\n${offenders.join('\n')}`).toEqual([]);
  });

  it('still catches French when it comes back', () => {
    // The checks above pass on an empty list, which is also what a broken matcher returns. These
    // say the matchers actually recognise the thing they are looking for.
    expect(isFrench('Aucun agent enregistré')).toBe(true);
    expect(isFrench("La commande n'a pas pu être envoyée")).toBe(true);
    expect(isFrench('No agent registered')).toBe(false);
    expect(isFrench('Français')).toBe(false);
  });
});
