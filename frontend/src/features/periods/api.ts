import { api } from '@/shared/api/axios';
import { toFilterParams, type FilterRequest, type PagedResult } from '@/shared/api/paging';
import type {
  ClosePeriodRequest,
  CreatePeriodRequest,
  FiscalPeriodDto,
  GeneratePeriodsRequest,
  ReopenPeriodRequest
} from './types';

/** Lists fiscal periods as a paged envelope, applying the supplied filter / sort / search. */
export async function searchPeriods(request: FilterRequest): Promise<PagedResult<FiscalPeriodDto>> {
  const { data } = await api.get<PagedResult<FiscalPeriodDto>>('/periods', {
    params: toFilterParams(request)
  });
  return data;
}

/** Reads a single fiscal period. */
export async function getPeriod(id: number): Promise<FiscalPeriodDto> {
  const { data } = await api.get<FiscalPeriodDto>(`/periods/${id}`);
  return data;
}

/** Reads the period whose date range contains the supplied calendar date. */
export async function getPeriodByDate(date: string): Promise<FiscalPeriodDto> {
  const { data } = await api.get<FiscalPeriodDto>('/periods/by-date', { params: { date } });
  return data;
}

/** Generates the full set of fiscal periods for a year, returning the generated periods. */
export async function generatePeriods(
  request: GeneratePeriodsRequest
): Promise<FiscalPeriodDto[]> {
  const { data } = await api.post<FiscalPeriodDto[]>('/periods/generate', request);
  return data;
}

/** Creates a single fiscal period explicitly and returns the persisted DTO. */
export async function createPeriod(request: CreatePeriodRequest): Promise<FiscalPeriodDto> {
  const { data } = await api.post<FiscalPeriodDto>('/periods', request);
  return data;
}

/** Closes a fiscal period (Open → Closed). */
export async function closePeriod(
  id: number,
  request: ClosePeriodRequest
): Promise<FiscalPeriodDto> {
  const { data } = await api.post<FiscalPeriodDto>(`/periods/${id}/close`, request);
  return data;
}

/** Reopens a fiscal period (Closed → Open). */
export async function reopenPeriod(
  id: number,
  request: ReopenPeriodRequest
): Promise<FiscalPeriodDto> {
  const { data } = await api.post<FiscalPeriodDto>(`/periods/${id}/reopen`, request);
  return data;
}
