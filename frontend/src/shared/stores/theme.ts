import { create } from 'zustand';
import { createTheme, type Theme } from '@mui/material/styles';

const baseTheme: Theme = createTheme({
  palette: {
    mode: 'light',
    primary: { main: '#1565c0' },
    secondary: { main: '#00897b' }
  },
  shape: { borderRadius: 8 },
  typography: {
    fontFamily: 'Inter, "Segoe UI", Roboto, system-ui, sans-serif'
  }
});

interface ThemeState {
  theme: Theme;
}

export const useThemeStore = create<ThemeState>(() => ({
  theme: baseTheme
}));
