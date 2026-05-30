import { api } from '@/shared/api/axios';
import { toFilterParams, type FilterRequest, type PagedResult } from '@/shared/api/paging';
import type { AccountDto, CreateAccountRequest, UpdateAccountRequest } from './types';

/** Lists accounts as a paged envelope, applying the supplied filter / sort / search. */
export async function searchAccounts(request: FilterRequest): Promise<PagedResult<AccountDto>> {
  const { data } = await api.get<PagedResult<AccountDto>>('/accounts', {
    params: toFilterParams(request)
  });
  return data;
}

/** Creates a new account and returns the persisted DTO. */
export async function createAccount(request: CreateAccountRequest): Promise<AccountDto> {
  const { data } = await api.post<AccountDto>('/accounts', request);
  return data;
}

/**
 * Updates an existing account. The `rowVersion` token captured on read is round-tripped
 * so a stale write is rejected with `CONCURRENT_MODIFICATION` (optimistic concurrency).
 */
export async function updateAccount(
  id: number,
  request: UpdateAccountRequest
): Promise<AccountDto> {
  const { data } = await api.put<AccountDto>(`/accounts/${id}`, request);
  return data;
}
