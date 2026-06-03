# CHG-ENH-001 — Frontend Sidebar Shell + Soft Depth/Color ("Ledger, Bound in Green")

> Created: 2026-05-31
> Author: Frontend
> Status: Implemented (merged into SDD-UI-001 v3 in Batch 9, commit 3db0357 — 2026-06-03)
> Related specs: SDD-UI-001 (authoritative system spec being enhanced), SDD-UI-002, SDD-NOM-001, SDD-INFRA-001
> Originating ticket: "Tweak the frontend — it's bland — and move the navbar to the left side." (user-confirmed; design panel produced the locked token set)

---

## 1. Summary

This change enhances the existing Finance SPA shell (`SDD-UI-001`) by (a) relocating navigation from the current top `AppBar` into a fixed **left sidebar** while retaining a **slim flat top bar** for the page title, density toggle, and EN/BG language toggle; and (b) introducing a **bounded relaxation** of three previously absolute `SDD-UI-001` §2.8 visual prohibitions — the no-shadow ban, the single-accent-color rule, and the paper-only-surfaces rule — to add tasteful depth and a single colored surface ("Ledger, Bound in Green"). The change is **non-breaking** and **additive/visual**: no API, event, or database contract changes; the ledger aesthetic's editorial character is preserved.

## 2. Motivation

The Batch 8 ledger shell is correct but reads as "bland": the top-bar-only navigation crowds the page title row, every surface is flat paper-on-paper with no depth cue, and the single green accent leaves the chrome monochromatic. The user explicitly asked to make it less bland and move the navbar to the left. A design panel converted that direction into a concrete, accessibility-checked token set ("Ledger, Bound in Green") so the relaxations stay bounded rather than degrading into generic Material defaults (gradients, glow, Material blue). The deep ink-green sidebar restores hierarchy, a warm brass accent provides a second non-fill mark, and soft ink-green-tinted shadows on a few surface families add depth without abandoning the print-like aesthetic.

## 3. Scope

### In scope
- Move primary navigation (Chart of Accounts, Currencies, Exchange Rates) from the top `AppBar` into a fixed **left sidebar** (248px, constant in both density modes).
- Retain a **slim flat top bar** (56px) in the content column holding the route-driven page title, the density toggle, and the EN/BG language toggle. Logout moves to the **bottom of the sidebar**.
- Below `md` (900px) the sidebar collapses into a temporary MUI `Drawer` toggled by a hamburger at the left of the top bar.
- **Bounded relaxation** of three `SDD-UI-001` §2.8 prohibitions per §4 below: soft shadows on exactly four surface families; one secondary accent (brass) as non-fill marks only; one colored surface (the sidebar).
- New palette tokens + three shadow tokens encoded into the theme.
- One new i18n key: `nav.menu` (EN/BG).
- A new theme file `frontend/src/shared/theme/shadows.ts`.

### Out of scope (explicit)
- Any backend, API, event, or database change.
- Dark mode or a theme/brand-color switcher (still deferred to a later phase).
- The `SDD-UI-002` dialog-vs-page dual-mode / `isPageMode` machinery (still deferred).
- Any change to data flow, routing targets, auth, correlation IDs, error mapping, density propagation semantics, or the feature surfaces themselves (Accounts, Currencies, Exchange Rates behavior is unchanged).
- New colors, fills, or shadows beyond the locked token set. Brass MUST NOT become a fill/button/link/stat-card/money figure/semantic state.

## 4. Proposed Behavior

All rules below are additive to or replace specific clauses of `SDD-UI-001` §2.8. Each is independently testable by the `ui-validate` agent against the running SPA or against the built theme object.

