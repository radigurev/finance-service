import { styled } from '@mui/material/styles';
import { MaterialDesignContent, type VariantType } from 'notistack';
import { ledgerColors, ledgerShadows, sansFamily } from '@/shared/theme';

/**
 * Toast surface color per notistack variant, taken from the LEDGER palette (SDD-UI-001 §2 —
 * "NO bright Material blue anywhere").
 *
 * notistack ships its own Material Design colors and does NOT read the MUI theme, so an unthemed
 * `SnackbarProvider` renders `info` as Material Light Blue `#2196F3`, `error` as `#D32F2F` and
 * `success` as `#43A047` — none of which exist in this palette. Every variant is therefore bound to a
 * token that is already used elsewhere in the app: oxblood for danger, ledger green for positive,
 * amber for warnings, and near-black ink for the neutral informational / default surface.
 */
export const ledgerSnackbarColors: Record<VariantType, string> = {
  error: ledgerColors.oxblood,
  success: ledgerColors.green,
  warning: ledgerColors.amber,
  info: ledgerColors.ink,
  default: ledgerColors.ink
};

/**
 * notistack's Material content surface restyled to the ledger aesthetic: palette backgrounds, the
 * paper tone for text, the app's sans face, a 4px radius and the menu-depth shadow (the global MUI
 * shadow ramp is flattened, so the depth is applied here explicitly).
 *
 * The `&.notistack-MuiContent-<variant>` selectors are notistack's documented styling seam — the
 * variant class is applied by `MaterialDesignContent` itself, so a single styled component serves
 * every variant.
 */
export const LedgerSnackbarContent = styled(MaterialDesignContent)({
  '&.notistack-MuiContent': {
    fontFamily: sansFamily,
    fontSize: '0.875rem',
    fontWeight: 500,
    borderRadius: 4,
    boxShadow: ledgerShadows.menu,
    color: ledgerColors.paper
  },
  '&.notistack-MuiContent-default': { backgroundColor: ledgerSnackbarColors.default },
  '&.notistack-MuiContent-info': { backgroundColor: ledgerSnackbarColors.info },
  '&.notistack-MuiContent-success': { backgroundColor: ledgerSnackbarColors.success },
  '&.notistack-MuiContent-warning': { backgroundColor: ledgerSnackbarColors.warning },
  '&.notistack-MuiContent-error': { backgroundColor: ledgerSnackbarColors.error }
});

/**
 * The `Components` map handed to every `SnackbarProvider` in the app (and to the test provider stack
 * in `renderWithProviders`, so the suite exercises the same surfaces the browser does).
 */
export const ledgerSnackbarComponents: Record<VariantType, typeof LedgerSnackbarContent> = {
  default: LedgerSnackbarContent,
  info: LedgerSnackbarContent,
  success: LedgerSnackbarContent,
  warning: LedgerSnackbarContent,
  error: LedgerSnackbarContent
};
