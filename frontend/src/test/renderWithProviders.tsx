import type { ReactElement, ReactNode } from 'react';
import { render, type RenderResult } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Routes, Route } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { ThemeProvider } from '@mui/material/styles';
import { CssBaseline } from '@mui/material';
import { SnackbarProvider } from 'notistack';
import { I18nextProvider } from 'react-i18next';
import { buildLedgerTheme } from '@/shared/theme';
import { NotificationBridge } from '@/shared/notifications/NotificationBridge';
import { ledgerSnackbarComponents } from '@/shared/notifications/ledgerSnackbar';
import i18n from '@/shared/i18n/i18n';

/** Options controlling routing context for a rendered component. */
export interface RenderOptions {
  /** Initial history entries for the in-memory router. Defaults to `['/']`. */
  initialEntries?: string[];
  /**
   * When set, the component is mounted under this route path so `useParams()` resolves.
   * The element is rendered for the first entry in {@link initialEntries}.
   */
  routePath?: string;
}

/** A fresh QueryClient with retries disabled so error paths resolve immediately in tests. */
function createTestQueryClient(): QueryClient {
  return new QueryClient({
    defaultOptions: {
      queries: { retry: false, gcTime: 0, staleTime: 0 },
      mutations: { retry: false }
    }
  });
}

function Providers({ children, options }: { children: ReactNode; options: RenderOptions }): ReactElement {
  const theme = buildLedgerTheme();
  return (
    <I18nextProvider i18n={i18n}>
      <ThemeProvider theme={theme}>
        <CssBaseline />
        <SnackbarProvider Components={ledgerSnackbarComponents}>
          <NotificationBridge />
          <QueryClientProvider client={createTestQueryClient()}>
            <MemoryRouter initialEntries={options.initialEntries ?? ['/']}>{children}</MemoryRouter>
          </QueryClientProvider>
        </SnackbarProvider>
      </ThemeProvider>
    </I18nextProvider>
  );
}

/**
 * Renders a component inside the full app provider stack (theme, query client, router,
 * notistack, i18n) and returns the Testing Library result plus a pre-bound userEvent.
 */
export function renderWithProviders(ui: ReactElement, options: RenderOptions = {}): RenderResult & {
  user: ReturnType<typeof userEvent.setup>;
} {
  const tree = options.routePath ? (
    <Routes>
      <Route path={options.routePath} element={ui} />
    </Routes>
  ) : (
    ui
  );

  const result = render(<Providers options={options}>{tree}</Providers>);
  return { ...result, user: userEvent.setup() };
}
