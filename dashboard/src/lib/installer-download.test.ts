import { describe, expect, it } from 'vitest';
import { installerFilename, installerRequestBody } from './installer-download';

describe('installerRequestBody (ADR-027)', () => {
  it('carries the code, the host the console is reached on, and the agent channel port', () => {
    // 443, nginx's agent server, not the console's 8443: nginx answers an enrollment on 8443
    // with 405, so an installer pointed there could never enroll.
    expect(installerRequestBody({ host: 'grid.hopital.local', otp: 'RG-CAM-2026-X7K9P3' }))
      .toEqual({ otp: 'RG-CAM-2026-X7K9P3', host: 'grid.hopital.local', port: 443 });
    expect(installerRequestBody({ host: '10.0.0.5', otp: 'RG-CAM-2026-X7K9P3', port: 9443 }).port).toBe(9443);
  });

  it('refuses a host or a code that is not what it should be', () => {
    expect(() => installerRequestBody({ host: 'grid hopital', otp: 'RG-CAM-2026-X7K9P3' })).toThrow(/host/);
    expect(() => installerRequestBody({ host: 'grid', otp: 'short' })).toThrow(/OTP/);
    expect(() => installerRequestBody({ host: 'grid', otp: 'rg-cam-2026-x7k9p3' })).toThrow(/OTP/); // lower case is not a code
  });
});

describe('installerFilename', () => {
  it('takes the name the GRID chose', () => {
    expect(installerFilename('attachment; filename="RansomGuard-CM-Installeur-grid.hopital.local.exe"', 'x'))
      .toBe('RansomGuard-CM-Installeur-grid.hopital.local.exe');
    expect(installerFilename('attachment; filename=plain.exe', 'x')).toBe('plain.exe');
  });

  it('falls back to the name the GRID would have chosen', () => {
    expect(installerFilename(undefined, 'grid.hopital.local')).toBe('RansomGuard-CM-Installeur-grid.hopital.local.exe');
  });
});
