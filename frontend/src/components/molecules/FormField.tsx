import { Box, Typography } from '@mui/material';

interface FormFieldProps {
  /** Small uppercase label above the control. */
  label: string;
  /** Marks the field as required with a quiet indicator. */
  required?: boolean;
  /** Error text rendered in oxblood beneath the control. */
  error?: string;
  /** The input control (an AppTextField, Select, etc.). */
  children: React.ReactNode;
}

/**
 * Pairs a small uppercase field label with its control and an optional oxblood
 * error line — the LEDGER form row. Keeps the control's own label off so the
 * group label reads as an editorial caption.
 */
export function FormField({ label, required = false, error, children }: FormFieldProps) {
  return (
    <Box sx={{ display: 'flex', flexDirection: 'column', gap: 0.5 }}>
      <Typography variant="overline" component="label">
        {label}
        {required ? <Box component="span" sx={{ color: 'error.main', ml: 0.5 }}>*</Box> : null}
      </Typography>
      {children}
      {error ? (
        <Typography variant="caption" sx={{ color: 'error.main' }}>
          {error}
        </Typography>
      ) : null}
    </Box>
  );
}
