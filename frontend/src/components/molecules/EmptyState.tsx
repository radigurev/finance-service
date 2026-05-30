import { Box, Typography } from '@mui/material';
import { serifFamily } from '@/shared/theme';

interface EmptyStateProps {
  /** Serif headline (already translated). */
  title: string;
  /** Supporting line (already translated). */
  description?: string;
  /** A single quiet action (e.g. a text/outlined button). */
  action?: React.ReactNode;
  /** Renders the editorial thin-ruled box; set false to embed inside a frame that already rules. */
  framed?: boolean;
}

/**
 * Editorial empty / error state: a centered serif message inside an optional
 * thin-ruled box with a single understated action. No illustrations, no emoji.
 */
export function EmptyState({ title, description, action, framed = true }: EmptyStateProps) {
  return (
    <Box
      sx={{
        display: 'flex',
        flexDirection: 'column',
        alignItems: 'center',
        justifyContent: 'center',
        textAlign: 'center',
        gap: 1,
        px: 4,
        py: 6,
        ...(framed
          ? { border: '1px solid', borderColor: 'divider', borderRadius: 2, backgroundColor: 'background.paper' }
          : {})
      }}
    >
      <Typography sx={{ fontFamily: serifFamily, fontWeight: 500, fontSize: '1.25rem' }}>
        {title}
      </Typography>
      {description ? (
        <Typography variant="body2" sx={{ color: 'text.secondary', maxWidth: 420 }}>
          {description}
        </Typography>
      ) : null}
      {action ? <Box sx={{ mt: 1 }}>{action}</Box> : null}
    </Box>
  );
}