### 4.1 Left sidebar (MUST)
- Navigation MUST render in a fixed left sidebar of **248px** width that is **constant in both density modes** (compact and comfortable do NOT change sidebar width).
- The sidebar surface MUST be a single flat solid `#143026` (deep ink-green) with a **1px `#2A4A3C` right-edge seam** and **NO shadow** (the seam, not a shadow, separates it from content).
- The sidebar MUST show, top to bottom: a serif "Finance" wordmark (`#FAF9F6`, `t('app.title')`), a 1px `#2A4A3C` rule under it, an uppercase "Ledger" section label (`#A8BDB1`, `t('nav.section')`), then the nav items.
- Nav items (Chart of Accounts, Currencies, Exchange Rates) MUST render with: resting text `#A8BDB1`; hover text `#E8EDE9` over a `#1A3A2D` fill; **active = `#1E4234` fill + `#E8EDE9` weight-600 text + a 3px brass `#C9A227` left rail**.
- Logout MUST be pinned at the bottom of the sidebar (`margin-top:auto`) with a 1px `#2A4A3C` separator above it. Logout MUST NOT appear in the top bar.
- Below `md` (900px) the sidebar MUST become a temporary MUI `Drawer` toggled by a hamburger `IconButton` at the left of the top bar, with `aria-label={t('nav.menu')}`.

### 4.2 Slim top bar (MUST)
- The top bar MUST be a slim **flat** bar in the content column (`margin-left: 248px` on `md+`), reusing the existing `MuiAppBar` override: paper bg `#FAF9F6`, ink text, `boxShadow: none`, 1px bottom hairline `#E7E5E0`. Height **56px**.
- Left of the top bar MUST hold the **route-driven page title** in serif Fraunces `1.125rem / 500`.
- The right cluster MUST hold, in order: the density toggle `IconButton`, a 1px×20px hairline divider, and the EN/BG language toggle (active locale in green `#1B5E3A`, the other in `#57534E`).
- The top bar MUST NOT contain navigation links or logout, and MUST stay flat (no shadow).

### 4.3 Soft shadows — bounded relaxation of the no-shadow ban (MUST)
- Soft, low-opacity (`0.04–0.12`), diffuse, **zero-spread**, ink-green-tinted, two-layer shadows MUST be permitted on **exactly four surface families**: Card/Panel, Dialog, Menu, Popover.
- The shadows MUST use the three locked shadow tokens (`card`, `dialog`, `menu`; Popover reuses the `menu` token).
- No glow, no spread, no colored ring MUST be emitted. **No other surface gets a shadow** — the top bar, DataGrid, FilterBar, inputs, and `Divider` MUST stay flat.

### 4.4 Brass accent — bounded relaxation of the single-accent rule (MUST)
- Exactly **one** new secondary color (brass) MUST be introduced, used ONLY as tiny **non-fill marks**: the 3px sidebar active rail, an optional 2px section-label tick (dark sidebar), and an optional 1px underline/top-rule on light surfaces.
- Bright brass `#C9A227` MUST be used on the **dark sidebar only**; on light surfaces brass MUST appear only as the darker tint `#7A5E1A` (1px underline/top-rule). Bright brass MUST NOT appear on light surfaces.
- Brass MUST NEVER be a fill, button, link, stat-card, money figure, or semantic state color. Green `#1B5E3A` MUST remain the sole primary action / selection / positive color.

### 4.5 Colored surface — bounded relaxation of the paper-only rule (MUST)
- Exactly **one** solid colored surface MUST exist — the left sidebar `#143026` (plus its derived tones `#1E4234`, `#1A3A2D`, `#2A4A3C`). No other surface MAY depart from paper / white / hairline.
- The sidebar MUST use a flat 1px hairline seam, not a shadow, to separate from content.

