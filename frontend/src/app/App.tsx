import { Routes, Route, Navigate } from 'react-router-dom';
import { AppShell } from './AppShell';
import { LoginPage } from '@/features/auth/LoginPage';
import { AccountsListPage } from '@/features/accounts/AccountsListPage';
import { RequireAuth } from '@/features/auth/RequireAuth';

export function App() {
  return (
    <Routes>
      <Route path="/login" element={<LoginPage />} />
      <Route
        path="/"
        element={
          <RequireAuth>
            <AppShell />
          </RequireAuth>
        }
      >
        <Route index element={<Navigate to="/accounts" replace />} />
        <Route path="accounts" element={<AccountsListPage />} />
      </Route>
      <Route path="*" element={<Navigate to="/" replace />} />
    </Routes>
  );
}
