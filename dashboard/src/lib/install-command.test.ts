import { describe, expect, it } from 'vitest';
import { buildInstallCommand } from './install-command';

const SHA = 'A'.repeat(32) + '0123456789ABCDEF0123456789ABCDEF';
const input = { host: 'grid.hopital.ts.net', otp: 'RG-CAM-2026-X7K9P3', sha256: SHA };

describe('buildInstallCommand (GRID-SEC-006)', () => {
  const command = buildInstallCommand(input);

  it('never disables TLS validation', () => {
    expect(command).not.toMatch(/(^|\s)-k(\s|$)/);
    expect(command).not.toMatch(/SkipCertificateCheck/i);
    expect(command).not.toMatch(/ServerCertificateValidationCallback/i);
    expect(command).not.toMatch(/curl/i);
  });

  it('never writes to C:\\', () => {
    expect(command).not.toMatch(/C:\\/i);
    expect(command).toContain('Join-Path $env:TEMP');
    expect(command).toContain('[guid]::NewGuid()');
  });

  it('checks the SHA-256 the GRID served before running anything', () => {
    const check = command.indexOf(`(Get-FileHash -Algorithm SHA256 $f).Hash -ne '${SHA}'`);
    const run = command.indexOf('powershell -NoProfile -ExecutionPolicy Bypass -File $f');
    expect(check).toBeGreaterThan(-1);
    expect(run).toBeGreaterThan(check);
    expect(command).toContain('nothing was run.');
  });

  it('limits Bypass to the installer process, never a global -EP Bypass', () => {
    expect(command).not.toMatch(/-EP\s+Bypass/i);
    expect(command).not.toMatch(/Set-ExecutionPolicy/i);
    expect(command.match(/ExecutionPolicy Bypass/g)).toHaveLength(1);
  });

  it('downloads from this GRID and passes the OTP and the GRID host', () => {
    expect(command).toContain("-Uri 'https://grid.hopital.ts.net:8443/agent/install.ps1'");
    expect(command).toContain("-OTP 'RG-CAM-2026-X7K9P3' -GridServer 'grid.hopital.ts.net'");
  });

  it('points to the CA procedure when the download is refused', () => {
    expect(command).toContain('Trusting the GRID certificate authority');
  });

  it.each([
    [{ ...input, host: "grid'; Remove-Item C:\\ -Recurse #" }],
    [{ ...input, otp: "x'; calc #" }],
    [{ ...input, sha256: SHA.toLowerCase() }],
    [{ ...input, sha256: 'abc' }],
  ])('refuses a value that is not exactly what it should be (%#)', (bad) => {
    expect(() => buildInstallCommand(bad)).toThrow();
  });
});
