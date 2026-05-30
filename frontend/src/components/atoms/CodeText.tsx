import { Box, type BoxProps } from '@mui/material';
import { monoFamily, tabularFeatureSettings } from '@/shared/theme';

interface CodeTextProps extends BoxProps {
  children: React.ReactNode;
}

/**
 * Renders account codes, IDs, and other reference figures in the IBM Plex Mono
 * tabular face so columns align character-for-character.
 */
export function CodeText({ children, sx, ...rest }: CodeTextProps) {
  return (
    <Box
      component="span"
      sx={{
        fontFamily: monoFamily,
        fontFeatureSettings: tabularFeatureSettings,
        letterSpacing: '0.01em',
        ...sx
      }}
      {...rest}
    >
      {children}
    </Box>
  );
}