### 4.6 Retained prohibitions (MUST NOT)
- Gradients MUST NOT appear anywhere (the sidebar is a single flat solid).
- Glassmorphism / backdrop blur MUST NOT be used.
- Material blue (`#1976d2` / `#1565c0` family) MUST NOT appear; the secondary accent is brass, never blue/teal.
- Glowing / neon accents and colored glow shadows MUST NOT be used (shadows are diffuse, low-opacity, ink-green-tinted).
- Pill-shaped buttons MUST NOT be used; buttons keep `borderRadius` 4.
- Emoji MUST NOT appear in UI.
- Gradient stat-cards MUST NOT be used; cards are flat white + hairline border + soft shadow only.
- Mono tabular money/codes, serif Fraunces headings, Inter body, hairline `#E7E5E0` rules MUST be retained.
- Light mode only MUST be retained (the dark sidebar is colored chrome, not a dark scheme).

### 4.7 Density flow (MUST — unchanged invariant)
- Density (compact / comfortable) MUST continue to flow from `useLayoutStore` to content surfaces (DataGrid, Panels, spacing) per `SDD-UI-001` §2.4. The sidebar width is exempt (constant 248px), but the top bar density toggle and all content surfaces MUST still honor the store.

### 4.8 i18n (MUST)
- The new key `nav.menu` MUST exist in BOTH `en.ts` (`Menu`) and `bg.ts` (`Меню`) in the same PR. All other shell strings reuse existing keys (`app.title`, `nav.section`, `auth.logout`, `layout.*`).

## 5. Affected Specs

| Spec ID | Section | Change |
|---|---|---|
| `SDD-UI-001` | §2.8 "AppBar/shell (MUST)" | Replace the top-bar-nav clause with the sidebar + slim-top-bar model (§4.1–§4.2). |
| `SDD-UI-001` | §2.8 "Surfaces (MUST)" + "Prohibited (MUST NOT)" | Rewrite so the three relaxations are explicit and bounded (§4.3–§4.5); retained prohibitions stay hard MUST NOTs (§4.6). |
| `SDD-UI-001` | §2.8 (palette + shadows) | Add the new palette tokens and three shadow tokens to the theme description. |
| `SDD-UI-001` | §2.6 Folder structure | Add `src/shared/theme/shadows.ts`. |
| `SDD-UI-001` | §5 Versioning | Add `v3 — Frontend sidebar shell + soft depth/color (CHG-ENH-001, non-breaking, additive/visual)`. |
| `SDD-UI-001` | §6 Test Plan | Add/adjust the theme + shell tests (see §10 below). |

## 6. Database Changes

None. This change is purely frontend/visual.

## 7. API Changes

None. No new endpoints, no contract changes, no new backend error codes.

- i18n keys added to `frontend/src/shared/i18n/locales/{en,bg}.ts`: `nav.menu` (`Menu` / `Меню`).

## 8. Event Contract Changes

None.

## 9. Frontend Impact

### Files that change
- `frontend/src/shared/theme/palette.ts` — add the new sidebar tones, brass tokens, and shadow tint.
- `frontend/src/shared/theme/shadows.ts` — **NEW** — exports the three locked shadow tokens (`card`, `dialog`, `menu`).
- `frontend/src/shared/theme/index.ts` — re-export the new tokens / theme.
- `frontend/src/shared/theme/theme.ts` — wire shadows onto exactly the four surface-family component overrides (`MuiCard`/Panel, `MuiDialog`, `MuiMenu`, `MuiPopover`); keep `MuiAppBar`/DataGrid flat.
- `frontend/src/components/templates/AppShell.tsx` — restructure: fixed left sidebar (nav + brass active rail + bottom logout), slim flat top bar (title + density + EN/BG), `<md` Drawer + hamburger.
- `frontend/src/shared/i18n/locales/en.ts` — add `nav.menu = Menu`.
- `frontend/src/shared/i18n/locales/bg.ts` — add `nav.menu = Меню`.

> Note: the Batch 8 theme currently lives in `frontend/src/shared/stores/theme.ts` per `SDD-UI-001` §6 implementation notes; this change formalizes the theme under `frontend/src/shared/theme/` (palette/shadows/theme/index). The implementator phase aligns the file location with §2.6.

