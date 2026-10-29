/**
 * The server's own words, for the person who has to act on them.
 *
 * A dialog that says only "An error occurred" tells an administrator nothing they can use. The
 * GRID already answers with a reason — "Cannot remove the tenant_admin role from the last active
 * administrator", "Unknown role: analyst" — and throwing it away turns a solvable problem into a
 * support call. Worse, on 2026-10-27 the message appeared over a change that had succeeded: the
 * response failed to parse, and the only thing shown was that something, somewhere, went wrong.
 *
 * So: show what the server said when it said anything, and otherwise say plainly that the answer
 * could not be read — which is a different problem and sends you to a different place.
 */
import { AxiosError } from 'axios';
import { ZodError } from 'zod';

/** FastAPI's `detail`: a string, or a list of validation problems. */
function fromDetail(detail: unknown): string | null {
  if (typeof detail === 'string' && detail.trim()) return detail;
  if (Array.isArray(detail)) {
    const parts = detail
      .map((d) => (typeof d === 'object' && d !== null && 'msg' in d ? String((d as { msg: unknown }).msg) : null))
      .filter((m): m is string => Boolean(m));
    if (parts.length) return parts.join('; ');
  }
  if (typeof detail === 'object' && detail !== null && 'message' in detail) {
    const m = (detail as { message: unknown }).message;
    if (typeof m === 'string' && m.trim()) return m;
  }
  return null;
}

/**
 * A message to show the user, or null when there is nothing better than the caller's fallback.
 */
export function apiErrorMessage(error: unknown): string | null {
  if (error instanceof ZodError) {
    // The request worked and the answer was not the shape we expected. Saying "an error
    // occurred" here is actively misleading, because the change may well have been saved.
    const fields = error.issues.map((i) => i.path.join('.')).filter(Boolean);
    return fields.length
      ? `The server's answer could not be read (unexpected ${fields.join(', ')}). The change may have been saved — reload before trying again.`
      : "The server's answer could not be read. The change may have been saved — reload before trying again.";
  }

  if (error instanceof AxiosError) {
    const said = fromDetail(error.response?.data?.detail) ?? fromDetail(error.response?.data);
    if (said) return said;
    if (error.response) return `The server refused this (HTTP ${error.response.status}).`;
    if (error.request) return 'The server could not be reached.';
  }

  if (error instanceof Error && error.message) return error.message;
  return null;
}
