import type { ProviderContext } from 'notistack';

let snackbar: ProviderContext | null = null;

/**
 * Wires the notistack provider context so the framework-agnostic `notification`
 * facade can enqueue toasts from query hooks and other non-component code.
 * Called once by {@link NotificationBridge}.
 */
export function registerSnackbar(context: ProviderContext): void {
  snackbar = context;
}

/**
 * App-wide toast facade. Hooks forward API failures here via
 * `notification.error(getApiErrorMessage(err, t))` — never raw messages.
 */
export const notification = {
  /** Shows an oxblood error toast. */
  error(message: string): void {
    snackbar?.enqueueSnackbar(message, { variant: 'error' });
  },
  /** Shows a green success toast. */
  success(message: string): void {
    snackbar?.enqueueSnackbar(message, { variant: 'success' });
  },
  /** Shows a neutral informational toast. */
  info(message: string): void {
    snackbar?.enqueueSnackbar(message, { variant: 'info' });
  }
};
