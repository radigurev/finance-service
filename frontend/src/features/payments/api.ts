import { api } from '@/shared/api/axios';
import { toFilterParams, type FilterRequest, type PagedResult } from '@/shared/api/paging';
import type {
  AgingReportDto,
  AgingReportQuery,
  AllocatePaymentRequest,
  AllocatePaymentResultDto,
  CancelPaymentRequest,
  ConfirmPaymentRequest,
  CounterpartyBalanceDto,
  CounterpartyBalanceQuery,
  CreatePaymentRequest,
  DeallocatePaymentResultDto,
  OpenItemDto,
  OpenItemQuery,
  PaymentAllocationDto,
  PaymentDto,
  PostPaymentRequest,
  ReversePaymentRequest,
  UpdatePaymentRequest
} from './types';

/**
 * Typed Payments API client (SDD-UI-FIN-002 §2; SDD-PAY-001/-002/-003). All fifteen shipped
 * endpoints across the five controllers, always through the shared axios instance — which attaches
 * the bearer token and a fresh `X-Correlation-ID` per request (SDD-INFRA-001). Never a raw
 * `axios`/`fetch`.
 */

/** Arguments for a deallocate call; both are OPTIONAL query parameters on the shipped route. */
export interface DeallocateArgs {
  rowVersion?: string;
  reason?: string;
}

/**
 * Merges `toFilterParams(request)` with the `OpenItemQueryRequest` narrowings. `toFilterParams`
 * emits only `Page`/`PageSize`/`Search`/`Filters[i].*`/`Sort[i].*`, but `GET /open-items` binds
 * BOTH a `FilterRequest` AND the narrowing record from the SAME query string, so the caller must
 * merge them (SDD-UI-FIN-002 §1.4 trap 8). Exported for wire-contract tests.
 */
export function buildOpenItemParams(
  query: OpenItemQuery,
  request: FilterRequest
): Record<string, string> {
  const params: Record<string, string> = { ...toFilterParams(request) };

  if (query.asOfDate) {
    params['asOfDate'] = query.asOfDate;
  }
  if (query.direction) {
    params['direction'] = query.direction;
  }
  if (query.counterpartyId) {
    params['counterpartyId'] = query.counterpartyId;
  }
  if (query.currencyCode) {
    params['currencyCode'] = query.currencyCode;
  }
  if (query.overdueOnly) {
    params['overdueOnly'] = 'true';
  }

  return params;
}

/**
 * Builds the `GET /api/v1/aging` query string. Two shipped-contract constraints are load-bearing:
 *
 * 1. `buckets` is `int[]?` and binds ONLY as REPEATED values — `?buckets=30&buckets=60&buckets=90`.
 *    Axios' default array serialization emits `buckets[]=30`, which ASP.NET Core will not bind, and
 *    a comma-separated `?buckets=30,60,90` would need a model binder that does not exist. Returning
 *    a `URLSearchParams` (which axios serializes verbatim) gives exact repeat semantics
 *    (SDD-UI-FIN-002 §1.4 trap 7).
 * 2. The endpoint binds NO `FilterRequest` at all — no `Page`/`PageSize`/`Sort` is ever emitted
 *    (§1.4 trap 8, trap 15).
 *
 * `buckets` is omitted entirely when the operator has not customized it, so the server applies its
 * documented default of `30, 60, 90` (§2.14). Exported for wire-contract tests.
 */
export function buildAgingParams(query: AgingReportQuery): URLSearchParams {
  const params = new URLSearchParams();

  params.set('asOfDate', query.asOfDate);
  params.set('direction', query.direction);

  if (query.counterpartyId) {
    params.set('counterpartyId', query.counterpartyId);
  }
  if (query.currencyCode) {
    params.set('currencyCode', query.currencyCode);
  }
  (query.buckets ?? []).forEach((boundary) => params.append('buckets', String(boundary)));

  return params;
}

/**
 * Merges `toFilterParams(request)` with the `CounterpartyBalanceQueryRequest` narrowings — this
 * endpoint binds both from one query string, like `/open-items` (§1.4 trap 8). Exported for tests.
 */
export function buildCounterpartyBalanceParams(
  query: CounterpartyBalanceQuery,
  request: FilterRequest
): Record<string, string> {
  const params: Record<string, string> = { ...toFilterParams(request) };

  params['asOfDate'] = query.asOfDate;
  params['direction'] = query.direction;
  if (query.currencyCode) {
    params['currencyCode'] = query.currencyCode;
  }

  return params;
}

/**
 * Builds the deallocate QUERY parameters. `rowVersion` and `reason` are `[FromQuery]` on the shipped
 * `PaymentAllocationsController.Deallocate` — a `DELETE` here carries NO body (§1.4 trap 9). Both are
 * optional; when `rowVersion` is omitted the server still guards with the token it loads inside the
 * transaction. Exported for wire-contract tests.
 */
export function buildDeallocateParams(args: DeallocateArgs): Record<string, string> {
  const params: Record<string, string> = {};

  if (args.rowVersion) {
    params['rowVersion'] = args.rowVersion;
  }
  if (args.reason) {
    params['reason'] = args.reason;
  }

  return params;
}

/** Lists payments as a paged envelope, applying the supplied filter / sort / search (§2.1). */
export async function searchPayments(request: FilterRequest): Promise<PagedResult<PaymentDto>> {
  const { data } = await api.get<PagedResult<PaymentDto>>('/payments', {
    params: toFilterParams(request)
  });
  return data;
}

/** Reads a single payment with its amounts, FX, allocation figures, and lifecycle timestamps (§2.2). */
export async function getPayment(id: string): Promise<PaymentDto> {
  const { data } = await api.get<PaymentDto>(`/payments/${id}`);
  return data;
}

