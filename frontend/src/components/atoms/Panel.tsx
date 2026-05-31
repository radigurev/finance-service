import { Box, type BoxProps } from '@mui/material';
import { useLayoutStore } from '@/shared/stores/layout';
import { ledgerShadows } from '@/shared/theme';

interface PanelProps extends BoxProps {
  /** Removes the inner padding (e.g. when wrapping a full-bleed table). */
  flush?: boolean;
}

/**
 * The LEDGER surface primitive: a 1px hairline-bordered white card carrying a soft,
 * ink-green-tinted depth shadow (the bounded relaxation from SDD-UI-001 §2.8). The
 * border keeps the crisp ledger edge; the shadow adds the requested depth. Padding
 * follows the active density unless `flush`.
 */
export function Panel({ flush = false, sx, children, ...rest }: PanelProps) {
  const isCompact = useLayoutStore((s) => s.isCompact);
  const padding = flush ? 0 : isCompact ? 2 : 3;

  return (
    <Box
      sx={{
        backgroundColor: 'background.paper',
        border: '1px solid',
        borderColor: 'divider',
        borderRadius: 1,
        boxShadow: ledgerShadows.card,
        p: padding,
        ...sx
      }}
      {...rest}
    >
      {children}
    </Box>
  );
}
