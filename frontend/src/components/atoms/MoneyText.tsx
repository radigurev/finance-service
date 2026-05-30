import { Box, type BoxProps } from '@mui/material';
import { monoFamily, tabularFeatureSettings, ledgerColors } from '@/shared/theme';

interface MoneyTextProps extends Omit<BoxProps, 'children'> {
  /** The numeric amount to render. */
  amount: number;
  /** ISO currency code rendered after the figure (e.g. "BGN"). */
  currency?: string;
  /** Fraction digits; defaults to 2 (DECIMAL(18,2) semantics). */
  fractionDigits?: number;
  /** BCP-47 locale for grouping/decimal separators. Defaults to the document locale. */
  locale?: string;
}

/**
 * Right-aligned monetary figure in the tabular mono face. Negative amounts render
 * in oxblood. Uses `Intl.NumberFormat` so grouping respects the active locale.
 */
export function MoneyText({
  amount,
  currency,
  fractionDigits = 2,
  locale,
  sx,
  ...rest
}: MoneyTextProps) {
  const formatted = new Intl.NumberFormat(locale, {
    minimumFractionDigits: fractionDigits,
    maximumFractionDigits: fractionDigits
  }).format(amount);

  return (
    <Box
      component="span"
      sx={{
        fontFamily: monoFamily,
        fontFeatureSettings: tabularFeatureSettings,
        display: 'inline-block',
        textAlign: 'right',
        color: amount < 0 ? ledgerColors.oxblood : 'inherit',
        ...sx
      }}
      {...rest}
    >
      {formatted}
      {currency ? ` ${currency}` : ''}
    </Box>
  );
}