/** Creates a draft payment (201) and returns the persisted DTO — the server's `baseAmount` wins. */
export async function createPayment(request: CreatePaymentRequest): Promise<PaymentDto> {
  const { data } = await api.post<PaymentDto>('/payments', request);
  return data;
}

/**
 * Updates a draft payment. The `rowVersion` captured on read is round-tripped so a stale write is
 * rejected with `CONCURRENT_MODIFICATION`; `documentType` is sent UNCHANGED so the server can reject
 * a change with `INVALID_PAYMENT_DOCUMENT_TYPE` (§2.5).
 */
export async function updatePayment(
  id: string,
  request: UpdatePaymentRequest
): Promise<PaymentDto> {
  const { data } = await api.put<PaymentDto>(`/payments/${id}`, request);
  return data;
}

/** Hard-deletes a draft payment (§2.6). */
export async function deletePayment(id: string): Promise<void> {
  await api.delete(`/payments/${id}`);
}

/** Confirms a draft (Draft → Confirmed); the server assigns the gapless `RCT`/`PAY` number (§2.7). */
export async function confirmPayment(
  id: string,
  request: ConfirmPaymentRequest
): Promise<PaymentDto> {
  const { data } = await api.post<PaymentDto>(`/payments/${id}/confirm`, request);
  return data;
}

/**
 * Completes the asynchronous Confirm→Post handshake for a confirmed payment. It never posts a
 * journal entry itself; when the back-event has not landed it answers `PAYMENT_POSTING_PENDING`
 * (409) and RE-ENQUEUES `PaymentConfirmedEvent` as the documented, bounded recovery path (§2.7).
 */
export async function postPayment(id: string, request: PostPaymentRequest): Promise<PaymentDto> {
  const { data } = await api.post<PaymentDto>(`/payments/${id}/post`, request);
  return data;
}

/**
 * Cancels a payment — **`Draft` ONLY**. `Confirmed → Cancelled` was deliberately removed from
 * `AllowedNextStates`, so a confirmed-or-later payment answers `INVALID_PAYMENT_STATE_TRANSITION`;
 * the correct correction there is a reversal (§2.8).
 */
export async function cancelPayment(
  id: string,
  request: CancelPaymentRequest
): Promise<PaymentDto> {
  const { data } = await api.post<PaymentDto>(`/payments/${id}/cancel`, request);
  return data;
}

/** Reverses a posted payment (a sign-flipped entry); blocked while `allocatedAmount > 0` (§2.9). */
export async function reversePayment(
  id: string,
  request: ReversePaymentRequest
): Promise<PaymentDto> {
  const { data } = await api.post<PaymentDto>(`/payments/${id}/reverse`, request);
  return data;
}

/** Lists one payment's allocation rows as a paged, invoice-enriched envelope (§2.10). */
export async function searchPaymentAllocations(
  paymentId: string,
  request: FilterRequest
): Promise<PagedResult<PaymentAllocationDto>> {
  const { data } = await api.get<PagedResult<PaymentAllocationDto>>(
    `/payments/${paymentId}/allocations`,
    { params: toFilterParams(request) }
  );
  return data;
}

/**
 * Allocates a payment against open invoices with an EXPLICIT, all-or-nothing item list. Answers
 * **200 (not 201)** with no `Location`; the result carries the new `rowVersion`, the new
 * allocated/unallocated figures, and every affected invoice's settlement state, so no follow-up read
 * is needed (§1.4 traps 10/11, §2.11).
 */
export async function allocatePayment(
  paymentId: string,
  request: AllocatePaymentRequest
): Promise<AllocatePaymentResultDto> {
  const { data } = await api.post<AllocatePaymentResultDto>(
    `/payments/${paymentId}/allocations`,
    request
  );
  return data;
}

/**
 * Releases one allocation row. `rowVersion` and `reason` travel as QUERY parameters — this `DELETE`
 * has NO body (§1.4 trap 9). The result re-seeds the payment `rowVersion` (§2.12).
 */
export async function deallocatePayment(
  paymentId: string,
  allocationId: number,
  args: DeallocateArgs = {}
): Promise<DeallocatePaymentResultDto> {
  const { data } = await api.delete<DeallocatePaymentResultDto>(
    `/payments/${paymentId}/allocations/${allocationId}`,
    { params: buildDeallocateParams(args) }
  );
  return data;
}

/** Reads the open-items worklist, merging the `FilterRequest` with the query narrowings (§2.13). */
export async function searchOpenItems(
  query: OpenItemQuery,
  request: FilterRequest
): Promise<PagedResult<OpenItemDto>> {
  const { data } = await api.get<PagedResult<OpenItemDto>>('/open-items', {
    params: buildOpenItemParams(query, request)
  });
  return data;
}

/**
 * Reads the bucketed AP/AR aging report (`finance.aging:read` — a SEPARATE permission from
 * `finance.payment:read`). One `AgingReportDto` carrying ALL rows; no paging contract exists (§2.14).
 */
export async function getAgingReport(query: AgingReportQuery): Promise<AgingReportDto> {
  const { data } = await api.get<AgingReportDto>('/aging', {
    params: buildAgingParams(query)
  });
  return data;
}

/** Reads the paged counterparty-balance roll-up (`finance.aging:read`) (§2.15). */
export async function searchCounterpartyBalances(
  query: CounterpartyBalanceQuery,
  request: FilterRequest
): Promise<PagedResult<CounterpartyBalanceDto>> {
  const { data } = await api.get<PagedResult<CounterpartyBalanceDto>>('/counterparty-balances', {
    params: buildCounterpartyBalanceParams(query, request)
  });
  return data;
}
