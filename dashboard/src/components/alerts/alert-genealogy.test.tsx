/**
 * The chain of processes behind a write, as the console draws it.
 *
 * Two things matter and both are easy to get wrong. The order: the agent sends the ancestors
 * nearest-first (parent, then grandparent) because that is how a process tree is walked, and a
 * human reads the chain the other way, oldest first — print it as it arrives and the page says
 * the attacker started Explorer. And the absence: an alert whose writer was never attributed has
 * no chain at all, and an empty tree drawn as if it were a finding is worse than saying so.
 */

import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { I18nextProvider } from 'react-i18next';
import i18n from '@/i18n';
import { AlertGenealogy } from './alert-genealogy';
import { chainOrder } from '@/lib/alert-genealogy';
import type { AlertGenealogy as Genealogy } from '@/api/alerts';

const CHAIN: Genealogy = {
  chain: 'explorer -> cmd -> powershell',
  writer: {
    pid: 6860,
    name: 'powershell',
    path: 'C:\\Windows\\System32\\WindowsPowerShell\\v1.0\\powershell.exe',
  },
  ancestors: [
    { pid: 4120, name: 'cmd', path: 'C:\\Windows\\System32\\cmd.exe' },
    { pid: 3100, name: 'explorer', path: 'C:\\Windows\\explorer.exe' },
  ],
  patterns: [
    {
      technique_id: 'T1486',
      technique_name: 'Data Encrypted for Impact',
      description: 'A script interpreter wrote to a decoy file.',
    },
  ],
};

function show(genealogy: Genealogy | null) {
  return render(
    <I18nextProvider i18n={i18n}>
      <AlertGenealogy genealogy={genealogy} />
    </I18nextProvider>,
  );
}

describe('the chain of processes behind a write', () => {
  it('reads oldest first, whatever order it arrived in', () => {
    expect(chainOrder(CHAIN).map((p) => p.name)).toEqual(['explorer', 'cmd', 'powershell']);
  });

  it('shows the whole chain, with the writer at the end', () => {
    show(CHAIN);

    const tree = screen.getByTestId('genealogy-tree');
    const names = Array.from(tree.querySelectorAll('li')).map((li) => li.textContent ?? '');

    expect(names).toHaveLength(3);
    expect(names[0]).toContain('explorer');
    expect(names[1]).toContain('cmd');
    expect(names[2]).toContain('powershell');

    // Only the process that actually wrote the file is marked as having written it.
    expect(names[2]).toContain('wrote the file');
    expect(names[0]).not.toContain('wrote the file');
  });

  it('names each process by its PID, so it can be matched with the trigger output', () => {
    show(CHAIN);
    expect(screen.getByText('PID 6860')).toBeInTheDocument();
    expect(screen.getByText('PID 4120')).toBeInTheDocument();
    expect(screen.getByText('PID 3100')).toBeInTheDocument();
  });

  it('shows the MITRE technique the chain matched', () => {
    show(CHAIN);
    const patterns = screen.getByTestId('genealogy-patterns');
    expect(patterns).toHaveTextContent('T1486');
    expect(patterns).toHaveTextContent('Data Encrypted for Impact');
    expect(patterns).toHaveTextContent('A script interpreter wrote to a decoy file.');
  });

  it('draws no pattern block when the chain matched none', () => {
    show({ ...CHAIN, patterns: [] });
    expect(screen.queryByTestId('genealogy-patterns')).not.toBeInTheDocument();
    // The chain itself is still shown: no pattern is not the same as no chain.
    expect(screen.getByTestId('genealogy-tree')).toBeInTheDocument();
  });

  it('says there is no chain rather than drawing an empty one', () => {
    show(null);
    expect(screen.getByTestId('genealogy-absent')).toBeInTheDocument();
    expect(screen.queryByTestId('genealogy-tree')).not.toBeInTheDocument();
  });

  it('shows a writer with no ancestors as the whole chain', () => {
    // Its parent had already exited when the ancestry was captured. One process is still a chain.
    show({ ...CHAIN, ancestors: [] });

    const tree = screen.getByTestId('genealogy-tree');
    const rows = Array.from(tree.querySelectorAll('li'));
    expect(rows).toHaveLength(1);
    expect(rows[0]?.textContent).toContain('powershell');
    expect(rows[0]?.textContent).toContain('wrote the file');
  });

  it('is English', () => {
    show(null);
    expect(screen.getByText(/What started it/i)).toBeInTheDocument();
    expect(screen.queryByText(/Ce qui l/i)).not.toBeInTheDocument();
  });
});
