import { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { Box, Stack, Typography } from '@mui/material';
import { useTranslation } from 'react-i18next';
import { api } from '@/shared/api/axios';
import { useAuthStore } from '@/shared/stores/auth';
import { notification } from '@/shared/notifications/notification';
import { getApiErrorMessage } from '@/shared/utils/getApiErrorMessage';
import { Panel, AppButton, AppTextField } from '@/components/atoms';
import { FormField } from '@/components/molecules';
import { serifFamily } from '@/shared/theme';

interface LoginResponse {
  accessToken: string;
  refreshToken: string;
  username: string;
}

/**
 * Sign-in page in the LEDGER aesthetic: a hairline-framed Panel on the warm paper
 * background, a serif wordmark, and quiet outlined inputs. Errors surface via the
 * notification facade rather than inline raw messages.
 */
export function LoginPage() {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const setSession = useAuthStore((s) => s.setSession);
  const [username, setUsername] = useState('');
  const [password, setPassword] = useState('');
  const [submitting, setSubmitting] = useState(false);

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    setSubmitting(true);
    try {
      const { data } = await api.post<LoginResponse>('/auth/login', { username, password });
      setSession(data);
      navigate('/', { replace: true });
    } catch (err) {
      notification.error(getApiErrorMessage(err, t));
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <Box
      sx={{
        minHeight: '100vh',
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'center',
        bgcolor: 'background.default',
        px: 2
      }}
    >
      <Panel sx={{ width: '100%', maxWidth: 380, p: 4 }}>
        <Typography
          component="span"
          sx={{ fontFamily: serifFamily, fontWeight: 600, fontSize: '1.75rem', letterSpacing: '-0.01em' }}
        >
          {t('app.title')}
        </Typography>
        <Typography variant="overline" sx={{ display: 'block', mt: 0.5, mb: 3 }}>
          {t('auth.login')}
        </Typography>

        <form onSubmit={handleSubmit} noValidate>
          <Stack spacing={2.5}>
            <FormField label={t('auth.username')} required>
              <AppTextField
                value={username}
                onChange={(e) => setUsername(e.target.value)}
                autoFocus
                autoComplete="username"
              />
            </FormField>
            <FormField label={t('auth.password')} required>
              <AppTextField
                type="password"
                value={password}
                onChange={(e) => setPassword(e.target.value)}
                autoComplete="current-password"
              />
            </FormField>
            <AppButton
              type="submit"
              variant="contained"
              fullWidth
              disabled={submitting || !username || !password}
            >
              {submitting ? t('common.saving') : t('auth.submit')}
            </AppButton>
          </Stack>
        </form>
      </Panel>
    </Box>
  );
}
