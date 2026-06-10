import { Outlet, NavLink, useNavigate, useLocation } from 'react-router-dom';
import {
  AppBar,
  Box,
  Container,
  Divider,
  Drawer,
  IconButton,
  Toolbar,
  Typography,
  Tooltip
} from '@mui/material';
import LogoutIcon from '@mui/icons-material/Logout';
import MenuIcon from '@mui/icons-material/Menu';
import DensityMediumIcon from '@mui/icons-material/DensityMedium';
import DensitySmallIcon from '@mui/icons-material/DensitySmall';
import { useState } from 'react';
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
  { to: '/journal-entries', labelKey: 'nav.journal' },
  { to: '/invoices', labelKey: 'nav.invoices' },
  { to: '/posting-rules', labelKey: 'nav.postingRules' },
  { to: '/general-ledger', labelKey: 'nav.generalLedger' },
  { to: '/periods', labelKey: 'nav.periods' },
  { to: '/currencies', labelKey: 'nav.currencies' },
  { to: '/exchange-rates', labelKey: 'nav.exchangeRates' }
];

/** Fixed sidebar width, constant in both density modes (chrome does not compact). */
const SIDEBAR_WIDTH = 248;

/**
 * The application shell template: a fixed deep ink-green left sidebar carrying the serif
 * wordmark, the "Ledger" section label, the quiet nav rows (active row gets a brass left
 * rail), and a bottom-pinned logout; alongside a slim flat paper top bar holding the
 * route-driven page title, the density toggle, and the EN/BG language toggle. Below md the
 * sidebar collapses into a temporary Drawer toggled by a hamburger.
 */
