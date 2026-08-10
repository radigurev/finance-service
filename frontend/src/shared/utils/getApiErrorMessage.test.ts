import { describe, it, expect } from 'vitest';
import { AxiosError } from 'axios';
import i18n from '@/shared/i18n/i18n';
import { getApiErrorMessage } from './getApiErrorMessage';

const t = i18n.getFixedT('en');
const tBg = i18n.getFixedT('bg');

/** The developer-English `detail` ASP.NET sends with a permission failure — never user-facing copy. */
const FORBIDDEN_DETAIL = "Caller lacks permission 'finance.payment:confirm'.";

function axiosErrorWith(data: unknown, status = 400): AxiosError {
  return new AxiosError('request failed', undefined, undefined, undefined, {
    status,
    data
  } as never);
}

describe('getApiErrorMessage', () => {
  it('translates a single error code from the ProblemDetails errors dictionary', () => {
    const err = axiosErrorWith({ errors: { field: ['CONCURRENT_MODIFICATION'] } }, 409);

    const message = getApiErrorMessage(err, t);

    expect(message).toBe(t('errors.CONCURRENT_MODIFICATION'));
    expect(message).not.toBe('CONCURRENT_MODIFICATION');
  });

  it('joins multiple error codes with a semicolon', () => {
    const err = axiosErrorWith({
      errors: { a: ['DUPLICATE_ACCOUNT_CODE'], b: ['INVALID_PARENT_ACCOUNT'] }
    });

    const message = getApiErrorMessage(err, t);

    expect(message).toContain(t('errors.DUPLICATE_ACCOUNT_CODE'));
    expect(message).toContain(t('errors.INVALID_PARENT_ACCOUNT'));
    expect(message).toContain('; ');
  });

  it('falls back to the unmapped code itself when no translation exists', () => {
    const err = axiosErrorWith({ errors: { x: ['SOME_UNMAPPED_CODE'] } });

    expect(getApiErrorMessage(err, t)).toBe('SOME_UNMAPPED_CODE');
  });

  it('uses the title (translated) when there is no errors dictionary', () => {
    const err = axiosErrorWith({ title: 'GENERIC_ERROR', detail: 'dev detail' }, 500);

    expect(getApiErrorMessage(err, t)).toBe(t('errors.GENERIC_ERROR'));
  });

  it('falls back to detail when the title has no translation', () => {
    const err = axiosErrorWith({ title: 'UNTRANSLATED_TITLE', detail: 'human readable detail' });

    expect(getApiErrorMessage(err, t)).toBe('human readable detail');
  });

  it('translates a 403 FORBIDDEN and never renders the developer detail (SDD-UI-FIN-002 §2.17)', () => {
    const err = axiosErrorWith({ title: 'FORBIDDEN', detail: FORBIDDEN_DETAIL }, 403);

    const message = getApiErrorMessage(err, t);

    expect(message).toBe('You do not have permission to perform this action.');
    expect(message).not.toBe(FORBIDDEN_DETAIL);
    expect(message).not.toContain('finance.payment:confirm');
  });

  it("translates ASP.NET's default `Forbidden` reason phrase as well as the machine code", () => {
    // The framework short-circuits some 403s with its own title casing, which must not fall through
    // to `detail` just because it is spelled differently from the SCREAMING_SNAKE_CASE code.
    const err = axiosErrorWith({ title: 'Forbidden', detail: FORBIDDEN_DETAIL }, 403);

    expect(getApiErrorMessage(err, t)).toBe('You do not have permission to perform this action.');
    expect(getApiErrorMessage(err, t)).not.toContain('Caller lacks permission');
  });

  it('translates a 403 into Bulgarian under the BG locale', () => {
    const err = axiosErrorWith({ title: 'FORBIDDEN', detail: FORBIDDEN_DETAIL }, 403);

    const message = getApiErrorMessage(err, tBg);

    expect(message).toMatch(/[Ѐ-ӿ]/);
    expect(message).not.toContain('Caller lacks permission');
  });

  it('translates both spellings of a 401 rather than leaking its detail', () => {
    const detail = 'Bearer token expired at 2026-08-05T10:00:00Z.';

    for (const title of ['UNAUTHORIZED', 'Unauthorized']) {
      const message = getApiErrorMessage(axiosErrorWith({ title, detail }, 401), t);

      expect(message).toBe('Your session has expired. Please sign in again.');
      expect(message).not.toContain('Bearer token');
      expect(getApiErrorMessage(axiosErrorWith({ title, detail }, 401), tBg)).toMatch(/[Ѐ-ӿ]/);
    }
  });

  it('returns the generic message for a non-Axios error', () => {
    expect(getApiErrorMessage(new Error('boom'), t)).toBe(t('errors.GENERIC_ERROR'));
  });

  it('returns the generic message for an Axios error without a response body', () => {
    expect(getApiErrorMessage(new AxiosError('network down'), t)).toBe(t('errors.GENERIC_ERROR'));
  });
});
