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
  positive: '#1B5E3A',
  /** Solid deep ink-green sidebar surface — the single colored surface in the app. */
  sidebar: '#143026',
  /** Active/selected nav-row fill on the sidebar. */
  sidebarActive: '#1E4234',
  /** Hover fill for inactive nav rows on the sidebar. */
  sidebarHover: '#1A3A2D',
  /** 1px rules inside the sidebar plus its right-edge seam. */
  sidebarHairline: '#2A4A3C',
  /** Primary nav / active text on the sidebar (AAA contrast on the sidebar surface). */
  sidebarText: '#E8EDE9',
  /** Muted section label / inactive nav / resting logout text on the sidebar. */
  sidebarMuted: '#A8BDB1',
  /** Serif wordmark on the sidebar (aliases the paper tone). */
  sidebarWordmark: '#FAF9F6',
  /** Secondary brass accent — DARK SIDEBAR ONLY (active rail / optional section tick). Never a fill. */
  brass: '#C9A227',
  /** Brass tint for LIGHT surfaces only (1px underline / top-rule); never the bright brass on light. */
  brassText: '#7A5E1A'
} as const;
