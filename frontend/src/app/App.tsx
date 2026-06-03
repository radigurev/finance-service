import { Routes, Route, Navigate } from 'react-router-dom';
import { AppShell } from '@/components/templates';
import {
  LoginPage,
  AccountsListPage,
  CurrenciesListPage,
  ExchangeRatesPage,
  JournalEntriesListPage,
  FiscalPeriodsListPage,
  TrialBalancePage,
  AccountLedgerPage
} from '@/components/pages';
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
        <Route path="journal-entries" element={<JournalEntriesListPage />} />
        <Route path="periods" element={<FiscalPeriodsListPage />} />
        <Route path="general-ledger" element={<TrialBalancePage />} />
        <Route path="general-ledger/accounts/:accountId" element={<AccountLedgerPage />} />
        <Route path="currencies" element={<CurrenciesListPage />} />
        <Route path="exchange-rates" element={<ExchangeRatesPage />} />
      </Route>
      <Route path="*" element={<Navigate to="/" replace />} />
    </Routes>
  );
}
