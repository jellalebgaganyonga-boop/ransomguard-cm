import { describe, it, expect } from 'vitest';
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { PROTECTION_BANNER_CLASS, agentStatusVariant, isSignalLostRed, protectionVariant } from './agent-status';
import tailwindConfig from '../../tailwind.config';
import { createElement } from 'react';
import { render } from '@testing-library/react';
import { Badge } from '@/components/ui/badge';

// ── agentStatusVariant ────────────────────────────────────────────────────────

describe('agentStatusVariant', () => {
  it('active → "low" (green badge)', () => {
    expect(agentStatusVariant('active')).toBe('low');
  });

  it('provisioned → "info" (blue badge)', () => {
    expect(agentStatusVariant('provisioned')).toBe('info');
  });

  it('disconnected → "high" (red badge)', () => {
    expect(agentStatusVariant('disconnected')).toBe('high');
  });

  it('decommissioned → "neutral" (gray badge)', () => {
    expect(agentStatusVariant('decommissioned')).toBe('neutral');
  });
});

// ── protectionVariant: the colour of the server's verdict, nothing recomputed ─

describe('protectionVariant', () => {
  it.each([
    ['protected', 'success'],
    ['partial', 'high'],
    ['unprotected', 'critical'],
    ['unknown', 'neutral'],
  ] as const)('%s → %s', (state, variant) => {
    expect(protectionVariant(state)).toBe(variant);
  });
});

// ── isSignalLostRed: the server's "no signal for more than 60 min", not a console threshold ─

describe('isSignalLostRed', () => {
  it('unprotected because of signal_lost → true', () => {
    expect(isSignalLostRed({ protection_state: 'unprotected', reason_codes: ['signal_lost'] })).toBe(true);
  });

  it('partial signal_lost (5 to 60 min) → false', () => {
    expect(isSignalLostRed({ protection_state: 'partial', reason_codes: ['signal_lost'] })).toBe(false);
  });

  it('unprotected for another reason → false', () => {
    expect(
      isSignalLostRed({ protection_state: 'unprotected', reason_codes: ['SENTINEL:inactive:init_failed'] }),
    ).toBe(false);
  });

  it('never connected → false (it never had a signal to lose)', () => {
    expect(isSignalLostRed({ protection_state: 'unknown', reason_codes: ['never_connected'] })).toBe(false);
  });
});

// ── The verdict colours, resolved down to the palette ─
// Class -> Tailwind colour -> --sys-* token -> --ref-palette-* colour. A class name alone proves
// nothing: "severity-low" read like green and was blue.

const tokensCss = readFileSync(resolve(__dirname, '../styles/tokens.css'), 'utf8');
const tailwindColors = (tailwindConfig.theme?.extend?.colors ?? {}) as Record<string, string>;

function paletteOf(utility: string): string {
  const key = utility.replace(/^(bg|border|text)-/, '');
  const arbitrary = /^\[color:var\((--ref-palette-[a-z]+)-\d+\)\]$/.exec(key);
  if (arbitrary) return arbitrary[1]!.replace('--ref-palette-', '');
  const variable = /var\((--[a-z0-9-]+)\)/.exec(tailwindColors[key] ?? '')?.[1];
  if (!variable) throw new Error(`no Tailwind colour for ${utility}`);
  const ref = new RegExp(`${variable}:\\s*var\\(--ref-palette-([a-z]+)-\\d+\\)`).exec(tokensCss)?.[1];
  if (!ref) throw new Error(`no palette for ${variable}`);
  return ref;
}

const isColour = (c: string) => {
  const key = c.replace(/^(bg|border|text)-/, '');
  return /^(bg|border|text)-/.test(c) && (key in tailwindColors || key.startsWith('[color:'));
};
const families = (classes: string) => classes.split(/\s+/).filter(isColour).map(paletteOf);

describe('the four verdicts in colour (decision 1 of LOT 2)', () => {
  it.each([
    ['protected', 'green'],
    ['partial', 'orange'],
    ['unprotected', 'red'],
    ['unknown', 'gray'],
  ] as const)('%s is %s — banner and badge', (state, colour) => {
    const banner = families(PROTECTION_BANNER_CLASS[state]);
    const { container } = render(createElement(Badge, { variant: protectionVariant(state) }, state));
    const badge = families(container.firstElementChild!.className);
    expect(new Set([...banner, ...badge])).toEqual(new Set([colour]));
  });
});
