import { afterEach, describe, expect, it } from 'vitest';
import { cleanup, render, screen } from '@testing-library/react';
import i18n from '@/i18n';
import { AlertModule } from './alert-module';

/**
 * MODULE column of the alert list (R6, a60224e): the test the MODULE commit never added.
 * The column shows the module, translated, with the event type underneath -- never an empty cell.
 */
describe('AlertModule (MODULE column)', () => {
  afterEach(async () => {
    cleanup();
    await i18n.changeLanguage('fr');
  });

  it('shows SENTINEL with its French label, and the event type underneath', async () => {
    await i18n.changeLanguage('fr');
    render(<AlertModule module="SENTINEL" alertType="SentinelCanaryModified" />);

    expect(screen.getByText('SENTINEL — fichiers leurres')).toBeInTheDocument();
    expect(screen.getByText('SentinelCanaryModified')).toBeInTheDocument();
  });

  it('shows the English label in English', async () => {
    await i18n.changeLanguage('en');
    render(<AlertModule module="EXFIL_WATCH" alertType="ExfilWatchDnsTunneling" />);

    expect(screen.getByText('EXFIL WATCH — data exfiltration')).toBeInTheDocument();
  });

  it.each([['UNKNOWN'], [undefined], [null], ['']])(
    'shows UNKNOWN as "Module inconnu", never an empty cell (module = %s)',
    async (module) => {
      await i18n.changeLanguage('fr');
      render(<AlertModule module={module} alertType="BrandNewEvent" />);

      const label = screen.getByText('Module inconnu');
      expect(label.textContent).not.toBe('');
    },
  );

  it('shows a module code this console does not know as is', async () => {
    await i18n.changeLanguage('fr');
    render(<AlertModule module="QUANTUM_SHIELD" alertType="QuantumShieldEvent" />);

    expect(screen.getByText('QUANTUM_SHIELD')).toBeInTheDocument();
  });

  it('has a French and an English label for every module code the GRID sends', async () => {
    const codes = ['SENTINEL', 'ENTROPY', 'GENEALOGY', 'USB_GUARD', 'EXFIL_WATCH', 'UNKNOWN'];
    for (const lang of ['fr', 'en']) {
      await i18n.changeLanguage(lang);
      for (const code of codes) {
        expect(i18n.exists(`modules.${code}`), `${lang}: modules.${code}`).toBe(true);
      }
    }
  });
});