export function AppShell() {
  const { t, i18n } = useTranslation();
  const navigate = useNavigate();
  const location = useLocation();
  const isCompact = useLayoutStore((s) => s.isCompact);
  const toggleDensity = useLayoutStore((s) => s.toggleDensity);
  const logout = useAuthStore((s) => s.logout);
  const [drawerOpen, setDrawerOpen] = useState(false);

  const currentLang = i18n.language.startsWith('bg') ? 'bg' : 'en';
  const languages = ['en', 'bg'] as const;

  function handleLogout() {
    logout();
    navigate('/login', { replace: true });
  }

  const activeItem = navItems.find((item) => location.pathname.startsWith(item.to));
  const pageTitle = activeItem ? t(activeItem.labelKey) : t('app.title');

  function renderSidebar() {
    return (
      <Box sx={{ display: 'flex', flexDirection: 'column', height: '100%' }}>
        <Box sx={{ pt: 3, px: 2.5, pb: 2.5 }}>
          <Typography
            component="span"
            sx={{
              fontFamily: serifFamily,
              fontWeight: 600,
              fontSize: '1.5rem',
              letterSpacing: '-0.01em',
              color: ledgerColors.sidebarWordmark
            }}
          >
            {t('app.title')}
          </Typography>
        </Box>

        <Box sx={{ borderTop: `1px solid ${ledgerColors.sidebarHairline}` }} />

        <Box
          component="span"
          sx={{
            display: 'flex',
            alignItems: 'center',
            gap: 1,
            px: 2.5,
            pt: 2.5,
            pb: 1.5
          }}
        >
          <Box sx={{ width: '2px', height: '10px', bgcolor: ledgerColors.brass }} />
          <Typography
            component="span"
            sx={{
              fontSize: '0.6875rem',
              fontWeight: 600,
              letterSpacing: '0.1em',
              textTransform: 'uppercase',
              color: ledgerColors.sidebarMuted
            }}
          >
            {t('nav.section')}
          </Typography>
        </Box>

        <Box component="nav" sx={{ display: 'flex', flexDirection: 'column', px: 1.5, gap: 0.25 }}>
          {navItems.map((item) => (
            <NavLink
              key={item.to}
              to={item.to}
              onClick={() => setDrawerOpen(false)}
              style={{ textDecoration: 'none' }}
            >
              {({ isActive }) => (
                <Box
                  sx={{
                    position: 'relative',
                    px: 2,
                    py: 1.25,
                    borderRadius: '6px',
                    bgcolor: isActive ? ledgerColors.sidebarActive : 'transparent',
                    transition: 'background-color 120ms ease, color 120ms ease',
                    '&:hover': {
                      bgcolor: isActive ? ledgerColors.sidebarActive : ledgerColors.sidebarHover
                    },
                    '&:hover .nav-label': { color: ledgerColors.sidebarText }
                  }}
                >
                  {isActive && (
                    <Box
                      sx={{
                        position: 'absolute',
                        left: 0,
                        top: 0,
                        bottom: 0,
                        width: '3px',
                        bgcolor: ledgerColors.brass
                      }}
                    />
                  )}
                  <Typography
                    component="span"
                    className="nav-label"
                    sx={{
                      fontSize: '0.9375rem',
                      fontWeight: isActive ? 600 : 500,
                      color: isActive ? ledgerColors.sidebarText : ledgerColors.sidebarMuted
                    }}
                  >
                    {t(item.labelKey)}
                  </Typography>
                </Box>
              )}
            </NavLink>
          ))}
        </Box>

        <Box sx={{ mt: 'auto', px: 1.5, pb: 2.5 }}>
          <Box sx={{ borderTop: `1px solid ${ledgerColors.sidebarHairline}`, mb: 1.5 }} />
          <Box
            role="button"
            tabIndex={0}
            aria-label={t('auth.logout')}
            onClick={handleLogout}
            onKeyDown={(e) => {
              if (e.key === 'Enter' || e.key === ' ') {
                e.preventDefault();
                handleLogout();
              }
            }}
            sx={{
              display: 'flex',
              alignItems: 'center',
              gap: 1.5,
              px: 2,
              py: 1.25,
              borderRadius: '6px',
              cursor: 'pointer',
              color: ledgerColors.sidebarMuted,
              transition: 'background-color 120ms ease, color 120ms ease',
              '&:hover': { bgcolor: ledgerColors.sidebarHover, color: ledgerColors.sidebarText }
            }}
          >
            <LogoutIcon fontSize="small" sx={{ fontSize: '1.125rem' }} />
            <Typography component="span" sx={{ fontSize: '0.9375rem', fontWeight: 500, color: 'inherit' }}>
              {t('auth.logout')}
            </Typography>
          </Box>
        </Box>
      </Box>
    );
  }

  return (
    <Box sx={{ display: 'flex', minHeight: '100vh', bgcolor: 'background.default' }}>
      <Box
        component="aside"
        sx={{
          position: 'fixed',
          top: 0,
          left: 0,
          width: SIDEBAR_WIDTH,
          height: '100vh',
          bgcolor: ledgerColors.sidebar,
          borderRight: `1px solid ${ledgerColors.sidebarHairline}`,
          display: { xs: 'none', md: 'flex' },
          flexDirection: 'column',
          zIndex: (theme) => theme.zIndex.appBar + 1
        }}
      >
        {renderSidebar()}
      </Box>

      <Drawer
        variant="temporary"
        open={drawerOpen}
        onClose={() => setDrawerOpen(false)}
        ModalProps={{ keepMounted: true }}
        sx={{
          display: { xs: 'block', md: 'none' },
          '& .MuiDrawer-paper': {
            width: SIDEBAR_WIDTH,
            boxSizing: 'border-box',
            bgcolor: ledgerColors.sidebar,
            borderRight: `1px solid ${ledgerColors.sidebarHairline}`
          }
        }}
      >
        {renderSidebar()}
      </Drawer>

      <Box sx={{ flexGrow: 1, minWidth: 0, ml: { xs: 0, md: `${SIDEBAR_WIDTH}px` } }}>
        <AppBar position="static">
          <Toolbar sx={{ minHeight: 56, px: 3, gap: 1 }}>
            <IconButton
              onClick={() => setDrawerOpen(true)}
              aria-label={t('nav.menu')}
              size="small"
              sx={{ display: { xs: 'inline-flex', md: 'none' }, mr: 0.5 }}
            >
              <MenuIcon fontSize="small" />
            </IconButton>

            <Typography
              component="span"
              sx={{
                flexGrow: 1,
                fontFamily: serifFamily,
                fontWeight: 500,
                fontSize: '1.125rem',
                color: 'text.primary'
              }}
            >
              {pageTitle}
            </Typography>

            <Tooltip title={isCompact ? t('layout.comfortable') : t('layout.compact')}>
              <IconButton onClick={toggleDensity} aria-label={t('layout.densityToggle')} size="small">
                {isCompact ? <DensitySmallIcon fontSize="small" /> : <DensityMediumIcon fontSize="small" />}
              </IconButton>
            </Tooltip>

            <Divider orientation="vertical" sx={{ height: 20, alignSelf: 'center', mx: 0.5 }} />

            <Tooltip title={t('layout.languageToggle')}>
              <Box sx={{ display: 'flex', alignItems: 'center' }}>
                {languages.map((lng) => {
                  const isActiveLang = currentLang === lng;
                  return (
                    <IconButton
                      key={lng}
                      onClick={() => void i18n.changeLanguage(lng)}
                      aria-label={lng.toUpperCase()}
                      aria-pressed={isActiveLang}
                      size="small"
                      sx={{
                        width: 'auto',
                        px: 0.75,
                        fontSize: '0.8125rem',
                        fontWeight: isActiveLang ? 700 : 600,
                        letterSpacing: '0.08em',
                        borderRadius: 1,
                        color: isActiveLang ? ledgerColors.green : 'text.secondary'
                      }}
                    >
                      {lng.toUpperCase()}
                    </IconButton>
                  );
                })}
              </Box>
            </Tooltip>
          </Toolbar>
        </AppBar>

        <Container maxWidth="xl" sx={{ py: isCompact ? 3 : 4 }}>
          <Outlet />
        </Container>
      </Box>
    </Box>
  );
}
