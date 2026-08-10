import { describe, it, expect } from 'vitest';
import { screen, waitFor } from '@testing-library/react';
import { renderWithProviders } from '@/test/renderWithProviders';
import { ledgerColors } from '@/shared/theme';
import { notification } from './notification';
import { ledgerSnackbarColors } from './ledgerSnackbar';

/** notistack's own Material Design surfaces — the colors the ledger palette forbids (SDD-UI-001 §2). */
const MATERIAL_DEFAULTS: Record<string, string> = {
  info: 'rgb(33, 150, 243)',
  success: 'rgb(67, 160, 71)',
  error: 'rgb(211, 47, 47)',
  warning: 'rgb(255, 152, 0)'
};

/** Turns a `#RRGGBB` token into the `rgb(r, g, b)` form `getComputedStyle` reports. */
function toRgb(hex: string): string {
  const digits: string = hex.replace('#', '');
  const channels: number[] = [0, 2, 4].map((start) =>
    Number.parseInt(digits.slice(start, start + 2), 16)
  );
  return `rgb(${channels[0]}, ${channels[1]}, ${channels[2]})`;
}

/** Enqueues a toast through the facade and returns notistack's rendered content element. */
async function enqueue(variant: 'info' | 'success' | 'error', message: string): Promise<HTMLElement> {
  renderWithProviders(<div />);
  notification[variant](message);

  await screen.findByText(message);
  const content = document.querySelector(`.notistack-MuiContent-${variant}`);
  if (!content) {
    throw new Error(`no .notistack-MuiContent-${variant} rendered`);
  }
  return content as HTMLElement;
}

describe('Ledger snackbar theming (SDD-UI-001 — no Material blue anywhere)', () => {
  it('renders an info toast on the ledger ink surface, never Material Light Blue', async () => {
    // The payments feature is the app's only `notification.info` consumer, so an unthemed provider put
    // #2196F3 on screen for the posting-pending affordance.
    const content = await enqueue('info', 'A retry has been queued.');

    await waitFor(() =>
      expect(window.getComputedStyle(content).backgroundColor).toBe(toRgb(ledgerColors.ink))
    );
    expect(window.getComputedStyle(content).backgroundColor).not.toBe(MATERIAL_DEFAULTS.info);
  });

  it('renders an error toast on the oxblood surface used elsewhere in the app', async () => {
    const content = await enqueue('error', 'Something went wrong.');

    expect(window.getComputedStyle(content).backgroundColor).toBe(toRgb(ledgerColors.oxblood));
    expect(window.getComputedStyle(content).backgroundColor).toBe('rgb(159, 18, 57)');
    expect(window.getComputedStyle(content).backgroundColor).not.toBe(MATERIAL_DEFAULTS.error);
  });

  it('renders a success toast on the deep ledger green, not Material green', async () => {
    const content = await enqueue('success', 'Payment recorded.');

    expect(window.getComputedStyle(content).backgroundColor).toBe(toRgb(ledgerColors.green));
    expect(window.getComputedStyle(content).backgroundColor).not.toBe(MATERIAL_DEFAULTS.success);
  });

  it('binds every notistack variant to an existing palette token', () => {
    // Reuse only — a toast must never introduce a hue that is not already in the theme.
    const palette: string[] = Object.values(ledgerColors);

    for (const [variant, color] of Object.entries(ledgerSnackbarColors)) {
      expect(palette, `${variant} is not a palette token`).toContain(color);
      expect(Object.values(MATERIAL_DEFAULTS)).not.toContain(toRgb(color));
    }

    expect(ledgerSnackbarColors.error).toBe(ledgerColors.oxblood);
    expect(ledgerSnackbarColors.success).toBe(ledgerColors.green);
    expect(ledgerSnackbarColors.warning).toBe(ledgerColors.amber);
    expect(ledgerSnackbarColors.info).toBe(ledgerColors.ink);
  });
});
