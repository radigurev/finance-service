import { describe, it, expect } from 'vitest';
import { en } from './locales/en';
import { bg } from './locales/bg';

type Dict = Record<string, unknown>;

/** Recursively collects dotted key paths from a nested locale object. */
function flattenKeys(obj: Dict, prefix = ''): string[] {
  return Object.entries(obj).flatMap(([key, value]) => {
    const path = prefix ? `${prefix}.${key}` : key;
    if (value !== null && typeof value === 'object' && !Array.isArray(value)) {
      return flattenKeys(value as Dict, path);
    }
    return [path];
  });
}

describe('i18n EN/BG parity (SDD-UI-001)', () => {
  const enKeys = flattenKeys(en as unknown as Dict).sort();
  const bgKeys = flattenKeys(bg as unknown as Dict).sort();

  it('every EN key exists in BG', () => {
    const missingInBg = enKeys.filter((k) => !bgKeys.includes(k));
    expect(missingInBg).toEqual([]);
  });

  it('every BG key exists in EN', () => {
    const missingInEn = bgKeys.filter((k) => !enKeys.includes(k));
    expect(missingInEn).toEqual([]);
  });

  it('no locale value is an empty string', () => {
    const emptyEn = flattenKeys(en as unknown as Dict).filter(
      (k) => readValue(en as unknown as Dict, k) === ''
    );
    const emptyBg = flattenKeys(bg as unknown as Dict).filter(
      (k) => readValue(bg as unknown as Dict, k) === ''
    );
    expect({ emptyEn, emptyBg }).toEqual({ emptyEn: [], emptyBg: [] });
  });
});

/** Reads a dotted key path back out of a nested locale object. */
function readValue(obj: Dict, path: string): unknown {
  return path.split('.').reduce<unknown>((acc, segment) => {
    if (acc !== null && typeof acc === 'object') {
      return (acc as Dict)[segment];
    }
    return undefined;
  }, obj);
}
