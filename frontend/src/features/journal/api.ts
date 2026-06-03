import { api } from '@/shared/api/axios';
import { toFilterParams, type FilterRequest, type PagedResult } from '@/shared/api/paging';
import type {
  CreateJournalEntryRequest,
  JournalEntryDto,
  PostJournalEntryRequest,
  ReverseJournalEntryRequest,
  UpdateJournalEntryRequest
} from './types';

/** Lists journal entries as a paged envelope, applying the supplied filter / sort / search. */
export async function searchJournalEntries(
  request: FilterRequest
): Promise<PagedResult<JournalEntryDto>> {
  const { data } = await api.get<PagedResult<JournalEntryDto>>('/journal-entries', {
    params: toFilterParams(request)
  });
  return data;
}

/** Reads a single journal entry with its lines. */
export async function getJournalEntry(id: string): Promise<JournalEntryDto> {
  const { data } = await api.get<JournalEntryDto>(`/journal-entries/${id}`);
  return data;
}

/** Creates a balanced draft journal entry and returns the persisted DTO. */
export async function createJournalEntry(
  request: CreateJournalEntryRequest
): Promise<JournalEntryDto> {
  const { data } = await api.post<JournalEntryDto>('/journal-entries', request);
  return data;
}

/**
 * Updates a draft journal entry. The `rowVersion` captured on read is round-tripped so a
 * stale write is rejected with `CONCURRENT_MODIFICATION` (optimistic concurrency).
 */
export async function updateJournalEntry(
  id: string,
  request: UpdateJournalEntryRequest
): Promise<JournalEntryDto> {
  const { data } = await api.put<JournalEntryDto>(`/journal-entries/${id}`, request);
  return data;
}

/** Deletes a draft journal entry. */
export async function deleteJournalEntry(id: string): Promise<void> {
  await api.delete(`/journal-entries/${id}`);
}

/** Posts a draft journal entry (Draft → Posted). */
export async function postJournalEntry(
  id: string,
  request: PostJournalEntryRequest
): Promise<JournalEntryDto> {
  const { data } = await api.post<JournalEntryDto>(`/journal-entries/${id}/post`, request);
  return data;
}

/** Reverses a posted journal entry (Posted → Reversed), returning the new reversal entry. */
export async function reverseJournalEntry(
  id: string,
  request: ReverseJournalEntryRequest
): Promise<JournalEntryDto> {
  const { data } = await api.post<JournalEntryDto>(`/journal-entries/${id}/reverse`, request);
  return data;
}
