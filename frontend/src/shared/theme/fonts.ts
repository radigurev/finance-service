/**
 * Offline-friendly font imports via @fontsource. Imported once for the whole app
 * (referenced from main.tsx). Fraunces = characterful serif headings,
 * Inter = body / UI, IBM Plex Mono = codes / money with tabular figures.
 */
import '@fontsource/fraunces/400.css';
import '@fontsource/fraunces/500.css';
import '@fontsource/fraunces/600.css';
import '@fontsource/inter/400.css';
import '@fontsource/inter/500.css';
import '@fontsource/inter/600.css';
import '@fontsource/ibm-plex-mono/400.css';
import '@fontsource/ibm-plex-mono/500.css';

/** Serif display face for headings and the wordmark. */
export const serifFamily = '"Fraunces", "Georgia", "Times New Roman", serif';

/** UI / body sans face. */
export const sansFamily = '"Inter", "Segoe UI", system-ui, -apple-system, sans-serif';

/** Tabular monospace face for codes, IDs, and money. */
export const monoFamily = '"IBM Plex Mono", "SFMono-Regular", "Consolas", monospace';

/** CSS feature settings that enable tabular (fixed-width) figures for alignment. */
export const tabularFeatureSettings = '"tnum" 1, "lnum" 1';
