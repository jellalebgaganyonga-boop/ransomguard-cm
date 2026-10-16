/**
 * The install command the console shows for a new endpoint (GRID-SEC-006).
 *
 * One command, for PowerShell run as administrator. It:
 *   1. downloads the installer WITH TLS validation (no -k, no SkipCertificateCheck) into a file
 *      with a random name under the administrator's %TEMP% -- never C:\, where any authenticated
 *      user can create or replace files;
 *   2. checks its SHA-256 against the fingerprint the GRID computed on the file it serves;
 *   3. stops if they differ; otherwise runs it with -ExecutionPolicy Bypass for that one process
 *      only, then deletes it.
 * If the GRID's CA is not trusted by the endpoint, the download fails and the message points to
 * the manual procedure (AGT-PKI-001). All of this goes away with the signed setup.exe (Sprint 12).
 *
 * Messages inside the command are plain ASCII: the PowerShell console's code page is unknown.
 */

export interface InstallCommandInput {
  /** The GRID host, as the console is reached (window.location.hostname). */
  host: string;
  otp: string;
  /** Upper-case hex SHA-256 of the served install.ps1 (ProvisionAgentResponse.installer_sha256). */
  sha256: string;
  port?: number;
}

const HOST = /^[A-Za-z0-9.-]{1,253}$/;
const OTP = /^[A-Z0-9-]{10,50}$/;
const SHA256 = /^[0-9A-F]{64}$/;

/** Builds the command; throws on any value that is not exactly what it should be. */
export function buildInstallCommand({ host, otp, sha256, port = 8443 }: InstallCommandInput): string {
  if (!HOST.test(host)) throw new Error(`invalid GRID host: ${host}`);
  if (!OTP.test(otp)) throw new Error('invalid enrollment OTP');
  if (!SHA256.test(sha256)) throw new Error('invalid installer fingerprint');

  const url = `https://${host}:${port}/agent/install.ps1`;
  return [
    "$ErrorActionPreference = 'Stop'",
    "$f = Join-Path $env:TEMP ('rg-install-' + [guid]::NewGuid().ToString('N') + '.ps1')",
    `try { Invoke-WebRequest -UseBasicParsing -Uri '${url}' -OutFile $f } catch { ` +
      "Write-Host ('Download refused: ' + $_.Exception.Message + ' -- if the GRID certificate is not trusted, " +
      "install its CA first (agent/README.md, Trusting the GRID certificate authority).') -ForegroundColor Red; return }",
    `if ((Get-FileHash -Algorithm SHA256 $f).Hash -ne '${sha256}') { Remove-Item $f -Force; ` +
      "Write-Host 'SHA-256 mismatch: installer refused, nothing was run.' -ForegroundColor Red; return }",
    `powershell -NoProfile -ExecutionPolicy Bypass -File $f -OTP '${otp}' -GridServer '${host}'`,
    'Remove-Item $f -Force',
  ].join('; ');
}
