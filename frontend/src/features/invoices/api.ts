import { api } from '@/shared/api/axios';
import { toFilterParams, type FilterRequest, type PagedResult } from '@/shared/api/paging';
import type {
  CancelInvoiceRequest,
  ConfirmInvoiceRequest,
  CreateInvoiceRequest,
  InvoiceDto,
  PostInvoiceRequest,
  UpdateInvoiceRequest
} from './types';

/**
 * Typed Invoices API client (SDD-UI-FIN-001 §2; SDD-INV-001 §5). Every call goes through the
 * shared axios instance, which attaches the bearer token and a fresh `X-Correlation-ID` per
 * request (SDD-INFRA-001) — never a raw `axios`/`fetch`.
 */

/** Lists invoices as a paged envelope, applying the supplied filter / sort / search (SDD-INFRA-005). */
export async function searchInvoices(request: FilterRequest): Promise<PagedResult<InvoiceDto>> {
  const { data } = await api.get<PagedResult<InvoiceDto>>('/invoices', {
    params: toFilterParams(request)
  });
  return data;
}

/** Reads a single invoice with its lines and computed totals. */
export async function getInvoice(id: string): Promise<InvoiceDto> {
  const { data } = await api.get<InvoiceDto>(`/invoices/${id}`);
  return data;
}

/** Creates a draft invoice and returns the persisted DTO (server totals are authoritative). */
export async function createInvoice(request: CreateInvoiceRequest): Promise<InvoiceDto> {
  const { data } = await api.post<InvoiceDto>('/invoices', request);
  return data;
}

/**
 * Updates a draft invoice. The `rowVersion` captured on read is round-tripped so a stale write
 * is rejected with `CONCURRENT_MODIFICATION` (optimistic concurrency).
 */
export async function updateInvoice(
  id: string,
  request: UpdateInvoiceRequest
): Promise<InvoiceDto> {
  const { data } = await api.put<InvoiceDto>(`/invoices/${id}`, request);
  return data;
}

/** Deletes a draft invoice. */
export async function deleteInvoice(id: string): Promise<void> {
  await api.delete(`/invoices/${id}`);
}

/** Confirms a draft invoice (Draft → Confirmed); the server assigns the gapless document number. */
export async function confirmInvoice(
  id: string,
  request: ConfirmInvoiceRequest
): Promise<InvoiceDto> {
  const { data } = await api.post<InvoiceDto>(`/invoices/${id}/confirm`, request);
  return data;
}

/** Completes posting of a confirmed invoice (Confirmed → Posted) once the JE is linked. */
export async function postInvoice(id: string, request: PostInvoiceRequest): Promise<InvoiceDto> {
  const { data } = await api.post<InvoiceDto>(`/invoices/${id}/post`, request);
  return data;
}

/** Cancels (voids) a draft or confirmed invoice; a non-empty reason is mandatory. */
export async function cancelInvoice(
  id: string,
  request: CancelInvoiceRequest
): Promise<InvoiceDto> {
  const { data } = await api.post<InvoiceDto>(`/invoices/${id}/cancel`, request);
  return data;
}
