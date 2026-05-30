import { forwardRef } from 'react';
import { TextField, type TextFieldProps } from '@mui/material';
import { useLayoutStore } from '@/shared/stores/layout';

/**
 * Outlined text-field atom with hairline borders and a green focus ring (from the theme).
 * Field size follows the active density — never hard-code `size="small"` at call sites.
 * Forwards its ref so it composes with react-hook-form's `register`.
 */
export const AppTextField = forwardRef<HTMLDivElement, TextFieldProps>(function AppTextField(
  props,
  ref
) {
  const isCompact = useLayoutStore((s) => s.isCompact);
  return (
    <TextField
      ref={ref}
      fullWidth
      variant="outlined"
      size={isCompact ? 'small' : 'medium'}
      {...props}
    />
  );
});
