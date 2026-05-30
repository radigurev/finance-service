/**
 * The LEDGER palette — a calm, print-like, editorial finance aesthetic.
 * Light only (dark mode is deferred). NO bright Material blue anywhere.
 */
export const ledgerColors = {
  /** Warm paper page background. */
  paper: '#FAF9F6',
  /** White surface used inside Panels, dialogs, and table frames. */
  surface: '#FFFFFF',
  /** Near-black ink for primary text. */
  ink: '#18181B',
  /** Warm gray for secondary/label text. */
  inkSoft: '#57534E',
  /** Hairline rule / divider color. */
  hairline: '#E7E5E0',
  /** Deep ledger green — the single accent. */
  green: '#1B5E3A',
  /** Soft green tint for selection / active backgrounds. */
  greenSoft: '#DCEAE0',
  /** Oxblood for danger / errors. */
  oxblood: '#9F1239',
  /** Amber for warnings. */
  amber: '#B45309',
  /** Positive (reuses ledger green). */
  positive: '#1B5E3A'
} as const;
