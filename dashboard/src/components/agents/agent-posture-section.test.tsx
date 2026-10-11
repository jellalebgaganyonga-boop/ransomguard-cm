import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { cleanup, render, screen, within } from '@testing-library/react';
import i18n from '@/i18n';
import type { AgentDetail, AgentPosture } from '@/api/agents';
import { AgentPostureSection } from './agent-posture-section';

/**
 * The agent page's posture section (R8). Rebuilt from the recovered fragment of 7ad1fc2, on the
 * final contract: the verdict is "partial" (not "at_risk"), product names have their space, the
 * banner composes the whole sentence.
 */

function posture(overrides: Partial<AgentPosture> = {}): AgentPosture {
  return {
    schema_version: 2,
    received_at: new Date().toISOString(),
    agent_uptime_seconds: 3600,
    threat_intel_version: '2026-10-11-001',
    last_detection_at: null,
    system_free_bytes: 5 * 1024 ** 3,
    db_volume_free_bytes: 2 * 1024 ** 3,
    pending_alerts: 2,
    last_upload_ok_at: new Date().toISOString(),
    last_upload_age_seconds: 120,
    last_upload_error_code: 'HTTP_422',
    rejected_alerts: null,
    modules: [
      { code: 'SENTINEL', state: 'active', reason_code: null, changed_at: '2026-10-11T08:00:00Z' },
      { code: 'EXFIL_WATCH', state: 'inactive', reason_code: 'admin_rights_missing', changed_at: '2026-10-11T08:00:00Z' },
      { code: 'ANTI_TAMPERING', state: 'inactive', reason_code: 'not_implemented', changed_at: '2026-10-11T08:00:00Z' },
    ],
    ...overrides,
  };
}

function makeAgent(overrides: Partial<AgentDetail> = {}): AgentDetail {
  return {
    id: 'a1',
    hostname: 'DESKTOP-NODK1GK',
    fqdn: 'desktop-nodk1gk.hospital.local',
    os_version: 'Microsoft Windows NT 10.0.26300.0',
    agent_version: '0.4.0',
    status: 'active',
    last_heartbeat_at: new Date().toISOString(),
    protection_state: 'partial',
    reason_codes: ['EXFIL_WATCH:inactive:admin_rights_missing'],
    signal_age_seconds: 30,
    posture: posture(),
    ...overrides,
  };
}

describe('AgentPostureSection', () => {
  beforeEach(async () => {
    await i18n.changeLanguage('fr');
  });
  afterEach(() => {
    cleanup();
  });

  it('composes the whole sentence in the banner: module, state, reason', () => {
    render(<AgentPostureSection agent={makeAgent()} />);

    const banner = screen.getByTestId('posture-banner');
    expect(banner).toHaveAttribute('data-state', 'partial');
    expect(
      within(banner).getByRole('heading', {
        name: 'Protection partielle — EXFIL WATCH inactif : droits administrateur manquants',
      }),
    ).toBeInTheDocument();
  });

  it('shows product names with their space, never the wire code', () => {
    render(<AgentPostureSection agent={makeAgent()} />);

    expect(screen.getByRole('cell', { name: 'EXFIL WATCH' })).toBeInTheDocument();
    expect(screen.getByRole('cell', { name: 'ANTI-TAMPERING' })).toBeInTheDocument();
    expect(screen.queryByText('EXFIL_WATCH')).not.toBeInTheDocument();
  });

  it('shows an unrecognised module code verbatim, never blank', () => {
    const agent = makeAgent({
      posture: posture({
        modules: [{ code: 'MOON_LASER', state: 'active', reason_code: null, changed_at: '2026-10-11T08:00:00Z' }],
      }),
    });
    render(<AgentPostureSection agent={agent} />);

    expect(screen.getByRole('cell', { name: 'MOON_LASER' }).textContent).toBe('MOON_LASER');
  });

  it('translates a module reason code, the raw code in the tooltip', () => {
    render(<AgentPostureSection agent={makeAgent()} />);

    const reason = screen.getByRole('cell', { name: 'droits administrateur manquants' });
    expect(reason).toHaveAttribute('title', 'Code : admin_rights_missing');
  });

  it('translates the transmission error code, the raw code in the tooltip', () => {
    render(<AgentPostureSection agent={makeAgent()} />);

    const error = screen.getByText('Rejeté par le serveur');
    expect(error).toHaveAttribute('title', 'Code : HTTP_422');
  });

  it('labels the queue "Alertes non transmises"', () => {
    render(<AgentPostureSection agent={makeAgent()} />);

    expect(screen.getByText('Alertes non transmises')).toBeInTheDocument();
  });

  it('shows null as "indisponible", never 0', () => {
    const agent = makeAgent({
      posture: posture({
        pending_alerts: null,
        system_free_bytes: null,
        db_volume_free_bytes: null,
        rejected_alerts: null,
        last_upload_ok_at: null,
        last_upload_age_seconds: null,
      }),
    });
    render(<AgentPostureSection agent={agent} />);

    expect(screen.getAllByText('indisponible').length).toBeGreaterThanOrEqual(5);
    expect(screen.queryByText('0')).not.toBeInTheDocument();
  });

  it('lists the other findings under the headline', () => {
    const agent = makeAgent({
      protection_state: 'unprotected',
      reason_codes: ['SENTINEL:inactive:stopped_unexpectedly', 'restart_loop'],
    });
    render(<AgentPostureSection agent={agent} />);

    expect(screen.getByRole('heading', { name: 'Non protégé — SENTINEL inactif : arrêt inattendu' })).toBeInTheDocument();
    expect(screen.getByText("Redémarrages répétés de l'agent")).toBeInTheDocument();
  });

  it('says when the endpoint never reported a posture', () => {
    render(
      <AgentPostureSection
        agent={makeAgent({ protection_state: 'unknown', reason_codes: ['never_connected'], posture: null })}
      />,
    );

    expect(screen.getByRole('heading', { name: 'Jamais connecté' })).toBeInTheDocument();
    expect(screen.getByText("Ce poste n'a transmis aucune posture.")).toBeInTheDocument();
  });

  it('renders in English', async () => {
    await i18n.changeLanguage('en');
    render(<AgentPostureSection agent={makeAgent()} />);

    expect(
      screen.getByRole('heading', { name: 'Partial protection — EXFIL WATCH inactive: administrator rights missing' }),
    ).toBeInTheDocument();
    expect(screen.getByText('Alerts not transmitted')).toBeInTheDocument();
  });
});
