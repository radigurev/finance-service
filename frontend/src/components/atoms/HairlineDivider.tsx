import { Divider, type DividerProps } from '@mui/material';

/**
 * A single thin hairline rule. Thin wrapper over MUI Divider so feature code
 * imports the LEDGER primitive instead of styling dividers ad hoc.
 */
export function HairlineDivider(props: DividerProps) {
  return <Divider {...props} sx={{ borderColor: 'divider', ...props.sx }} />;
}
