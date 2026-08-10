import React from 'react';
import ReactDOM from 'react-dom/client';
import { BrowserRouter } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { CssBaseline, ThemeProvider } from '@mui/material';
import { SnackbarProvider } from 'notistack';
import { App } from './app/App';
import { useThemeStore } from './shared/stores/theme';
import { NotificationBridge } from './shared/notifications/NotificationBridge';
import { ledgerSnackbarComponents } from './shared/notifications/ledgerSnackbar';
import './shared/theme/fonts';
import './shared/i18n/i18n';

const queryClient = new QueryClient({
  defaultOptions: {
    queries: { retry: 1, refetchOnWindowFocus: false, staleTime: 30_000 }
  }
});

function Root() {
  const theme = useThemeStore((s) => s.theme);
  return (
    <ThemeProvider theme={theme}>
      <CssBaseline />
      <SnackbarProvider
        maxSnack={3}
        autoHideDuration={5000}
        anchorOrigin={{ vertical: 'bottom', horizontal: 'right' }}
        // notistack does NOT read the MUI theme; without this map its variants render in Material
        // colors (info = #2196F3), which the ledger palette forbids.
        Components={ledgerSnackbarComponents}
      >
        <NotificationBridge />
        <QueryClientProvider client={queryClient}>
          <BrowserRouter>
            <App />
          </BrowserRouter>
        </QueryClientProvider>
      </SnackbarProvider>
    </ThemeProvider>
  );
}

ReactDOM.createRoot(document.getElementById('root')!).render(
  <React.StrictMode>
    <Root />
  </React.StrictMode>
);
