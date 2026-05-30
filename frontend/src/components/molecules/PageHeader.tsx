import { Box, Typography } from '@mui/material';
import { serifFamily } from '@/shared/theme';

interface PageHeaderProps {
  /** Page title in the serif display face (already translated). */
  title: string;
  /** Optional uppercase eyebrow above the title. */
  overline?: string;
  /** Optional supporting subtitle. */
  subtitle?: string;
  /** Right-aligned actions (search box, primary button, etc.). */
  actions?: React.ReactNode;
}

/**
 * The list / page header: a large Fraunces title with an optional eyebrow and a
 * right-aligned action cluster, closed by a single hairline rule beneath.
 */
export function PageHeader({ title, overline, subtitle, actions }: PageHeaderProps) {
  return (
    <Box sx={{ mb: 3 }}>
      <Box
        sx={{
          display: 'flex',
          alignItems: 'flex-end',
          justifyContent: 'space-between',
          flexWrap: 'wrap',
          gap: 2,
          pb: 1.5
        }}
      >
        <Box>
          {overline ? (
            <Typography variant="overline" sx={{ display: 'block', lineHeight: 1.4 }}>
              {overline}
            </Typography>
          ) : null}
          <Typography
            component="h1"
            sx={{ fontFamily: serifFamily, fontWeight: 500, fontSize: '2rem', lineHeight: 1.1 }}
          >
            {title}
          </Typography>
          {subtitle ? (
            <Typography variant="body2" sx={{ color: 'text.secondary', mt: 0.5 }}>
              {subtitle}
            </Typography>
          ) : null}
        </Box>
        {actions ? (
          <Box sx={{ display: 'flex', alignItems: 'center', gap: 1.5, flexShrink: 0 }}>{actions}</Box>
        ) : null}
      </Box>
      <Box sx={{ height: '1px', backgroundColor: 'divider' }} />
    </Box>
  );
}