### Routes / dialogs / pages
- No new routes. `/accounts`, `/currencies`, and the exchange-rate view are unchanged. Create/edit stays dialog-based (`SDD-UI-002` still deferred).

### Modal vs page mode considerations
- No change. The `isPageMode` machinery remains deferred to `SDD-UI-002`.

## 10. Testing

UI behavior is verified by the `ui-validate` agent (Chrome DevTools MCP). Theme invariants that can be asserted against the built theme object are `[Unit]`.

### Test Plan delta (what `ui-validate` / theme tests MUST now check)
- `Shell_NavInLeftSidebar_SlimTopBarHasTitleDensityLang` — nav renders in the left sidebar; the slim top bar holds page title + density toggle + EN/BG toggle (no nav links, no logout). `[Integration]`
- `Shell_ActiveNavItem_HasBrassLeftRail` — the active nav row shows `#1E4234` fill + weight-600 text + a 3px brass `#C9A227` left rail. `[Integration]`
- `Shell_Logout_PinnedAtSidebarBottom` — logout sits at the sidebar bottom (separator above), not in the top bar. `[Integration]`
- `Shell_BelowMd_SidebarBecomesDrawerWithHamburger` — under 900px the sidebar collapses to a temporary Drawer toggled by a hamburger with `aria-label = t('nav.menu')`. `[Integration]`
- `Theme_SoftShadowOnCardDialogMenuOnly_TopBarAndGridFlat` — soft ink-green-tinted shadows appear on Card/Panel, Dialog, Menu, Popover ONLY; top bar and DataGrid emit no shadow. `[Unit]`
- `Theme_BrassUsedAsNonFillMarkOnly_NeverAsFill` — brass appears only as the sidebar active rail / section tick / light-surface hairline; never as a fill, button, link, stat-card, money figure, or semantic state. `[Unit]`
- `Theme_SingleColoredSurfaceIsSidebar_OthersPaper` — exactly one solid colored surface (`#143026` sidebar + derived tones); all other surfaces stay paper/white/hairline. `[Unit]`
- `Theme_NoMaterialBluePresent` — built palette contains no `#1976d2`/`#1565c0`; primary is `#1B5E3A` (retained). `[Unit]`
- `DensityToggle_Compact_TightensContentSurfaces_SidebarWidthConstant` — density still flows to DataGrid/Panel spacing; sidebar width stays 248px in both modes. `[Integration]`
- `I18n_AllKeysExistInEnAndBg_IncludingNavMenu` — every key including `nav.menu` exists in both locale files. `[Unit]`
- `Console_NoErrorsAfterShellNavigation` — no console errors after navigating the new shell. `[Integration]`
- `Network_NoUnexpectedRequestsFromShellChrome` — the chrome change introduces no new network calls. `[Integration]`

## 11. Rollout

- Feature flag: none — visual/additive, ships in one batch.
- Migration ordering: none (no DB/event changes).
- Downstream coordination: none (Warehouse-side unaffected). Accountant/user review of the new chrome is advisory only.

## 12. Risks

- **Contrast/accessibility:** the design panel verified sidebar text contrast (`#E8EDE9` 11.97:1 AAA, `#A8BDB1` 7.15:1 AAA on `#143026`). Implementation MUST NOT substitute unverified tones.
- **Scope creep into Material defaults:** the bounded relaxations could drift into generic shadows/colors. The retained prohibitions (§4.6) and the `[Unit]` theme tests guard against this.
- **Theme file relocation:** moving theme code from `stores/theme.ts` to `shared/theme/*` could regress overrides if not carefully ported; covered by the `Theme_*` unit tests.

## 13. Open Questions

- Should the optional 2px brass section-label tick and the optional 1px brass underline on light surfaces ship in this batch or stay as design options? Current spec marks them OPTIONAL (MAY); default is to ship the active rail only and treat the ticks/underline as MAY.
