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

/** The subset of the child control's props this component reads or augments. */
interface ControlProps {
  id?: string;
  select?: boolean;
  SelectProps?: { labelId?: string };
}

/**
 * Pairs a small uppercase field label with its control and an optional oxblood
 * error line — the LEDGER form row. Keeps the control's own label off so the
 * group label reads as an editorial caption.
 *
 * Associates the visible label with its input for accessibility, choosing the association mechanism
 * from the KIND of control:
 *
 * - **A text input** (the default) is a labelable element, so a stable id is derived via `useId()`,
 *   injected as the child's `id`, and referenced by the label's `htmlFor`.
 * - **A `select` control** is NOT labelable: MUI renders the `id` on a `<div role="combobox">`, so
 *   `<label for>` pointing at it is invalid HTML (Chrome reports "Incorrect use of
 *   `<label for=FORM_ELEMENT>`") and the association silently does not hold. Those fields instead get
 *   `aria-labelledby` via MUI's `SelectProps.labelId`, which also restores click-to-focus — MUI binds a
 *   click handler on the element carrying that id.
 *
 * Either way an explicit `htmlFor`, or an `id` the child already declares, wins, so every existing
 * call site keeps working unchanged.
 */
export function FormField({ label, required = false, error, htmlFor, children }: FormFieldProps) {
  const generatedId = useId();
  const child = Children.only(children);
  const childProps: ControlProps | undefined = isValidElement(child)
    ? (child.props as ControlProps)
    : undefined;

  const controlId = htmlFor ?? childProps?.id ?? generatedId;
  const labelId = `${controlId}-label`;
  const isSelect: boolean = childProps?.select === true;

  const control =
    isValidElement(child) && (!childProps?.id || isSelect)
      ? cloneElement(child as React.ReactElement<ControlProps>, {
          id: childProps?.id ?? controlId,
          ...(isSelect
            ? { SelectProps: { labelId, ...childProps?.SelectProps } }
            : {})
        })
      : child;

  return (
    <Box sx={{ display: 'flex', flexDirection: 'column', gap: 0.5 }}>
      <Typography
        variant="overline"
        component="label"
        id={labelId}
        htmlFor={isSelect ? undefined : controlId}
      >
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
