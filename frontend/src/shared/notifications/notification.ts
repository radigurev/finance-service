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
 *
 * The surface colors come from `ledgerSnackbarColors`, which every `SnackbarProvider` in the app
 * installs through its `Components` map. notistack does NOT read the MUI theme, so without that map
 * these variants would render in notistack's own Material colors regardless of what these docs claim.
 */
export const notification = {
  /** Shows an error toast on the ledger oxblood surface. */
  error(message: string): void {
    snackbar?.enqueueSnackbar(message, { variant: 'error' });
  },
  /** Shows a success toast on the deep ledger-green surface. */
  success(message: string): void {
    snackbar?.enqueueSnackbar(message, { variant: 'success' });
  },
  /** Shows a neutral informational toast on the near-black ink surface. */
  info(message: string): void {
    snackbar?.enqueueSnackbar(message, { variant: 'info' });
  }
};
