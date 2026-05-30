import { useEffect, useState } from 'react';
import { InputAdornment } from '@mui/material';
import SearchIcon from '@mui/icons-material/Search';
import { useTranslation } from 'react-i18next';
import { AppTextField } from '@/components/atoms';

interface FilterBarProps {
  /** Current committed search term. */
  value: string;
  /** Fired (debounced) when the user changes the search term. */
  onSearchChange: (term: string) => void;
  /** Placeholder text override; defaults to the shared `filter.searchPlaceholder` key. */
  placeholder?: string;
  /** Extra controls rendered after the search box (e.g. type / status selects). */
  children?: React.ReactNode;
}

/**
 * A debounced search box plus an optional slot for additional filter controls.
 * Debounce keeps server paging requests off every keystroke (SDD-INFRA-005).
 */
export function FilterBar({ value, onSearchChange, placeholder, children }: FilterBarProps) {
  const { t } = useTranslation();
  const [text, setText] = useState(value);

  useEffect(() => {
    setText(value);
  }, [value]);

  useEffect(() => {
    if (text === value) {
      return;
    }
    const handle = window.setTimeout(() => onSearchChange(text), 300);
    return () => window.clearTimeout(handle);
  }, [text, value, onSearchChange]);

  return (
    <>
      <AppTextField
        value={text}
        onChange={(e) => setText(e.target.value)}
        placeholder={placeholder ?? t('filter.searchPlaceholder')}
        sx={{ maxWidth: 320 }}
        InputProps={{
          startAdornment: (
            <InputAdornment position="start">
              <SearchIcon fontSize="small" />
            </InputAdornment>
          )
        }}
      />
      {children}
    </>
  );
}
