import { Outlet, Link as RouterLink, useNavigate } from 'react-router-dom';
import {
  AppBar,
  Box,
  Container,
  IconButton,
  Toolbar,
  Typography,
  Switch,
  FormControlLabel,
  MenuItem,
  Select,
  Button
} from '@mui/material';
import LogoutIcon from '@mui/icons-material/Logout';
import { useTranslation } from 'react-i18next';
import { useLayoutStore } from '@/shared/stores/layout';
import { useAuthStore } from '@/shared/stores/auth';

export function AppShell() {
  const { t, i18n } = useTranslation();
  const navigate = useNavigate();
  const isCompact = useLayoutStore((s) => s.isCompact);
  const toggleDensity = useLayoutStore((s) => s.toggleDensity);
  const logout = useAuthStore((s) => s.logout);

  function handleLanguageChange(value: string) {
    void i18n.changeLanguage(value);
  }

  function handleLogout() {
    logout();
    navigate('/login', { replace: true });
  }

  return (
    <Box sx={{ display: 'flex', flexDirection: 'column', minHeight: '100vh' }}>
      <AppBar position="static" color="primary">
        <Toolbar>
          <Typography variant="h6" sx={{ flexGrow: 1 }}>
            {t('app.title')}
          </Typography>

          <Button component={RouterLink} to="/accounts" color="inherit">
            {t('nav.accounts')}
          </Button>

          <FormControlLabel
            sx={{ ml: 2, color: 'inherit' }}
            control={<Switch checked={isCompact} onChange={toggleDensity} color="default" />}
            label={t('layout.compact')}
          />

          <Select
            size="small"
            value={i18n.language.startsWith('bg') ? 'bg' : 'en'}
            onChange={(e) => handleLanguageChange(e.target.value)}
            sx={{
              ml: 2,
              color: 'inherit',
              '.MuiOutlinedInput-notchedOutline': { borderColor: 'rgba(255,255,255,0.5)' }
            }}
          >
            <MenuItem value="en">EN</MenuItem>
            <MenuItem value="bg">BG</MenuItem>
          </Select>

          <IconButton color="inherit" onClick={handleLogout} aria-label={t('auth.logout')}>
            <LogoutIcon />
          </IconButton>
        </Toolbar>
      </AppBar>

      <Container maxWidth="xl" sx={{ py: isCompact ? 2 : 4, flexGrow: 1 }}>
        <Outlet />
      </Container>
    </Box>
  );
}
