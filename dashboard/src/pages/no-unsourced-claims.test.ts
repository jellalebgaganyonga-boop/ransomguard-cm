import { readFileSync, readdirSync, statSync } from 'node:fs';
import { join, relative } from 'node:path';
import { describe, expect, it } from 'vitest';

/**
 * An honest screen: no compliance verdict and no percentage is written by hand into what the
 * console renders. The read-only and executive dashboards used to show "CONFORMITÉ ACTUELLE :
 * 98%" and "Loi 2024/017 : Conforme" with nothing behind them (INVENTAIRE_CONSOLE.md, section 3).
 * Such a figure must come from the API; until a real computation exists, it does not appear.
 */

const ROOT = join(__dirname, '..');
const PAGES = join(ROOT, 'pages');
const LOCALES = [join(ROOT, 'i18n', 'locales', 'fr.json'), join(ROOT, 'i18n', 'locales', 'en.json')];

const PERCENT = /\d{1,3}\s?%/;
const VERDICT = /\b(conforme|compliant)\b/i;

function sources(dir: string): string[] {
  return readdirSync(dir).flatMap((name) => {
    const path = join(dir, name);
    if (statSync(path).isDirectory()) return sources(path);
    return /\.tsx?$/.test(name) && !/\.test\.tsx?$/.test(name) ? [path] : [];
  });
}

/** Comments explain; they are not rendered. */
function withoutComments(code: string): string {
  return code.replace(/\{\s*\/\*[\s\S]*?\*\/\s*\}/g, '').replace(/\/\*[\s\S]*?\*\//g, '').replace(/^\s*\/\/.*$/gm, '');
}

function findings(text: string, file: string): string[] {
  return text.split('\n').flatMap((line, i) =>
    PERCENT.test(line) || VERDICT.test(line) ? [`${relative(ROOT, file)}:${i + 1}: ${line.trim()}`] : [],
  );
}

describe('no unsourced claim is rendered', () => {
  it('src/pages writes no percentage and no "Conforme" / "Compliant" by hand', () => {
    const found = sources(PAGES).flatMap((file) => findings(withoutComments(readFileSync(file, 'utf8')), file));
    expect(found).toEqual([]);
  });

  it('the FR and EN locales carry no percentage and no compliance verdict', () => {
    const found = LOCALES.flatMap((file) => findings(readFileSync(file, 'utf8'), file));
    expect(found).toEqual([]);
  });

  it('would catch the claims that were removed', () => {
    expect(findings('<p>CONFORMITÉ ACTUELLE : 98%</p>', 'x.tsx')).toHaveLength(1);
    expect(findings('<span>✅ Conforme</span>', 'x.tsx')).toHaveLength(1);
    expect(findings('"law": "Law 2024/017: Compliant"', 'en.json')).toHaveLength(1);
  });
});
