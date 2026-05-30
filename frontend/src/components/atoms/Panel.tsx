import { Box, type BoxProps } from '@mui/material';
import { useLayoutStore } from '@/shared/stores/layout';

interface PanelProps extends BoxProps {
  /** Removes the inner padding (e.g. when wrapping a full-bleed table). */
  flush?: boolean;
}

/**
 * The LEDGER surface primitive: a 1px hairline-bordered white card with NO elevation.
 * Replaces shadowed MUI Cards. Padding follows the active density unless `flush`.
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
        borderRadius: 2,
        p: padding,
        ...sx
      }}
      {...rest}
    >
      {children}
    </Box>
  );
}
