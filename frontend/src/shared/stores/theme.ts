import { create } from 'zustand';
import type { Theme } from '@mui/material/styles';
import { buildLedgerTheme } from '@/shared/theme';

const ledgerTheme: Theme = buildLedgerTheme();

interface ThemeState {
  theme: Theme;
}

/** Exposes the single LEDGER theme. Dark mode is deferred. */
export const useThemeStore = create<ThemeState>(() => ({
  theme: ledgerTheme
}));
