import { useEffect } from 'react';
import { useSnackbar } from 'notistack';
import { registerSnackbar } from './notification';

/**
 * Mounted inside the notistack `SnackbarProvider`, this captures the imperative
 * snackbar context and registers it with the {@link notification} facade so it can
 * be used outside React components.
 */
export function NotificationBridge() {
  const context = useSnackbar();

  useEffect(() => {
    registerSnackbar(context);
  }, [context]);

  return null;
}
