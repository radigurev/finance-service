import { Box } from '@mui/material';
import { PageHeader } from '@/components/molecules';

interface ListPageTemplateProps {
  /** Page title rendered in the serif header. */
  title: string;
  /** Optional uppercase eyebrow above the title. */
  overline?: string;
  /** Optional supporting subtitle. */
  subtitle?: string;
  /** Header-right action cluster (search box, primary button). */
  actions?: React.ReactNode;
  /** A filter row rendered between the header and the table. */
  toolbar?: React.ReactNode;
  /** The list body (typically a DataTable). */
  children: React.ReactNode;
}

/**
 * Standard listing layout: serif PageHeader, an optional toolbar/filter row, then the
 * list body. Keeps every list view structurally identical so the LEDGER rhythm holds.
 */
export function ListPageTemplate({
  title,
  overline,
  subtitle,
  actions,
  toolbar,
  children
}: ListPageTemplateProps) {
  return (
    <Box>
      <PageHeader title={title} overline={overline} subtitle={subtitle} actions={actions} />
      {toolbar ? (
        <Box sx={{ display: 'flex', alignItems: 'center', gap: 1.5, flexWrap: 'wrap', mb: 2 }}>
          {toolbar}
        </Box>
      ) : null}
      {children}
    </Box>
  );
}
