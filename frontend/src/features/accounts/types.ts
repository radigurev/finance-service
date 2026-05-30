/**
 * Account type discriminator. The backend serializes `AccountType` as its numeric
 * value (System.Text.Json default — no string-enum converter is registered), so the
 * wire contract for this field is an integer matching `Finance.Common.Enums.AccountType`.
 */
export enum AccountType {
  Asset = 1,
  Liability = 2,
  Equity = 3,
  Revenue = 4,
  Expense = 5
}

/** All selectable account types in declaration order. */
export const ACCOUNT_TYPES: AccountType[] = [
  AccountType.Asset,
  AccountType.Liability,
  AccountType.Equity,
  AccountType.Revenue,
  AccountType.Expense
];

/** Maps an {@link AccountType} to its i18n label key under `accounts.type_*`. */
export function accountTypeLabelKey(type: AccountType): string {
  return `accounts.type_${AccountType[type]}`;
}

/** Mirrors `Finance.ServiceModel.Accounts.AccountDto`. */
export interface AccountDto {
  id: number;
  code: string;
  name: string;
  type: AccountType;
  parentId: number | null;
  isActive: boolean;
  countryCode: string;
  /** Base64 rowversion token round-tripped on update for optimistic concurrency. */
  rowVersion: string;
}

/** Mirrors `Finance.ServiceModel.Accounts.CreateAccountRequest`. */
export interface CreateAccountRequest {
  code: string;
  name: string;
  type: AccountType;
  parentId: number | null;
}

/** Mirrors `Finance.ServiceModel.Accounts.UpdateAccountRequest`. */
export interface UpdateAccountRequest {
  name: string;
  isActive: boolean;
  rowVersion: string;
}
