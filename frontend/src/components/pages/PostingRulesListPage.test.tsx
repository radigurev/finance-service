import { describe, it, expect, vi, beforeEach } from 'vitest';
import { screen } from '@testing-library/react';
import { AxiosError } from 'axios';
import { renderWithProviders } from '@/test/renderWithProviders';
import { PostingRulesListPage } from './PostingRulesListPage';
import { usePostingRules } from '@/features/postingRules/api';
import { type PostingRuleDto } from '@/features/postingRules/types';
import type { PagedResult } from '@/shared/api/paging';

vi.mock('@/features/postingRules/api');
// The apply-rule dialog mounts a nomenclature-backed currency picker; stub the hook so the
// page test makes no stray network calls.
vi.mock('@/shared/hooks/useNomenclature', () => ({
  useNomenclature: () => ({
    countries: [],
    states: [],
    cities: [],
    currencies: [],
    isLoading: false
  })
}));

const usePostingRulesMock = vi.mocked(usePostingRules);

function pagedRules(items: PostingRuleDto[]): PagedResult<PostingRuleDto> {
  return { items, totalCount: items.length, page: 1, pageSize: 50 };
}

const sampleRule: PostingRuleDto = {
  id: 1,
  ruleKey: 'SALE_INVOICE',
  description: 'Sales invoice posting',
  countryCode: 'BG',
  isActive: true,
  lines: [],
  rowVersion: 'AAAA'
};

type PostingRulesQuery = ReturnType<typeof usePostingRules>;

/** Builds a minimal TanStack-Query-shaped return value for the mocked hook. */
function queryResult(overrides: Partial<PostingRulesQuery>): PostingRulesQuery {
  return {
    data: undefined,
    error: null,
    isFetching: false,
    ...overrides
  } as PostingRulesQuery;
}

describe('PostingRulesListPage', () => {
  beforeEach(() => {
    usePostingRulesMock.mockReset();
  });

  it('renders the page title and a returned posting-rule row', async () => {
    usePostingRulesMock.mockReturnValue(queryResult({ data: pagedRules([sampleRule]) }));

    renderWithProviders(<PostingRulesListPage />);

    expect(await screen.findByText('Posting Rules')).toBeInTheDocument();
    expect(await screen.findByText('Sales invoice posting')).toBeInTheDocument();
    expect(screen.getByText('SALE_INVOICE')).toBeInTheDocument();
  });

  it('issues the server-side filter request with page 1 and the default page size', async () => {
    usePostingRulesMock.mockReturnValue(queryResult({ data: pagedRules([sampleRule]) }));

    renderWithProviders(<PostingRulesListPage />);

    await screen.findByText('Posting Rules');
    expect(usePostingRulesMock).toHaveBeenCalled();
    const filter = usePostingRulesMock.mock.calls[0][0];
    expect(filter.page).toBe(1);
    expect(filter.pageSize).toBe(50);
  });

  it('shows the empty state when no rules are returned', async () => {
    usePostingRulesMock.mockReturnValue(queryResult({ data: pagedRules([]) }));

    renderWithProviders(<PostingRulesListPage />);

    expect(await screen.findByText('Posting Rules')).toBeInTheDocument();
    expect(usePostingRulesMock).toHaveBeenCalled();
  });

  it('surfaces an error toast when the query fails', async () => {
    usePostingRulesMock.mockReturnValue(
      queryResult({
        error: new AxiosError('boom', undefined, undefined, undefined, {
          status: 500,
          data: { title: 'GENERIC_ERROR' }
        } as never)
      })
    );

    renderWithProviders(<PostingRulesListPage />);

    expect(await screen.findByText(/something went wrong/i)).toBeInTheDocument();
  });
});
