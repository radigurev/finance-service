export const en = {
  app: {
    title: 'Finance'
  },
  nav: {
    section: 'Ledger',
    accounts: 'Chart of Accounts',
    currencies: 'Currencies',
    exchangeRates: 'Exchange Rates'
  },
  layout: {
    compact: 'Compact density',
    comfortable: 'Comfortable density',
    densityToggle: 'Toggle density',
    languageToggle: 'Switch language'
  },
  common: {
    save: 'Save',
    saving: 'Saving…',
    cancel: 'Cancel',
    confirm: 'Confirm',
    edit: 'Edit',
    delete: 'Delete',
    close: 'Close',
    back: 'Back'
  },
  filter: {
    searchPlaceholder: 'Search…'
  },
  table: {
    rowsPerPage: 'Rows per page'
  },
  auth: {
    login: 'Sign in',
    username: 'Username',
    password: 'Password',
    submit: 'Sign in',
    logout: 'Sign out'
  },
  accounts: {
    title: 'Chart of Accounts',
    newAccount: 'New account',
    createTitle: 'New account',
    editTitle: 'Edit account',
    code: 'Code',
    name: 'Name',
    type: 'Type',
    parent: 'Parent',
    active: 'Active',
    country: 'Country',
    statusActive: 'Active',
    statusInactive: 'Inactive',
    created: 'Account created.',
    updated: 'Account updated.',
    empty: 'No accounts yet.',
    emptyHint: 'Create the first account to start building the chart.',
    type_Asset: 'Asset',
    type_Liability: 'Liability',
    type_Equity: 'Equity',
    type_Revenue: 'Revenue',
    type_Expense: 'Expense',
    validation: {
      codeRequired: 'Code is required.',
      codeTooLong: 'Code must be 20 characters or fewer.',
      nameRequired: 'Name is required.',
      nameTooLong: 'Name must be 200 characters or fewer.'
    }
  },
  currencies: {
    title: 'Currencies',
    newCurrency: 'New currency',
    createTitle: 'New currency',
    editTitle: 'Edit currency',
    searchPlaceholder: 'Search by code or name…',
    isoCode: 'ISO code',
    name: 'Name',
    symbol: 'Symbol',
    active: 'Active',
    statusActive: 'Active',
    statusInactive: 'Inactive',
    created: 'Currency created.',
    updated: 'Currency updated.',
    deactivated: 'Currency deactivated.',
    deactivate: 'Deactivate',
    deactivateTitle: 'Deactivate currency',
    deactivateMessage: 'Deactivate {{code}}? It will no longer appear in selection lists, but historical records keep it.',
    empty: 'No currencies yet.',
    emptyHint: 'Add the first currency to start recording amounts.',
    validation: {
      isoCodeInvalid: 'The ISO code must be exactly three uppercase letters.',
      nameRequired: 'Name is required.',
      nameTooLong: 'Name must be 100 characters or fewer.',
      symbolTooLong: 'Symbol must be 5 characters or fewer.'
    }
  },
  exchangeRates: {
    title: 'Exchange Rates',
    queryOverline: 'Query',
    queryHeading: 'Look up a rate',
    resultOverline: 'Result',
    latestHeading: 'Latest rate',
    modeLatest: 'On a date',
    modeRange: 'Over a range',
    currency: 'Currency',
    asOfDate: 'As of date',
    from: 'From',
    to: 'To',
    date: 'Date',
    rate: 'Rate',
    asOf: 'As of {{date}}',
    invalidRange: 'The start date must be on or before the end date.',
    noSelectionTitle: 'No rate to show yet.',
    noSelectionLatestHint: 'Pick a currency and a date to see the latest rate on or before it.',
    noSelectionRangeHint: 'Pick a currency and a valid date range to list the rates.'
  },
  errors: {
    GENERIC_ERROR: 'Something went wrong. Please try again.',
    VALIDATION_FAILED: 'Some fields are invalid. Please review and try again.',
    CONCURRENT_MODIFICATION: 'This record was changed by someone else. Reload and try again.',
    PAGE_SIZE_TOO_LARGE: 'The requested page size is too large.',
    INVALID_FILTER_FIELD: 'That field cannot be filtered.',
    INVALID_SORT_FIELD: 'That field cannot be sorted.',
    INVALID_OPERATOR: 'Invalid filter operator.',
    INVALID_FILTER_VALUE: 'Invalid filter value.',
    INVALID_CREDENTIALS: 'Invalid username or password.',
    DUPLICATE_ACCOUNT_CODE: 'An account with this code already exists.',
    INVALID_PARENT_ACCOUNT: 'Invalid parent account.',
    ACCOUNT_NOT_FOUND: 'Account not found.',
    INVALID_ACCOUNT_CODE: 'Invalid account code.',
    INVALID_ACCOUNT_TYPE: 'Invalid account type.',
    ACCOUNT_INACTIVE: 'Account is inactive.',
    ACCOUNT_HAS_ENTRIES: 'Account has posted entries and cannot be deleted.',
    INVALID_CURRENCY_CODE: 'The currency code must be exactly three uppercase letters.',
    DUPLICATE_CURRENCY_CODE: 'A currency with this code already exists.',
    CURRENCY_NOT_FOUND: 'Currency not found.',
    EXCHANGE_RATE_NOT_FOUND: 'No exchange rate found for the selected currency and date.',
    INVALID_DATE_RANGE: 'The start date must be on or before the end date.',
    WAREHOUSE_NOMENCLATURE_UNREACHABLE: 'Reference data is temporarily unavailable. Please try again shortly.',
    INVALID_CURRENCY_NAME: 'The currency name is required and must be 100 characters or fewer.',
    INVALID_CURRENCY_SYMBOL: 'The currency symbol must be 5 characters or fewer.'
  }
};
