/**
 * The double-click installer (ADR-027): the console asks the GRID for an executable that embeds
 * the enrollment code and the GRID address, and hands it to the browser as a download. Nothing is
 * typed on the endpoint; nothing is built here -- the GRID builds it, the browser saves it.
 */

/** What the GRID is asked for: the code just obtained, and the host this console is reached on. */
export interface InstallerRequest {
  otp: string;
  /** The GRID host as the console is reached (window.location.hostname): the agent will call it. */
  host: string;
  /** The port of the AGENT channel, not the console's. Default: AGENT_CHANNEL_PORT. */
  port?: number;
}

const HOST = /^[A-Za-z0-9.-]{1,253}$/;
const OTP = /^[A-Z0-9-]{10,50}$/;

/**
 * Where agents talk to the GRID: nginx's agent server (deployment/grid/nginx/conf.d/agent.conf,
 * :443), which routes /api/v1/agents/*. The console's own port, 8443, does not -- an enrollment
 * sent there gets nginx's 405 -- so an installer built with the console's port could never
 * enroll. install.ps1 makes the same choice: BaseUrl = https://<GridServer>, port 443.
 */
export const AGENT_CHANNEL_PORT = 443;

/** Builds the request body; throws on any value that is not exactly what it should be. */
export function installerRequestBody({ host, otp, port = AGENT_CHANNEL_PORT }: InstallerRequest): { otp: string; host: string; port: number } {
  if (!HOST.test(host)) throw new Error(`invalid GRID host: ${host}`);
  if (!OTP.test(otp)) throw new Error('invalid enrollment OTP');
  return { otp, host, port };
}

/** The file name the GRID chose (Content-Disposition), or the one it would have chosen. */
export function installerFilename(contentDisposition: string | null | undefined, host: string): string {
  const match = /filename="?([^";]+)"?/i.exec(contentDisposition ?? '');
  return match?.[1] ?? `RansomGuard-CM-Installer-${host}.exe`;
}

/** Hands a blob to the browser as a file download. */
export function saveBlob(blob: Blob, filename: string): void {
  const url = URL.createObjectURL(blob);
  const anchor = document.createElement('a');
  anchor.href = url;
  anchor.download = filename;
  document.body.appendChild(anchor);
  anchor.click();
  anchor.remove();
  setTimeout(() => URL.revokeObjectURL(url), 10_000);
}
