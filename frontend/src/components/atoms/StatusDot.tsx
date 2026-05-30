import { Box, Typography } from '@mui/material';
import { ledgerColors } from '@/shared/theme';

type StatusTone = 'positive' | 'neutral' | 'danger' | 'warning';

interface StatusDotProps {
  /** Tone that selects the dot color. */
  tone: StatusTone;
  /** Adjacent label text (already translated by the caller). */
  label: string;
}

const toneColor: Record<StatusTone, string> = {
  positive: ledgerColors.green,
  neutral: ledgerColors.inkSoft,
  danger: ledgerColors.oxblood,
  warning: ledgerColors.amber
};

/**
 * A small colored dot plus a quiet label — the LEDGER status indicator that
 * deliberately replaces large MUI Chips.
 */
export function StatusDot({ tone, label }: StatusDotProps) {
  return (
    <Box sx={{ display: 'inline-flex', alignItems: 'center', gap: 0.75 }}>
      <Box
        sx={{
          width: 8,
          height: 8,
          borderRadius: '50%',
          backgroundColor: toneColor[tone],
          flexShrink: 0
        }}
      />
      <Typography variant="body2" sx={{ color: 'text.primary' }}>
        {label}
      </Typography>
    </Box>
  );
}
