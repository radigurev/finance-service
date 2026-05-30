import { Button, type ButtonProps } from '@mui/material';

/**
 * Themed button atom. Primary actions use the solid deep-green `contained` variant;
 * secondary actions use hairline-`outlined` or `text`. No shadows, no pills, no gradients —
 * all enforced by the theme; this wrapper exists so feature code imports a single atom.
 */
export function AppButton(props: ButtonProps) {
  return <Button disableElevation {...props} />;
}
