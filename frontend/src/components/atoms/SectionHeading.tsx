import { Box, Typography } from '@mui/material';
import { serifFamily } from '@/shared/theme';

interface SectionHeadingProps {
  /** Heading text (already translated by the caller). */
  children: React.ReactNode;
  /** Optional small uppercase eyebrow label above the heading. */
  overline?: string;
  /** Optional right-aligned action node (e.g. a button). */
  action?: React.ReactNode;
}

/**
 * Editorial section header: a Fraunces serif title with a thin rule beneath and an
 * optional uppercase eyebrow. The thin rule is the LEDGER structural divider.
 */
export function SectionHeading({ children, overline, action }: SectionHeadingProps) {
  return (
    <Box sx={{ mb: 2 }}>
      <Box
        sx={{
          display: 'flex',
          alignItems: 'flex-end',
          justifyContent: 'space-between',
          gap: 2,
          pb: 1
        }}
      >
        <Box>
          {overline ? (
            <Typography variant="overline" sx={{ display: 'block', lineHeight: 1.4 }}>
              {overline}
            </Typography>
          ) : null}
          <Typography
            component="h2"
            sx={{ fontFamily: serifFamily, fontWeight: 500, fontSize: '1.5rem', lineHeight: 1.2 }}
          >
            {children}
          </Typography>
        </Box>
        {action ? <Box sx={{ flexShrink: 0 }}>{action}</Box> : null}
      </Box>
      <Box sx={{ height: '1px', backgroundColor: 'divider' }} />
    </Box>
  );
}
