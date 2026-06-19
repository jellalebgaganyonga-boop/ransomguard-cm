// ============================================================
// src/hooks/use-idle-timeout.ts
// US1.2 AC1.2.3 — 30 minutes inactivity → logout + "Session expirée" modal
//
// Strategy:
//   - Listen to: mousemove, keypress, scroll, click (document-level)
//   - Reset timer on each activity event
//   - When IDLE_MS elapses without activity → fire onIdle callback
//   - onIdle: show modal → then logout
//   - WARNING_MS before timeout: show 1-minute warning (UX improvement)
//
// Note: This hook is mounted ONLY when the user is authenticated.
//       It is unmounted on logout (ProtectedRoute unmounts it).
// ============================================================

import { useEffect, useRef, useCallback } from 'react';

const IDLE_MS = 30 * 60 * 1000;     // 30 minutes — AC1.2.3
const WARNING_MS = 29 * 60 * 1000;  // 29 minutes — 1-minute warning before logout

const ACTIVITY_EVENTS = [
  'mousemove',
  'mousedown',
  'keypress',
  'scroll',
  'touchstart',
  'click',
] as const;

interface UseIdleTimeoutOptions {
  onWarning: () => void;   // show "session expires in 1 minute" warning
  onIdle: () => void;      // trigger logout + show "session expired" modal
  enabled: boolean;        // false when not authenticated
}

export function useIdleTimeout({ onWarning, onIdle, enabled }: UseIdleTimeoutOptions) {
  const idleTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  const warnTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  const warnFiredRef = useRef(false);

  const clearTimers = useCallback(() => {
    if (idleTimerRef.current) clearTimeout(idleTimerRef.current);
    if (warnTimerRef.current) clearTimeout(warnTimerRef.current);
  }, []);

  const resetTimers = useCallback(() => {
    clearTimers();
    warnFiredRef.current = false;

    warnTimerRef.current = setTimeout(() => {
      warnFiredRef.current = true;
      onWarning();
    }, WARNING_MS);

    idleTimerRef.current = setTimeout(() => {
      onIdle();
    }, IDLE_MS);
  }, [clearTimers, onWarning, onIdle]);

  useEffect(() => {
    if (!enabled) return;

    // Start timers immediately on mount
    resetTimers();

    // Register activity listeners on document
    const handleActivity = () => resetTimers();
    ACTIVITY_EVENTS.forEach((event) =>
      document.addEventListener(event, handleActivity, { passive: true })
    );

    return () => {
      clearTimers();
      ACTIVITY_EVENTS.forEach((event) =>
        document.removeEventListener(event, handleActivity)
      );
    };
  }, [enabled, resetTimers, clearTimers]);
}
