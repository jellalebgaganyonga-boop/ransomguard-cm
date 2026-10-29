import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { describe, expect, it } from 'vitest';

/**
 * Every badge's text is readable on its own background, in both themes.
 *
 * The MEDIUM badge was an empty yellow box in dark mode: its text colour was written in the
 * component as the reference shade yellow-20, and in dark mode yellow-20 is also what
 * --sys-color-severity-medium-subtle resolves to. The word was painted in its own background
 * colour. A colour chosen inside a component cannot follow the theme, so this test reads the
 * token files and checks the pairs the badge actually uses.
 *
 * WCAG AA for normal text is 4.5:1.
 */

const styles = join(process.cwd(), 'src', 'styles');
const tokens = readFileSync(join(styles, 'tokens.css'), 'utf8');
const darkOverrides = readFileSync(join(styles, 'globals.css'), 'utf8');

const PALETTE = new Map(
  [...tokens.matchAll(/--ref-palette-([a-z]+-\d+):\s*(#[0-9A-Fa-f]{6})/g)].map((m) => [m[1]!, m[2]!]),
);

/** The last definition wins, which is how the cascade reads them. */
function resolve(css: string, token: string): string {
  const prefix = `--${token}:`;
  let shade: string | undefined;

  for (const line of css.split('\n')) {
    const trimmed = line.trim();
    if (!trimmed.startsWith(prefix)) continue;
    const value = trimmed.slice(prefix.length).trim();
    const match = /^var\(--ref-palette-([a-z]+-\d+)\)/.exec(value);
    if (match) shade = match[1];
  }

  if (!shade) throw new Error(`token --${token} is not defined`);
  const hex = PALETTE.get(shade);
  if (!hex) throw new Error(`--${token} points at --ref-palette-${shade}, which does not exist`);
  return hex;
}

function luminance(hex: string): number {
  const channels = [1, 3, 5].map((i) => parseInt(hex.slice(i, i + 2), 16) / 255);
  const linear = channels.map((c) => (c <= 0.03928 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4));
  return 0.2126 * linear[0]! + 0.7152 * linear[1]! + 0.0722 * linear[2]!;
}

function contrast(a: string, b: string): number {
  const [hi, lo] = [luminance(a), luminance(b)].sort((x, y) => y - x) as [number, number];
  return (hi + 0.05) / (lo + 0.05);
}

/** Exactly the pairs badge.tsx puts together. */
const BADGES = [
  ['critical', 'sys-color-severity-critical-subtle', 'sys-color-severity-critical-on-subtle'],
  ['high', 'sys-color-severity-high-subtle', 'sys-color-severity-high-on-subtle'],
  ['medium', 'sys-color-severity-medium-subtle', 'sys-color-severity-medium-on-subtle'],
  ['low', 'sys-color-severity-low-subtle', 'sys-color-severity-low-on-subtle'],
  ['info', 'sys-color-severity-info-subtle', 'sys-color-severity-info-on-subtle'],
  ['success', 'sys-color-feedback-success-subtle', 'sys-color-feedback-success-on-subtle'],
] as const;

describe.each([
  ['light', tokens],
  // globals.css holds the html.dark overrides; a token it does not override keeps its light
  // value, which is what the browser does too.
  ['dark', `${tokens}\n${darkOverrides}`],
])('badge contrast in %s mode', (_mode, css) => {
  it.each(BADGES)('%s text is readable on its own background', (_name, bgToken, fgToken) => {
    expect(contrast(resolve(css, bgToken), resolve(css, fgToken))).toBeGreaterThanOrEqual(4.5);
  });

  it.each(BADGES)('%s text is not simply its own background', (_name, bgToken, fgToken) => {
    // The exact shape of the bug: the two resolved to the same colour.
    expect(resolve(css, fgToken)).not.toBe(resolve(css, bgToken));
  });
});

describe('badge.tsx', () => {
  const source = readFileSync(join(process.cwd(), 'src', 'components', 'ui', 'badge.tsx'), 'utf8');

  it('picks no colour of its own', () => {
    // This is the bug itself. A reference shade written into the component is fixed for both
    // themes, so it cannot be right in both -- and yellow-20 text happened to be exactly the
    // dark-mode background it was sitting on. The token files decide colours; the component
    // names a role.
    expect(source).not.toMatch(/--ref-palette-/);
  });

  it.each(BADGES.map(([name]) => name))('gives the %s badge an -on-subtle text token', (name) => {
    const role = name === 'success' ? 'success' : `severity-${name}`;
    expect(source).toContain(`text-${role}-on-subtle`);
  });
});
