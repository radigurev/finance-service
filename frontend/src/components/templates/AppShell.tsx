import { Outlet, NavLink, useNavigate } from 'react-router-dom';
import { AppBar, Box, Container, IconButton, Toolbar, Typography, Tooltip } from '@mui/material';
import LogoutIcon from '@mui/icons-material/Logout';
import DensityComfyIcon from '@mui/icons-material/DensityMedium';
import DensitySmallIcon from '@mui/icons-material/DensitySmall';
import { useTranslation } from 'react-i18next';
import { useLayoutStore } from '@/shared/stores/layout';
import { useAuthStore } from '@/shared/stores/auth';
import { serifFamily, ledgerColors } from '@/shared/theme';

interface NavItem {
  to: string;
  labelKey: string;
}

const navItems: NavItem[] = [
  { to: '/accounts', labelKey: 'nav.accounts' },
  { to: '/currencies', labelKey: 'nav.currencies' },
  { to: '/exchange-rates', labelKey: 'nav.exchangeRates' }
];

/**
 * The application shell template: a paper-colored top bar with a single hairline bottom
 * border (no fill, no shadow, no blue), a serif "Finance" wordmark, quiet nav links with a
 * 2px green active underline, and understated density / language / logout controls.
 */
export function AppShell() {
  const { t, i18n } = useTranslation();
  const navigate = useNavigate();
  const isCompact = useLayoutStore((s) => s.isCompact);
  const toggleDensity = useLayoutStore((s) => s.toggleDensity);
  const logout = useAuthStore((s) => s.logout);

  const currentLang = i18n.language.startsWith('bg') ? 'bg' : 'en';

  function handleToggleLanguage() {
    void i18n.changeLanguage(currentLang === 'bg' ? 'en' : 'bg');
  }

  function handleLogout() {
    logout();
    navigate('/login', { replace: true });
  }

  return (
    <Box sx={{ display: 'flex', flexDirection: 'column', minHeight: '100vh', bgcolor: 'background.default' }}>
      <AppBar position="static">
        <Container maxWidth="xl" disableGutters>
          <Toolbar sx={{ gap: 3, minHeight: 60 }}>
            <Typography
              component="span"
              sx={{ fontFamily: serifFamily, fontWeight: 600, fontSize: '1.375rem', letterSpacing: '-0.01em' }}
            >
              {t('app.title')}
            </Typography>

            <Box component="nav" sx={{ display: 'flex', gap: 2.5, flexGrow: 1 }}>
              {navItems.map((item) => (
                <NavLink key={item.to} to={item.to} style={{ textDecoration: 'none' }}>
                  {({ isActive }) => (
                    <Typography
                      component="span"
                      sx={{
                        fontSize: '0.9375rem',
                        fontWeight: isActive ? 600 : 500,
                        color: isActive ? 'text.primary' : 'text.secondary',
                        pb: 0.5,
                        borderBottom: '2px solid',
                        borderColor: isActive ? ledgerColors.green : 'transparent',
                        transition: 'color 120ms ease',
                        '&:hover': { color: 'text.primary' }
                      }}
                    >
                      {t(item.labelKey)}
                    </Typography>
                  )}
                </NavLink>
              ))}
            </Box>

            <Tooltip title={isCompact ? t('layout.comfortable') : t('layout.compact')}>
              <IconButton
                onClick={toggleDensity}
                aria-label={t('layout.densityToggle')}
                size="small"
              >
                {isCompact ? <DensitySmallIcon fontSize="small" /> : <DensityComfyIcon fontSize="small" />}
              </IconButton>
            </Tooltip>

            <Tooltip title={t('layout.languageToggle')}>
              <IconButton
                onClick={handleToggleLanguage}
                aria-label={t('layout.languageToggle')}
                size="small"
                sx={{
                  width: 'auto',
                  px: 1,
                  fontSize: '0.8125rem',
                  fontWeight: 600,
                  letterSpacing: '0.08em',
                  borderRadius: 1
                }}
              >
                {currentLang.toUpperCase()}
              </IconButton>
            </Tooltip>

            <Tooltip title={t('auth.logout')}>
              <IconButton onClick={handleLogout} aria-label={t('auth.logout')} size="small">
                <LogoutIcon fontSize="small" />
              </IconButton>
            </Tooltip>
          </Toolbar>
        </Container>
      </AppBar>

      <Container maxWidth="xl" sx={{ py: isCompact ? 3 : 4, flexGrow: 1 }}>
        <Outlet />
      </Container>
    </Box>
  );
}
