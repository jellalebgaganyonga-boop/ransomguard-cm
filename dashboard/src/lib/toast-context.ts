/**
 * The toast API (context + hook), apart from the components so the provider's file only exports
 * components (react-refresh). See components/ui/toast.tsx for what a toast is for.
 */

import { createContext, useContext } from 'react';

export type ToastKind = 'success' | 'error' | 'info';

export interface Toast {
  id: number;
  kind: ToastKind;
  message: string;
}

export interface ToastApi {
  push: (kind: ToastKind, message: string) => void;
  success: (message: string) => void;
  error: (message: string) => void;
  info: (message: string) => void;
}

export const ToastContext = createContext<ToastApi | null>(null);

const NOOP: ToastApi = { push: () => {}, success: () => {}, error: () => {}, info: () => {} };

/** The toast API; outside a provider (a component rendered alone in a unit test) it does nothing. */
export function useToast(): ToastApi {
  return useContext(ToastContext) ?? NOOP;
}
