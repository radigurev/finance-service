import { useQuery } from '@tanstack/react-query';
import { api } from '@/shared/api/axios';
import { toFilterParams, type FilterRequest, type PagedResult } from '@/shared/api/paging';
import type { JournalEntryDto } from '@/features/journal/types';
import type {
  ApplyPostingRuleRequest,
  CreatePostingRuleRequest,
  PostingRuleDto,
  UpdatePostingRuleRequest
} from './types';

/** Reference-data freshness window — posting rules change rarely (SDD-FIN-006 §2.7). */
const REFERENCE_STALE_TIME = 5 * 60 * 1000;

/** Lists posting rules as a paged envelope, applying the supplied filter / sort / search. */
export async function searchPostingRules(
  request: FilterRequest
): Promise<PagedResult<PostingRuleDto>> {
  const { data } = await api.get<PagedResult<PostingRuleDto>>('/posting-rules', {
    params: toFilterParams(request)
  });
  return data;
}

/** Reads a single posting rule with its ordered lines. */
export async function getPostingRule(id: number): Promise<PostingRuleDto> {
  const { data } = await api.get<PostingRuleDto>(`/posting-rules/${id}`);
  return data;
}

/** Creates a new posting rule with its lines and returns the persisted DTO. */
export async function createPostingRule(
  request: CreatePostingRuleRequest
): Promise<PostingRuleDto> {
  const { data } = await api.post<PostingRuleDto>('/posting-rules', request);
  return data;
}

/**
 * Updates a posting rule (description, active flag, lines) under optimistic concurrency. The
 * captured `rowVersion` is round-tripped so a stale write is rejected with `CONCURRENT_MODIFICATION`.
 */
export async function updatePostingRule(
  id: number,
  request: UpdatePostingRuleRequest
): Promise<PostingRuleDto> {
  const { data } = await api.put<PostingRuleDto>(`/posting-rules/${id}`, request);
  return data;
}

/**
 * Applies a named rule to an amount context, producing a balanced journal entry. With
 * `postImmediately` the entry is posted; otherwise it is left as a Draft (SDD-FIN-006 §2.3).
 */
export async function applyPostingRule(
  request: ApplyPostingRuleRequest
): Promise<JournalEntryDto> {
  const { data } = await api.post<JournalEntryDto>('/posting/apply', request);
  return data;
}

/**
 * Posting-rules listing query (SDD-FIN-006 §2.1). Rules are reference data, so a short
 * `staleTime` is used; the list cache key is `['posting-rules', filter]` and is invalidated
 * client-side on create/update (see {@link usePostingRuleMutations}).
 */
export function usePostingRules(filter: FilterRequest) {
  return useQuery<PagedResult<PostingRuleDto>>({
    queryKey: ['posting-rules', filter],
    queryFn: () => searchPostingRules(filter),
    placeholderData: (prev) => prev,
    staleTime: REFERENCE_STALE_TIME
  });
}

/** Single posting-rule query (SDD-FIN-006 §2.1). Disabled until a positive id is supplied. */
export function usePostingRule(id: number) {
  return useQuery<PostingRuleDto>({
    queryKey: ['posting-rules', 'detail', id],
    queryFn: () => getPostingRule(id),
    enabled: Number.isInteger(id) && id > 0,
    staleTime: REFERENCE_STALE_TIME
  });
}
