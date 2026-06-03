import { Children, cloneElement, isValidElement, useId } from 'react';
import { Box, Typography } from '@mui/material';

interface FormFieldProps {
  /** Small uppercase label above the control. */
  label: string;
  /** Marks the field as required with a quiet indicator. */
  required?: boolean;
  /** Error text rendered in oxblood beneath the control. */
  error?: string;
  /**
   * Optional explicit id for the control. When omitted, a stable unique id is generated
   * and injected into the single child control so the label's `htmlFor` resolves (a11y).
   */
  htmlFor?: string;
  /** The input control (an AppTextField, Select, etc.). */
  children: React.ReactNode;
}

/**
 * Pairs a small uppercase field label with its control and an optional oxblood
 * error line — the LEDGER form row. Keeps the control's own label off so the
 * group label reads as an editorial caption.
 *
 * Associates the visible label with its input for accessibility: a stable id is
 * derived via `useId()`, set on the label's `htmlFor`, and injected as the child
 * control's `id` (unless the child already declares one, or an explicit `htmlFor`
 * is supplied). This keeps every existing call site working unchanged.
 */
export function FormField({ label, required = false, error, htmlFor, children }: FormFieldProps) {
  const generatedId = useId();
  const child = Children.only(children);
  const childId = isValidElement(child) ? (child.props as { id?: string }).id : undefined;

  const controlId = htmlFor ?? childId ?? generatedId;

  const control =
    isValidElement(child) && !childId
      ? cloneElement(child as React.ReactElement<{ id?: string }>, { id: controlId })
      : child;

  return (
    <Box sx={{ display: 'flex', flexDirection: 'column', gap: 0.5 }}>
      <Typography variant="overline" component="label" htmlFor={controlId}>
        {label}
        {required ? <Box component="span" sx={{ color: 'error.main', ml: 0.5 }}>*</Box> : null}
      </Typography>
      {control}
      {error ? (
        <Typography variant="caption" sx={{ color: 'error.main' }}>
          {error}
        </Typography>
      ) : null}
    </Box>
  );
}
