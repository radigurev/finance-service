import { describe, it, expect, vi, beforeEach } from 'vitest';
import { screen, waitFor } from '@testing-library/react';
import { AxiosError } from 'axios';
import { renderWithProviders } from '@/test/renderWithProviders';
import { ExchangeRatesPage } from './ExchangeRatesPage';
import { fetchLatestRate, fetchRateRange } from '@/features/currencies/api';
import type { ExchangeRateDto } from '@/features/currencies/types';

vi.mock('@/features/currencies/api');
// The currency picker reads its options through the nomenclature hook; stub it so the
// page test makes no stray network calls and the dropdown mounts with a known option.
vi.mock('@/shared/hooks/useNomenclature', () => ({
  useNomenclature: () => ({
    countries: [],
    states: [],
    cities: [],
    currencies: [{ code: 'EUR', name: 'Euro' }],
    isLoading: false
  })
}));

const fetchLatestRateMock = vi.mocked(fetchLatestRate);
const fetchRateRangeMock = vi.mocked(fetchRateRange);

const sampleRate: ExchangeRateDto = {
  currencyIsoCode: 'EUR',
  rate: 1.95583,
  rateDate: '2026-06-01T00:00:00+00:00'
};

describe('ExchangeRatesPage', () => {
  beforeEach(() => {
    fetchLatestRateMock.mockReset();
    fetchRateRangeMock.mockReset();
    fetchLatestRateMock.mockResolvedValue(sampleRate);
    fetchRateRangeMock.mockResolvedValue([sampleRate]);
  });

  it('renders the page title and the look-up controls', async () => {
    renderWithProviders(<ExchangeRatesPage />);

    expect(await screen.findByText('Exchange Rates')).toBeInTheDocument();
    expect(screen.getByText('Look up a rate')).toBeInTheDocument();
    expect(screen.getByText('Latest rate')).toBeInTheDocument();
  });

  it('fetches the latest rate once a currency and date are chosen', async () => {
    const { user } = renderWithProviders(<ExchangeRatesPage />);

    await screen.findByText('Exchange Rates');

    // Pick the currency from the select, then enter an as-of date.
    const selectButton = screen.getByRole('combobox');
    await user.click(selectButton);
    await user.click(await screen.findByText('Euro'));

    const dateInput = document.querySelector('input[type="date"]') as HTMLInputElement;
    await user.type(dateInput, '2026-06-01');

    await waitFor(() => expect(fetchLatestRateMock).toHaveBeenCalled());
    const [currency, date] = fetchLatestRateMock.mock.calls[0];
    expect(currency).toBe('EUR');
    expect(date).toBe('2026-06-01');
  });

  it('does not query until a currency is selected', async () => {
    renderWithProviders(<ExchangeRatesPage />);

    await screen.findByText('Exchange Rates');
    // The empty-selection prompt is shown and no query has fired.
    expect(await screen.findByText('No rate to show yet.')).toBeInTheDocument();
    expect(fetchLatestRateMock).not.toHaveBeenCalled();
  });

  it('surfaces an error toast when the latest-rate query fails', async () => {
    fetchLatestRateMock.mockRejectedValue(
      new AxiosError('boom', undefined, undefined, undefined, {
        status: 500,
        data: { title: 'GENERIC_ERROR' }
      } as never)
    );

    const { user } = renderWithProviders(<ExchangeRatesPage />);

    await screen.findByText('Exchange Rates');
    const selectButton = screen.getByRole('combobox');
    await user.click(selectButton);
    await user.click(await screen.findByText('Euro'));
    const dateInput = document.querySelector('input[type="date"]') as HTMLInputElement;
    await user.type(dateInput, '2026-06-01');

    expect(await screen.findByText(/something went wrong/i)).toBeInTheDocument();
  });
});
