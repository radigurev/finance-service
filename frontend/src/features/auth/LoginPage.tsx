import { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { Box, Button, Card, CardContent, Stack, TextField, Typography, Alert } from '@mui/material';
import { useTranslation } from 'react-i18next';
import { api } from '@/shared/api/axios';
import { useAuthStore } from '@/shared/stores/auth';
import { getApiErrorMessage } from '@/shared/utils/getApiErrorMessage';

interface LoginResponse {
  accessToken: string;
  refreshToken: string;
  username: string;
}

export function LoginPage() {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const setSession = useAuthStore((s) => s.setSession);
  const [username, setUsername] = useState('');
  const [password, setPassword] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    setError(null);
    setSubmitting(true);
    try {
      const { data } = await api.post<LoginResponse>('/auth/login', { username, password });
      setSession(data);
      navigate('/', { replace: true });
    } catch (err) {
      setError(getApiErrorMessage(err, t));
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
        bgcolor: 'grey.100'
      }}
    >
      <Card sx={{ minWidth: 360 }}>
        <CardContent>
          <Typography variant="h5" sx={{ mb: 3 }}>
            {t('auth.login')}
          </Typography>
          <form onSubmit={handleSubmit}>
            <Stack spacing={2}>
              <TextField
                label={t('auth.username')}
                value={username}
                onChange={(e) => setUsername(e.target.value)}
                autoFocus
                required
              />
              <TextField
                label={t('auth.password')}
                type="password"
                value={password}
                onChange={(e) => setPassword(e.target.value)}
                required
              />
              {error && <Alert severity="error">{error}</Alert>}
              <Button type="submit" variant="contained" disabled={submitting}>
                {t('auth.submit')}
              </Button>
            </Stack>
          </form>
        </CardContent>
      </Card>
    </Box>
  );
}
