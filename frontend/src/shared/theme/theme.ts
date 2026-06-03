import { createTheme, type Theme } from '@mui/material/styles';
import { ledgerColors } from './palette';
import { ledgerShadows } from './shadows';
import { serifFamily, sansFamily, monoFamily, tabularFeatureSettings } from './fonts';

/**
 * Builds the LEDGER MUI theme: warm paper background, hairline-ruled surfaces with
 * NO elevation, serif headings, and tabular-mono figures. The same theme serves both
 * density modes; per-component density is driven at the call site from the layout store.
 */
export function buildLedgerTheme(): Theme {
  const theme: Theme = createTheme({
    palette: {
      mode: 'light',
      primary: { main: ledgerColors.green, contrastText: '#FFFFFF' },
      secondary: { main: ledgerColors.inkSoft, contrastText: '#FFFFFF' },
      error: { main: ledgerColors.oxblood },
      warning: { main: ledgerColors.amber },
      success: { main: ledgerColors.positive },
      background: { default: ledgerColors.paper, paper: ledgerColors.surface },
      text: { primary: ledgerColors.ink, secondary: ledgerColors.inkSoft },
      divider: ledgerColors.hairline
    },
    shape: { borderRadius: 8 },
    typography: {
      fontFamily: sansFamily,
      h1: { fontFamily: serifFamily, fontWeight: 500, letterSpacing: '-0.01em', fontOpticalSizing: 'auto' },
      h2: { fontFamily: serifFamily, fontWeight: 500, letterSpacing: '-0.01em', fontOpticalSizing: 'auto' },
      h3: { fontFamily: serifFamily, fontWeight: 500, fontOpticalSizing: 'auto' },
      h4: { fontFamily: serifFamily, fontWeight: 500, fontOpticalSizing: 'auto' },
      h5: { fontFamily: serifFamily, fontWeight: 500, fontOpticalSizing: 'auto' },
      h6: { fontFamily: serifFamily, fontWeight: 500, fontOpticalSizing: 'auto' },
      subtitle1: { fontWeight: 500 },
      subtitle2: { fontWeight: 600, letterSpacing: '0.04em' },
      button: { textTransform: 'none', fontWeight: 500, letterSpacing: '0.02em' },
      overline: {
        fontWeight: 600,
        fontSize: '0.6875rem',
        letterSpacing: '0.1em',
        textTransform: 'uppercase',
        color: ledgerColors.inkSoft
      }
    },
    // No elevation anywhere: flatten the whole shadow ramp.
    shadows: Array(25).fill('none') as Theme['shadows']
  });

  theme.components = {
    MuiCssBaseline: {
      styleOverrides: {
        body: {
          backgroundColor: ledgerColors.paper,
          color: ledgerColors.ink,
          // Tabular figures everywhere by default; the mono face still overrides per element.
          fontVariantNumeric: 'tabular-nums lining-nums'
        }
      }
    },
    MuiPaper: {
      defaultProps: { elevation: 0 },
      styleOverrides: {
        root: { backgroundImage: 'none', boxShadow: 'none' },
        outlined: { borderColor: ledgerColors.hairline }
      }
    },
    MuiCard: {
      defaultProps: { elevation: 0, variant: 'outlined' },
      styleOverrides: {
        root: {
          border: `1px solid ${ledgerColors.hairline}`,
          borderRadius: 8,
          boxShadow: ledgerShadows.card,
          backgroundColor: ledgerColors.surface
        }
      }
    },
    MuiAppBar: {
      defaultProps: { elevation: 0, color: 'transparent' },
      styleOverrides: {
        root: {
          backgroundColor: ledgerColors.paper,
          color: ledgerColors.ink,
          boxShadow: 'none',
          borderBottom: `1px solid ${ledgerColors.hairline}`
        }
      }
    },
    MuiButton: {
      defaultProps: { disableElevation: true, disableRipple: false },
      styleOverrides: {
        root: { borderRadius: 4, boxShadow: 'none', paddingInline: 16 },
        contained: {
          boxShadow: 'none',
          '&:hover': { boxShadow: 'none', backgroundColor: '#164D30' }
        },
        outlined: {
          borderColor: ledgerColors.hairline,
          color: ledgerColors.ink,
          '&:hover': { borderColor: ledgerColors.ink, backgroundColor: 'transparent' }
        },
        text: { color: ledgerColors.ink }
      }
    },
    MuiIconButton: {
      styleOverrides: {
        root: { borderRadius: 4, color: ledgerColors.inkSoft, '&:hover': { color: ledgerColors.ink } }
      }
    },
    MuiOutlinedInput: {
      styleOverrides: {
        root: {
          borderRadius: 6,
          backgroundColor: ledgerColors.surface,
          '& .MuiOutlinedInput-notchedOutline': { borderColor: ledgerColors.hairline },
          '&:hover .MuiOutlinedInput-notchedOutline': { borderColor: ledgerColors.inkSoft },
          '&.Mui-focused .MuiOutlinedInput-notchedOutline': {
            borderColor: ledgerColors.green,
            borderWidth: 1
          }
        }
      }
    },
    MuiInputLabel: {
      styleOverrides: {
        root: {
          color: ledgerColors.inkSoft,
          '&.Mui-focused': { color: ledgerColors.green }
        }
      }
    },
    MuiFormHelperText: {
      styleOverrides: {
        root: { '&.Mui-error': { color: ledgerColors.oxblood } }
      }
    },
    MuiDivider: {
      styleOverrides: { root: { borderColor: ledgerColors.hairline } }
    },
    MuiDialog: {
      // elevation:0 alongside variant:'outlined' avoids MUI's "Combining elevation={24} with
      // variant='outlined' has no effect" warning. The ledger dialog depth is applied via the
      // styleOverrides.paper boxShadow below, not via MUI's elevation ramp.
      defaultProps: { PaperProps: { variant: 'outlined', elevation: 0 } },
      styleOverrides: {
        paper: {
          border: `1px solid ${ledgerColors.hairline}`,
          borderRadius: 8,
          boxShadow: ledgerShadows.dialog
        }
      }
    },
    MuiMenu: {
      styleOverrides: {
        paper: {
          boxShadow: ledgerShadows.menu,
          border: `1px solid ${ledgerColors.hairline}`,
          borderRadius: 6
        }
      }
    },
    MuiPopover: {
      styleOverrides: {
        paper: { boxShadow: ledgerShadows.menu }
      }
    },
    MuiTooltip: {
      styleOverrides: {
        tooltip: {
          backgroundColor: ledgerColors.ink,
          fontFamily: sansFamily,
          fontSize: '0.75rem',
          borderRadius: 4
        }
      }
    },
    MuiLink: {
      defaultProps: { underline: 'none' },
      styleOverrides: { root: { color: ledgerColors.ink, fontWeight: 500 } }
    },
    MuiDataGrid: {
      styleOverrides: {
        root: {
          border: `1px solid ${ledgerColors.hairline}`,
          borderRadius: 4,
          backgroundColor: ledgerColors.surface,
          fontFamily: sansFamily,
          '--DataGrid-rowBorderColor': ledgerColors.hairline,
          '& .MuiDataGrid-withBorderColor': { borderColor: ledgerColors.hairline },
          // Ledger mono cells: codes / money render in tabular monospace, right aligned.
          '& .MuiDataGrid-cell--ledgerMono': {
            fontFamily: monoFamily,
            fontFeatureSettings: tabularFeatureSettings
          },
          '& .MuiDataGrid-row:hover': { backgroundColor: ledgerColors.paper },
          '& .MuiDataGrid-row.Mui-selected': {
            backgroundColor: ledgerColors.greenSoft
          },
          '& .MuiDataGrid-row.Mui-selected:hover': {
            backgroundColor: ledgerColors.greenSoft
          }
        },
        columnHeaders: { backgroundColor: ledgerColors.paper },
        columnHeader: {
          backgroundColor: ledgerColors.paper,
          '&:focus, &:focus-within': { outline: 'none' }
        },
        columnHeaderTitle: {
          textTransform: 'uppercase',
          letterSpacing: '0.08em',
          fontSize: '0.6875rem',
          fontWeight: 600,
          color: ledgerColors.inkSoft
        },
        columnSeparator: { color: ledgerColors.hairline },
        cell: {
          borderBottomColor: ledgerColors.hairline,
          color: ledgerColors.ink,
          '&:focus, &:focus-within': { outline: 'none' }
        },
        footerContainer: { borderTopColor: ledgerColors.hairline }
      }
    }
  };

  return theme;
}
