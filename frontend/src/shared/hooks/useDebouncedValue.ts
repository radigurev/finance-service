import { useEffect, useState } from 'react';

/** Default quiet period before a typed value is treated as committed. */
export const DEFAULT_DEBOUNCE_MS = 300;

/**
 * Returns `value` only after it has stopped changing for `delayMs` — the guard for inputs that feed a
 * react-query key. Without it, every keystroke is a new query key and therefore a new request; the
 * aging report is the sharp case, because `GET /api/v1/aging` has neither paging nor a server-side cap
 * (SDD-UI-FIN-002 §1.6 gap 8), so each intermediate character would build a full unbounded report.
 *
 * The pending timer is cleared on every change and on unmount, so at most one trailing update lands.
 */
export function useDebouncedValue<T>(value: T, delayMs: number = DEFAULT_DEBOUNCE_MS): T {
  const [debounced, setDebounced] = useState<T>(value);

  useEffect(() => {
    const handle = window.setTimeout(() => setDebounced(value), delayMs);
    return () => window.clearTimeout(handle);
  }, [value, delayMs]);

  return debounced;
}
