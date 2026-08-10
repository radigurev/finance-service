import { EmptyState } from './EmptyState';

interface ForbiddenStateProps {
  /** Serif headline (already translated). */
  title: string;
  /** Supporting line (already translated). */
  description?: string;
}

/**
 * The editorial FORBIDDEN state (SDD-UI-FIN-002 §2.17). A `403` on a list/read request renders this
 * quiet thin-ruled panel — never a blank page, an infinite spinner, a raw status, or a red crash
 * toast on every route change. It carries NO retry action on purpose: a missing permission does not
 * resolve by pressing a button.
 *
 * Each surface must reach its own conclusion from its own response. Because `finance.aging:read` is a
 * SEPARATE permission from `finance.payment:read`, a caller may legitimately see payments and open
 * items while the aging report and balances are forbidden; one surface's permission MUST NOT be
 * inferred from another's.
 */
export function ForbiddenState({ title, description }: ForbiddenStateProps) {
  return <EmptyState title={title} description={description} />;
}
