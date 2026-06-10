/**
 * Wire contracts for the Invoices feature (SDD-UI-FIN-001). These mirror the .NET
 * `Finance.ServiceModel.Invoices` records field-for-field (SDD-INV-001 §2.3, §2.8, §2.10) —
 * keep names identical so the JSON deserializes without remapping.
 *
 * Enum serialization: the `Finance.Invoices.API` registers NO `JsonStringEnumConverter`, so
 * `System.Text.Json` emits the `Finance.Common.Enums.Invoice*` enums as their numeric values
 * (matching the journal feature). The TypeScript enums below therefore carry the exact integer
 * values declared on the .NET enums (`InvoiceDocumentType` 1–4, `InvoiceDirection` AP=1/AR=2,
 * `InvoiceStatus` Draft=1 … Reversed=5).
 */

/** Mirrors `Finance.Common.Enums.InvoiceDocumentType` (numeric on the wire). */
export enum InvoiceDocumentType {
  PurchaseInvoice = 1,
  SaleInvoice = 2,
  CreditNote = 3,
  DebitNote = 4
}

/** Mirrors `Finance.Common.Enums.InvoiceDirection` (numeric on the wire). */
export enum InvoiceDirection {
  AP = 1,
  AR = 2
}

/** Mirrors `Finance.Common.Enums.InvoiceStatus` (numeric on the wire). */
export enum InvoiceStatus {
  Draft = 1,
  Confirmed = 2,
  Posted = 3,
  Cancelled = 4,
  Reversed = 5
}

/**
 * The UI-only "posting…" affordance (SDD-UI-FIN-001 §2.6): a `Confirmed` invoice whose
 * `journalEntryId` has not yet been linked by the Journal back-event. It is NOT a backend
 * status value — it is derived for display only (see {@link displayStatusKey}).
 */
export const POSTING_PENDING = 'Posting' as const;

/** Maps an {@link InvoiceDocumentType} to its i18n label key under `invoices.type_*`. */
export function documentTypeLabelKey(type: InvoiceDocumentType): string {
  return `invoices.type_${InvoiceDocumentType[type]}`;
}

/** Maps an {@link InvoiceDirection} to its i18n label key under `invoices.direction_*`. */
export function directionLabelKey(direction: InvoiceDirection): string {
  return `invoices.direction_${InvoiceDirection[direction]}`;
}

/**
 * Resolves the i18n status-label key for an invoice, surfacing the posting-pending affordance:
 * a `Confirmed` invoice with no linked `journalEntryId` renders as `invoices.status_Posting`
 * ("posting…"); every other state maps to `invoices.status_<StatusName>` (SDD-UI-FIN-001 §2.6).
 */
export function displayStatusKey(invoice: Pick<InvoiceDto, 'status' | 'journalEntryId'>): string {
  if (invoice.status === InvoiceStatus.Confirmed && !invoice.journalEntryId) {
    return `invoices.status_${POSTING_PENDING}`;
  }
  return `invoices.status_${InvoiceStatus[invoice.status]}`;
}

/** Mirrors `Finance.ServiceModel.Invoices.InvoiceLineDto`. */
export interface InvoiceLineDto {
  lineNumber: number;
  description: string;
  quantity: number;
  unitPrice: number;
  /** Decimal fraction (e.g. `0.20` for 20%). */
  taxRate: number;
  lineNet: number;
  lineTax: number;
  lineGross: number;
}

/** Mirrors `Finance.ServiceModel.Invoices.InvoiceDto`. */
export interface InvoiceDto {
  id: string;
  /** The gapless country-formatted document number assigned at confirm; `null` while `Draft`. */
  documentNumber: string | null;
  documentType: InvoiceDocumentType;
  direction: InvoiceDirection;
  status: InvoiceStatus;
  counterpartyId: string;
  currencyCode: string;
  baseCurrencyCode: string;
  /** ISO 8601 time-zone-aware issue date. */
  issueDate: string;
  /** ISO 8601 time-zone-aware due date (on or after `issueDate`). */
  dueDate: string;
  netTotal: number;
  taxTotal: number;
  grossTotal: number;
  /** On a credit/debit note, the original invoice it corrects; otherwise `null`. */
  correctsInvoiceId: string | null;
  /** The linked journal entry once posted; `null` until the posting handshake completes. */
  journalEntryId: string | null;
  createdAt: string;
  confirmedAt: string | null;
  postedAt: string | null;
  /**
   * Warehouse source-document type when this draft was system-created from a Warehouse event
   * (SDD-INT-WH-001 §2.2); `undefined`/`null` for manual drafts. The v1 `InvoiceDto` may not
   * yet expose this field — origin display is best-effort, never blocking (SDD-UI-FIN-001 §2.10).
   */
  sourceDocumentType?: string | null;
  /** Warehouse source-document identifier; pairs with {@link sourceDocumentType}. */
  sourceDocumentId?: string | null;
  lines: InvoiceLineDto[];
  /** Base64 rowversion token round-tripped on update/confirm/post/cancel for optimistic concurrency. */
  rowVersion: string;
}

/** Mirrors `Finance.ServiceModel.Invoices.InvoiceLineRequest`. */
export interface InvoiceLineRequest {
  description: string;
  quantity: number;
  unitPrice: number;
  taxRate: number;
}

/**
 * Mirrors `Finance.ServiceModel.Invoices.CreateInvoiceRequest`. The base currency is sourced
 * server-side from the country strategy and is NOT part of the request body. `correctsInvoiceId`
 * is set only when issuing a credit/debit note against a posted invoice (SDD-UI-FIN-001 §2.9).
 */
export interface CreateInvoiceRequest {
  documentType: InvoiceDocumentType;
  counterpartyId: string;
  currencyCode: string;
  issueDate: string;
  dueDate: string;
  lines: InvoiceLineRequest[];
  correctsInvoiceId?: string | null;
}

/** Mirrors `Finance.ServiceModel.Invoices.UpdateInvoiceRequest`. */
export interface UpdateInvoiceRequest {
  counterpartyId: string;
  currencyCode: string;
  issueDate: string;
  dueDate: string;
  lines: InvoiceLineRequest[];
  rowVersion: string;
}

/** Mirrors `Finance.ServiceModel.Invoices.ConfirmInvoiceRequest`. */
export interface ConfirmInvoiceRequest {
  rowVersion: string;
}

/** Mirrors `Finance.ServiceModel.Invoices.PostInvoiceRequest`. */
export interface PostInvoiceRequest {
  rowVersion: string;
}

/** Mirrors `Finance.ServiceModel.Invoices.CancelInvoiceRequest`. */
export interface CancelInvoiceRequest {
  reason: string;
  rowVersion: string;
}
